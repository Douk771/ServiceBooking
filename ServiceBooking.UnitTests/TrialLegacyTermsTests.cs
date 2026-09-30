using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;

namespace ServiceBooking.UnitTests;

/// <summary>
/// Code review, cycle 28 (blocking finding 1): with two editions in the registry, an account granted the trial under the released edition 2026-09-26
/// must see THAT edition rendered with its own four substitutions (never the raw template with literal {0}…{3}) and must be able to acknowledge it.
/// Pure logic — no database.
/// </summary>
public class TrialLegacyTermsTests
{
    private static readonly DateTime EndsAt = new(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);

    private static BillingAccount Account(string? version, int? windowDays = 7, DateTime? acknowledgedAt = null) => new()
    {
        TrialTermsVersion = version,
        TrialDurationDays = 14,
        TrialMailingWindowDays = windowDays,
        TrialStartedAtUtc = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc),
        TrialTermsAcknowledgedAtUtc = acknowledgedAt,
    };

    [Fact]
    public void BuildActivationTerms_LegacyEdition_IsRenderedWithAllFourSubstitutions_NoPlaceholderLeft()
    {
        var terms = TrialStateReader.BuildActivationTerms(Account(TrialTermsRegistry.LegacyVersion20260926, windowDays: 7), "Пробный", EndsAt);

        terms.Version.Should().Be("2026-09-26");
        terms.Sha256.Should().Be("babf6890d1a403a04d073160c8d3fd031ea319977a200897742207d6e85f50d2");
        terms.Text.Should().NotContainAny("{0}", "{1}", "{2}", "{3}");
        terms.Text.Should().Contain("«Пробный»").And.Contain("на 14 дней").And.Contain("до 14.10.2026 включительно")
            .And.Contain("не больше 7 дней с того момента");
        terms.AcknowledgementRequired.Should().BeTrue();
    }

    [Fact]
    public void BuildActivationTerms_CurrentEdition_IsRenderedWithThreeSubstitutions()
    {
        var terms = TrialStateReader.BuildActivationTerms(Account(TrialTermsRegistry.CurrentVersion), "Пробный", EndsAt);

        terms.Version.Should().Be(TrialTermsRegistry.CurrentVersion);
        terms.Text.Should().Be(TrialTermsRegistry.RenderCurrent("Пробный", 14, EndsAt));
        terms.Text.Should().NotContain("{");
    }

    [Fact]
    public void BuildActivationTerms_NoRecordedEdition_FallsBackToTheCurrentOne()
    {
        var terms = TrialStateReader.BuildActivationTerms(Account(null), "Пробный", EndsAt);

        terms.Version.Should().Be(TrialTermsRegistry.CurrentVersion);
        terms.Text.Should().Be(TrialTermsRegistry.RenderCurrent("Пробный", 14, EndsAt));
    }

    [Fact]
    public void BuildActivationTerms_UnknownEdition_GivesEmptyTextAndHash_NotARawTemplate()
    {
        var terms = TrialStateReader.BuildActivationTerms(Account("1999-01-01"), "Пробный", EndsAt);

        terms.Text.Should().BeEmpty();
        terms.Sha256.Should().BeEmpty();
    }

    [Fact]
    public void BuildActivationTerms_Acknowledged_ReportsItAndTheShownAt()
    {
        var acknowledgedAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);
        var account = Account(TrialTermsRegistry.LegacyVersion20260926, acknowledgedAt: acknowledgedAt);

        var terms = TrialStateReader.BuildActivationTerms(account, "Пробный", EndsAt);

        terms.AcknowledgementRequired.Should().BeFalse();
        terms.AcknowledgedAt.Should().Be(acknowledgedAt);
        terms.ShownAt.Should().Be(account.TrialStartedAtUtc);
    }

    [Fact]
    public void Render_LegacyEdition_SubstitutesTheMailingWindow_AndUnknownIsNull()
    {
        TrialTermsRegistry.Render(TrialTermsRegistry.LegacyVersion20260926, "P", 14, EndsAt, 5)!.Should().Contain("не больше 5 дней");
        TrialTermsRegistry.Render("1999-01-01", "P", 14, EndsAt, 5).Should().BeNull();
    }

    [Theory]
    [InlineData("2026-09-30", null, true)]           // current edition: always
    [InlineData("2026-09-30", "2026-09-26", true)]
    [InlineData("2026-09-26", "2026-09-26", true)]   // the edition recorded in the account, shown to the owner
    [InlineData("2026-09-26", null, false)]          // not recorded in the account
    [InlineData("2026-09-26", "2026-09-30", false)]  // another edition than the recorded one
    [InlineData("1999-01-01", "1999-01-01", false)]  // unknown edition is never acknowledgeable
    [InlineData("2027-01-01", null, false)]
    public void IsAcknowledgeable_AcceptsTheCurrentEditionAndTheOneRecordedInTheAccount(string requested, string? recorded, bool expected) =>
        TrialTermsRegistry.IsAcknowledgeable(requested, recorded).Should().Be(expected);
}
