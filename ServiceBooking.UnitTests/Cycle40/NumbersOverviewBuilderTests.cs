using FluentAssertions;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests.Cycle40;

/// <summary>API_CONTRACT_CYCLE40.md §40.22 — how the facts (payment, terms, availability) become the ChannelDto fields; the database-free part of
/// <see cref="NumbersOverviewBuilder"/>.</summary>
public class NumbersOverviewBuilderTests
{
    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly Guid AccountId = Guid.NewGuid();
    private const string RiskVersion = "2026-10-20";

    private static NotificationChannel Channel(
        ChannelState state = ChannelState.NotConnected, bool terms = true, DateTime? requestedAt = null) => new()
    {
        Id = Guid.NewGuid(), BillingAccountId = AccountId, Transport = NotificationTransport.Max, State = state,
        PhoneNumber = state == ChannelState.Connected ? "79991234567" : null,
        LegalEntityForm = terms ? LegalEntityForm.Ip : null, Inn = terms ? "7707083893" : null,
        RiskAcceptedVersion = terms ? RiskVersion : null, RequestedAtUtc = requestedAt,
        CreatedAt = Now.AddDays(-3),
    };

    private static NumbersContext Context(
        NotificationChannel channel, OptionRowFacts? option, bool offerConsent = true, bool sellable = true, bool platformEnabled = true)
    {
        var max = AccountMessagingReader.BuildTransport(
            NotificationTransport.Max, option, null, new LegacySubscriptionFacts(false, null), Now, [channel],
            new TransportOptionState(true, sellable, 490m), platformEnabled);
        var whatsapp = AccountMessagingReader.BuildTransport(
            NotificationTransport.WhatsApp, null, null, new LegacySubscriptionFacts(false, null), Now, [],
            new TransportOptionState(false, false, null), platformEnabled);
        return new NumbersContext
        {
            States = new Dictionary<Guid, AccountMessagingState> { [AccountId] = new(AccountId, platformEnabled, whatsapp, max, [channel]) },
            CompaniesByAccount = new Dictionary<Guid, IReadOnlyList<Company>>
            {
                [AccountId] = [new Company { Id = Guid.NewGuid(), Name = "Салон «Лён»", IsActive = true }, new Company { Id = Guid.NewGuid(), Name = "Магазин", IsActive = false }],
            },
            IdleDays = 14, OwnerPhoneMasked = "+7 999 ***-**-33", RiskVersion = RiskVersion, OfferVersion = "2026-10-20",
            OfferConsentCurrent = offerConsent, TestFailureDetails = new Dictionary<Guid, string?>(),
        };
    }

    private static OptionRowFacts PaidRow(bool trial = false) =>
        new(null, Now.AddDays(20), trial, Now.AddDays(-5));

    [Fact]
    public void TermsAccepted_NeedsRiskVersionStatusInnAndCurrentOffer()
    {
        var ok = Channel();
        NumbersOverviewBuilder.TermsAccepted(ok, Context(ok, PaidRow())).Should().BeTrue();

        NumbersOverviewBuilder.TermsAccepted(ok, Context(ok, PaidRow(), offerConsent: false)).Should().BeFalse("offer consent is not of the current edition");

        var oldRisk = Channel(); oldRisk.RiskAcceptedVersion = "2025-01-01";
        NumbersOverviewBuilder.TermsAccepted(oldRisk, Context(oldRisk, PaidRow())).Should().BeFalse();

        var noInn = Channel(); noInn.Inn = null;
        NumbersOverviewBuilder.TermsAccepted(noInn, Context(noInn, PaidRow())).Should().BeFalse();

        var noForm = Channel(); noForm.LegalEntityForm = null;
        NumbersOverviewBuilder.TermsAccepted(noForm, Context(noForm, PaidRow())).Should().BeFalse();
    }

    [Fact]
    public void PaidNumberWithoutTerms_AsksForTerms_AndCannotConnectYet()
    {
        var channel = Channel(terms: false);
        var dto = NumbersOverviewBuilder.BuildChannel(channel, Context(channel, PaidRow(trial: true)));

        dto.DisplayStatus.Should().Be(ChannelDisplayStatus.ActionRequired);
        dto.Action.Should().Be(ChannelAction.AcceptTerms);
        dto.DisplayText.Should().Be("Примите условия подключения MAX");
        dto.CanConnect.Should().BeFalse();
        dto.IsTrial.Should().BeTrue();
        dto.PaymentState.Should().Be(ChannelPaymentStatus.Paid);
    }

