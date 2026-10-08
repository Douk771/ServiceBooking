using System.Text.RegularExpressions;
using FluentAssertions;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

public class StaysPureRulesTests
{
    [Theory]
    [InlineData(StayBookingStatus.Held, StayAction.AttachProof, StayBookingStatus.AwaitingPaymentCheck)]
    [InlineData(StayBookingStatus.AwaitingPaymentCheck, StayAction.AttachProof, StayBookingStatus.AwaitingPaymentCheck)]
    [InlineData(StayBookingStatus.Held, StayAction.Expire, StayBookingStatus.ExpiredUnpaid)]
    [InlineData(StayBookingStatus.AwaitingPaymentCheck, StayAction.ConfirmPayment, StayBookingStatus.Confirmed)]
    [InlineData(StayBookingStatus.AwaitingPaymentCheck, StayAction.RejectPayment, StayBookingStatus.PaymentRejected)]
    [InlineData(StayBookingStatus.Confirmed, StayAction.CancelByGuest, StayBookingStatus.CancelledByGuest)]
    [InlineData(StayBookingStatus.Held, StayAction.CancelByOwner, StayBookingStatus.CancelledByOwner)]
    public void Allowed_transitions(StayBookingStatus from, StayAction action, StayBookingStatus to) =>
        StayStateMachine.Next(from, action).Should().Be(to);

    [Theory]
    [InlineData(StayBookingStatus.Confirmed, StayAction.ConfirmPayment)]
    [InlineData(StayBookingStatus.Confirmed, StayAction.Expire)]
    [InlineData(StayBookingStatus.Held, StayAction.ConfirmPayment)]
    [InlineData(StayBookingStatus.ExpiredUnpaid, StayAction.CancelByOwner)]
    [InlineData(StayBookingStatus.CancelledByGuest, StayAction.AttachProof)]
    [InlineData(StayBookingStatus.Confirmed, StayAction.AttachProof)]
    public void Forbidden_transitions(StayBookingStatus from, StayAction action) =>
        StayStateMachine.Next(from, action).Should().BeNull();

    [Fact]
    public void Terminal_statuses_do_not_occupy() =>
        Enum.GetValues<StayBookingStatus>().Where(StayStateMachine.IsTerminal).Should().BeEquivalentTo(
            [StayBookingStatus.ExpiredUnpaid, StayBookingStatus.PaymentRejected, StayBookingStatus.CancelledByGuest, StayBookingStatus.CancelledByOwner]);

    [Fact]
    public void Completed_is_computed_in_booking_zone()
    {
        // check-out 2026-12-31 12:00 Novokuznetsk (UTC+7) = 05:00Z
        var co = new DateOnly(2026, 12, 31);
        StayStateMachine.DisplayStatus(StayBookingStatus.Confirmed, new DateTime(2026, 12, 31, 4, 59, 0, DateTimeKind.Utc), co, new TimeOnly(12, 0), "Asia/Novokuznetsk").Should().Be("Confirmed");
        StayStateMachine.DisplayStatus(StayBookingStatus.Confirmed, new DateTime(2026, 12, 31, 5, 0, 0, DateTimeKind.Utc), co, new TimeOnly(12, 0), "Asia/Novokuznetsk").Should().Be("Completed");
    }

    [Fact]
    public void Publish_rules_publish_without_registry_number_under_attestation()
    {
        // ЮР-2, customer decision: any kind without a number is published under the owner's attestation.
        foreach (var kind in Enum.GetValues<HouseObjectKind>())
            HousePublishRules.Check(false, true, kind, true).Should().BeNull();
        HousePublishRules.Check(false, true, HouseObjectKind.GuestHouse, false).Should().Be(HousePublishProblem.AttestationRequired);
        HousePublishRules.Check(true, false, null, false).Should().Be(HousePublishProblem.HouseArchived);
        HousePublishRules.Check(false, false, null, true).Should().Be(HousePublishProblem.NoPrice);
        HousePublishRules.Check(false, true, null, true).Should().Be(HousePublishProblem.ObjectKindRequired);
        HousePublishRules.HouseProblems(false, true, HouseObjectKind.GuestHouse).Should().NotContain(HousePublishProblem.RegistryNumberRequired);
    }

