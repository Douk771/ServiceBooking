using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;

namespace ServiceBooking.UnitTests;

/// <summary>
/// Cycle 7, B3 (ARCHITECTURE_CYCLE7.md §54.3, §44.2) — <c>SeedBillingCatalog</c> (a raw-SQL migration)
/// hardcodes the <c>SubscriptionOption.CapabilityKey</c> values it seeds ("companies", "employees",
/// "notifications.whatsapp") because <see cref="SubscriptionResolver.GetEffectivePlansForAccountsAsync"/>
/// reads those exact strings back out via <see cref="CapabilityKeys"/>. This test is the regression
/// guard: if a capability key is ever renamed on the resolver/<see cref="CapabilityKeys"/> side without
/// updating the migration's raw SQL (or vice versa), the seeded options silently stop doing anything
/// (§44.3 п.6's summed arithmetic reads a CapabilityKey nobody's option has), with no compiler error to
/// catch it — the SQL literals can't reference the C# constant directly.
///
/// This does not replace re-running the migration itself against a real database after a resolver
/// change — it only catches the specific "renamed the string and forgot the other side" mistake at unit
/// test speed, without touching a database or a migration runner.
/// </summary>
public class BillingCatalogSeedKeysTests
{
    [Fact]
    public void WhatsAppOptionCode_MatchesTheLiteralSeedBillingCatalogHardcodes()
    {
        // SeedBillingCatalog inserts the WhatsApp option with Code = CapabilityKey = 'notifications.whatsapp'
        // and looks it back up by that same literal when converting paid channels into option quantity
        // (step 4).
        SubscriptionResolver.WhatsAppOptionCode.Should().Be("notifications.whatsapp");
        CapabilityKeys.NotificationsWhatsApp.Should().Be("notifications.whatsapp");
    }

    [Fact]
    public void CapabilityKeys_MatchTheLiteralsSeedBillingCatalogHardcodes()
    {
        // SeedBillingCatalog seeds the "extra-companies"/"extra-employees" options with CapabilityKey =
        // 'companies'/'employees' (the option's Code keeps the "extra-" prefix; the CapabilityKey does
        // not — §44.2). If either constant here drifts from those literals, the migration's raw SQL and
        // the resolver's Sum(...).Where(o => o.Option.CapabilityKey == ...) lookups fall out of sync.
        CapabilityKeys.Companies.Should().Be("companies");
        CapabilityKeys.Employees.Should().Be("employees");
    }

    [Fact]
    public void OptionCapabilityCatalog_NoLongerOffersTheRetiredLimitKeys()
    {
        // ARCHITECTURE_CYCLE19.md §386.1/§404 — "companies"/"employees" are deliberately removed from
        // the admin-facing dropdown: after cycle 19 nothing may be sold under either capability (the
        // limit is tariff-only, AccountLimitFormula). The migration's already-seeded rows are untouched
        // (§383.2) — this only stops NEW options from being created with these keys.
        var knownKeys = OptionCapabilityCatalog.Known.Select(c => c.Key).ToList();

        knownKeys.Should().NotContain(CapabilityKeys.Companies);
        knownKeys.Should().NotContain(CapabilityKeys.Employees);
        knownKeys.Should().Contain(CapabilityKeys.NotificationsWhatsApp);
    }
}