    [Fact]
    public void PaidNumberWithTerms_CanConnect_AndCompaniesAreAllOfTheAccount()
    {
        var channel = Channel();
        var dto = NumbersOverviewBuilder.BuildChannel(channel, Context(channel, PaidRow()));

        dto.CanConnect.Should().BeTrue();
        dto.Action.Should().Be(ChannelAction.BindNumber);
        dto.CanReplace.Should().BeFalse("a number that was never bound cannot be replaced");
        dto.Companies.Should().HaveCount(2);
        dto.StateText.Should().Be("Номер не привязан");
    }

    [Fact]
    public void ConnectedNumber_Works_AndMayBeReplaced()
    {
        var channel = Channel(ChannelState.Connected);
        channel.LastTestResult = ChannelTestResult.Sent;
        channel.LastTestResultAtUtc = Now;
        var dto = NumbersOverviewBuilder.BuildChannel(channel, Context(channel, PaidRow()));

        dto.DisplayStatus.Should().Be(ChannelDisplayStatus.Working);
        dto.CanReplace.Should().BeTrue();
        dto.CanConnect.Should().BeFalse();
        dto.LastTest.Should().NotBeNull();
        dto.LastTest!.Text.Should().Be("Проверочное сообщение отправлено на ваш номер +7 999 ***-**-33");
    }

    [Fact]
    public void UnpaidNumberWithNewerRequest_IsUnderReview()
    {
        var channel = Channel(requestedAt: Now.AddHours(-1));
        var dto = NumbersOverviewBuilder.BuildChannel(channel, Context(channel, null));

        dto.PaymentPending.Should().BeTrue();
        dto.DisplayText.Should().Be("Оплата на проверке");
        dto.PaymentState.Should().Be(ChannelPaymentStatus.NotPaid);
    }

    [Fact]
    public void UnpaidNumberOfAClosedOption_IsUnavailable()
    {
        var channel = Channel();
        var dto = NumbersOverviewBuilder.BuildChannel(channel, Context(channel, null, sellable: false));

        dto.DisplayText.Should().Be("Подключение MAX временно недоступно");
        dto.Action.Should().BeNull();
    }

    [Fact]
    public void PlatformOff_ShowsOff_ForEveryNumber()
    {
        var channel = Channel(ChannelState.Connected);
        var dto = NumbersOverviewBuilder.BuildChannel(channel, Context(channel, PaidRow(), platformEnabled: false));
        dto.DisplayStatus.Should().Be(ChannelDisplayStatus.Off);
        dto.DisplayText.Should().Be("Рассылки временно отключены платформой");
    }

    [Fact]
    public void UnchangedLineOfAChannelOption_IsRecognized_TrialRowAndPaidRowAlike()
    {
        var trial = new AccountSubscriptionOption { Quantity = 1, GrantedByTrial = true };
        var line = new AssignOptionInput(Guid.NewGuid(), 1, null);

        AdminBillingController.IsUnchangedChannelOptionLine(trial, line).Should().BeTrue();
        AdminBillingController.IsUnchangedChannelOptionLine(trial, line with { Quantity = 2 }).Should().BeFalse("the quantity changed");
        AdminBillingController.IsUnchangedChannelOptionLine(trial, line with { PaidUntil = new DateOnly(2026, 12, 1) }).Should().BeFalse("a date was given: this is a purchase");
        // A row the admin confirmed ("Подтвердить оплату") has its own date: the "Назначить подписку" form sends it back without one — unchanged, not a 400.
        AdminBillingController.IsUnchangedChannelOptionLine(new AccountSubscriptionOption { Quantity = 1, PaidUntilUtc = Now }, line).Should().BeTrue("a paid row sent back as read stays as it is");
        AdminBillingController.IsUnchangedChannelOptionLine(new AccountSubscriptionOption { Quantity = 1, GrantedByTrial = true, EndsAtUtc = Now }, line).Should().BeFalse("an ending row is not unchanged");
        AdminBillingController.IsUnchangedChannelOptionLine(null, line).Should().BeFalse("a NEW row needs its date");
    }
}