    private static readonly StayProviderFacts FullProvider = new(StayProviderStatus.Individual, "Иван", "500100732259", null, "г. Шерегеш");

    [Fact]
    public void Gate_reason_order()
    {
        StaysBookingGate.Evaluate(false, false, 5, 1, 30, null, new(null, null, null, null, null)).ReasonCode.Should().Be(NotAcceptingReason.CompanyBlocked);
        StaysBookingGate.Evaluate(true, false, 5, 1, 30, null, FullProvider).ReasonCode.Should().Be(NotAcceptingReason.NoPlan);
        StaysBookingGate.Evaluate(true, true, 2, 1, 30, null, FullProvider).ReasonCode.Should().Be(NotAcceptingReason.OverHouseLimit);
        StaysBookingGate.Evaluate(true, true, 1, 1, 30, " ", FullProvider).ReasonCode.Should().Be(NotAcceptingReason.NoPaymentDetails);
        StaysBookingGate.Evaluate(true, true, 1, 1, 30, "карта", new(null, null, null, null, null)).ReasonCode.Should().Be(NotAcceptingReason.NoProviderInfo);
        StaysBookingGate.Evaluate(true, true, 1, 1, 30, "карта", FullProvider).Accepting.Should().BeTrue();
        StaysBookingGate.Evaluate(true, true, 1, null, 0, null, new(null, null, null, null, null)).Accepting.Should().BeTrue();
    }

    [Fact]
    public void Gate_requires_ogrn_for_organization_and_entrepreneur()
    {
        StaysBookingGate.ProviderComplete(new(StayProviderStatus.IndividualEntrepreneur, "ИП", "500100732259", null, "адрес")).Should().BeFalse();
        StaysBookingGate.ProviderComplete(new(StayProviderStatus.IndividualEntrepreneur, "ИП", "500100732259", "304500116000157", "адрес")).Should().BeTrue();
    }

    [Fact]
    public void CheckInInfo_release_window()
    {
        var tz = "Asia/Novokuznetsk";
        var ci = new DateOnly(2026, 12, 30);
        var co = new DateOnly(2027, 1, 2);
        bool Due(DateTime utc, bool text = true, bool released = false, StayBookingStatus st = StayBookingStatus.Confirmed) =>
            CheckInInfoRelease.IsDue(st, text, released, tz, utc, ci, new TimeOnly(9, 0), co, new TimeOnly(12, 0));
        Due(new DateTime(2026, 12, 30, 1, 59, 0, DateTimeKind.Utc)).Should().BeFalse(); // 08:59 local
        Due(new DateTime(2026, 12, 30, 2, 0, 0, DateTimeKind.Utc)).Should().BeTrue();
        Due(new DateTime(2027, 1, 2, 5, 0, 0, DateTimeKind.Utc)).Should().BeFalse();
        Due(new DateTime(2026, 12, 30, 3, 0, 0, DateTimeKind.Utc), text: false).Should().BeFalse();
        Due(new DateTime(2026, 12, 30, 3, 0, 0, DateTimeKind.Utc), released: true).Should().BeFalse();
        Due(new DateTime(2026, 12, 30, 3, 0, 0, DateTimeKind.Utc), st: StayBookingStatus.Held).Should().BeFalse();
    }

    [Fact]
    public void Notification_plan_table()
    {
        StayNotificationPlan.ForEvent(StayBookingEventKind.Created).Select(p => p.Type).Should().BeEquivalentTo([NotificationType.StaffStayCreated, NotificationType.StayGuestCreated]);
        StayNotificationPlan.ForEvent(StayBookingEventKind.Created, manualBooking: true).Should().BeEmpty();
        StayNotificationPlan.ForEvent(StayBookingEventKind.PaymentConfirmed).Single().Type.Should().Be(NotificationType.StayGuestConfirmed);
        StayNotificationPlan.ForEvent(StayBookingEventKind.PaymentProofViewed).Should().BeEmpty();
        StayNotificationPlan.ForScheduled(StayScheduledKind.HoldExpiring).Type.Should().Be(NotificationType.StayGuestHoldExpiring);
        // a stay type must never reach the salon bitmask (int, 31 usable bits)
        Enum.GetValues<NotificationType>().Max(t => (int)t).Should().BeLessThan(31);
    }

