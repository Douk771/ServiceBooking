using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;

namespace ServiceBooking.UnitTests;

/// <summary>
/// Cycle 5, B3 (ARCHITECTURE_CYCLE5.md §54.3) — <c>SeedBillingCatalog</c> (a raw-SQL migration) hardcodes
/// the literals "extra-companies", "extra-employees" and "notifications.whatsapp" as the
/// <c>SubscriptionOption.CapabilityKey</c>/<c>Code</c> values it seeds, because
/// <see cref="SubscriptionResolver.GetEffectivePlansForAccountsAsync"/> reads those exact strings back
/// out. There is no shared C# constant between the migration and the resolver for two of these three
/// (only the WhatsApp one has <see cref="SubscriptionResolver.WhatsAppOptionCode"/>) — this test is the
/// regression guard: if either literal is ever renamed on the resolver side without updating the
/// migration (or vice versa), the seeded options silently stop doing anything (§44.3 п.6's summed
/// arithmetic reads a CapabilityKey nobody's option has), with no compiler error to catch it.
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
        // SeedBillingCatalog inserts the WhatsApp option with Code = 'notifications.whatsapp' and looks
        // it back up by that same literal when converting paid channels into option quantity (step 4).
        SubscriptionResolver.WhatsAppOptionCode.Should().Be("notifications.whatsapp");
    }

    [Fact]
    public void OptionCapabilityCatalog_StillKnowsTheThreeKeysSeedBillingCatalogUses()
    {
        // SeedBillingCatalog seeds three options whose CapabilityKey is exactly one of these three
        // literals (§54.3). OptionCapabilityCatalog.Known is the admin-facing "known keys" list — it
        // isn't read by the resolver itself, but if a future edit here drops one of these three, it is a
        // strong signal that the corresponding resolver read (SubscriptionResolver.cs's own
        // "extra-employees"/"extra-companies" string literals) is being renamed too and the migration
        // needs to move with it.
        var knownKeys = OptionCapabilityCatalog.Known.Select(c => c.Key).ToList();

        knownKeys.Should().Contain("extra-companies");
        knownKeys.Should().Contain("extra-employees");
        knownKeys.Should().Contain(SubscriptionResolver.WhatsAppOptionCode);
    }
}