    [Fact]
    public void Access_table()
    {
        StaysAccess.Has(StaysMyRole.Housekeeper, StaysPermission.ViewSchedule).Should().BeTrue();
        foreach (var p in Enum.GetValues<StaysPermission>().Where(p => p != StaysPermission.ViewSchedule))
            StaysAccess.Has(StaysMyRole.Housekeeper, p).Should().BeFalse();
        StaysAccess.Has(StaysMyRole.Manager, StaysPermission.ManageCompany).Should().BeFalse();
        StaysAccess.Has(StaysMyRole.Manager, StaysPermission.ManageHouses).Should().BeFalse();
        StaysAccess.Has(StaysMyRole.Manager, StaysPermission.ManageBookings).Should().BeTrue();
        StaysAccess.For(StaysMyRole.Owner).Should().HaveCount(8);
    }

    [Fact]
    public void Slug_policy_follows_dom_routes_json()
    {
        StaysSlugPolicy.Validate("kedr-house").Should().Be(SlugCheck.Ok);
        StaysSlugPolicy.Validate("cabinet").Should().Be(SlugCheck.Reserved);
        StaysSlugPolicy.Validate("ab").Should().Be(SlugCheck.Invalid);
        StaysSlugPolicy.Validate("primer-x").Should().Be(SlugCheck.Invalid);
        StaysSlugPolicy.IsValidHouseSlug("a1").Should().BeTrue();
        StaysSlugPolicy.IsValidHouseSlug("A_1").Should().BeFalse();
        foreach (var route in StaysSlugPolicy.SpaRoutes.Where(r => r != "/"))
            StaysSlugPolicy.ReservedSlugs.Should().Contain(SlugPolicy.FirstSegment(route)!);
    }

    [Fact]
    public void Texts_never_use_forbidden_words()
    {
        var words = new Regex("задат|невозвратн|депозит", RegexOptions.IgnoreCase);
        var texts = typeof(StaysTexts).GetFields().Where(f => f.FieldType == typeof(string)).Select(f => (string)f.GetValue(null)!).ToList();
        texts.AddRange(Enum.GetValues<StayCancellationPolicy>().Select(StaysTexts.CancellationSummary));
        texts.AddRange([StaysTexts.RefundFull(1000), StaysTexts.RefundPartial(100, 900), StaysTexts.OwnerCancelRefund(5),
            StaysTexts.OutcomeText(StayBookingStatus.CancelledByOwner, "r", "+7"), StaysTexts.OutcomeText(StayBookingStatus.PaymentRejected, "r", "+7"),
            StaysTexts.OutcomeText(StayBookingStatus.ExpiredUnpaid, null, null), StaysTexts.HoldExpiredMessage("+7"), StaysTexts.CannotCancel("+7")]);
        texts.AddRange(Enum.GetValues<StayRefusalCode>().Select(c => StaysTexts.RefusalMessage(c, 3, 3, new DateOnly(2027, 1, 1), 4)));
        texts.Where(t => words.IsMatch(t)).Should().BeEmpty();
    }

    [Fact]
    public void Cancellation_config_validator_rejects_unlawful_values()
    {
        StayCancellationRule.Validate(StayCancellationRule.Defaults).Should().BeEmpty();
        var bad = new Dictionary<StayCancellationPolicy, StayCancellationRule>(StayCancellationRule.Defaults)
        {
            [StayCancellationPolicy.Standard] = new(CancellationBoundary.CheckInDayStart, 2),
            [StayCancellationPolicy.Flexible] = new(CancellationBoundary.None, 1)
        };
        StayCancellationRule.Validate(bad).Should().HaveCount(2);
    }

    [Theory]
    [InlineData(1, "ночь")] [InlineData(2, "ночи")] [InlineData(5, "ночей")] [InlineData(11, "ночей")] [InlineData(21, "ночь")] [InlineData(24, "ночи")]
    public void Plural(int n, string word) => StaysTexts.Plural(n, "ночь", "ночи", "ночей").Should().Be(word);

    [Fact]
    public void Token_is_43_url_safe_chars()
    {
        var t = PublicStayToken.Generate();
        t.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        PublicStayToken.Generate().Should().NotBe(t);
    }
}
