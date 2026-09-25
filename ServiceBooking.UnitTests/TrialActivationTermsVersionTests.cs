using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using Xunit.Abstractions;

namespace ServiceBooking.UnitTests;

/// <summary>
/// Т1 (ARCHITECTURE_CYCLE18.md §335.4, §338.4) — pins the hash of every RELEASED terms edition. An
/// accidental edit to a released template must fail this test loudly: "правка выпущенной редакции
/// запрещена: добавьте новую версию, старую не удаляйте" (§335.4). This is cheaper than any external
/// integrity tool and is what stops a released promise from silently changing.
/// </summary>
public class TrialActivationTermsVersionTests(ITestOutputHelper output)
{
    [Fact]
    public void CurrentVersion_MatchesPinnedHash()
    {
        // 🔴 If this assertion fails because you edited TrialLegalNotices.TrialActivationTerms for the
        // CURRENT version: STOP. Add a new dated version to TrialTermsRegistry instead — the old text
        // stays forever (§335.4). Do not just update this hash to match your edit.
        var hash = TrialTermsRegistry.Sha256Of(TrialTermsRegistry.CurrentVersion);
        output.WriteLine($"{TrialTermsRegistry.CurrentVersion} => {hash}");
        hash.Should().Be("babf6890d1a403a04d073160c8d3fd031ea319977a200897742207d6e85f50d2");
    }

    [Fact]
    public void CurrentVersion_PromisedThresholds_MatchTextLiterally()
    {
        // The text says "за 7, 3 и 1 день" literally — PromisedThresholdsByVersion must never drift
        // from what the released sentence says (§338.4 п.6).
        TrialTermsRegistry.CurrentPromisedThresholds.Should().BeEquivalentTo(new[] { 7, 3, 1 }, o => o.WithStrictOrdering());
    }

    [Fact]
    public void UnknownVersion_TemplateAndHash_AreNull()
    {
        TrialTermsRegistry.TryGetTemplate("1999-01-01").Should().BeNull();
        TrialTermsRegistry.Sha256Of("1999-01-01").Should().BeNull();
    }

    [Fact]
    public void HashIsComputedFromTemplate_NotFromRenderedText()
    {
        // Rendering the SAME version with different substitutions must not change its hash — the hash
        // answers "which edition", not "which activation" (§335.4).
        var rendered1 = TrialTermsRegistry.RenderCurrent("Пробный период", 14, new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc), 7);
        var rendered2 = TrialTermsRegistry.RenderCurrent("Другой тариф", 30, new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 3);
        rendered1.Should().NotBe(rendered2);
        TrialTermsRegistry.Sha256Of(TrialTermsRegistry.CurrentVersion).Should().Be(TrialTermsRegistry.Sha256Of(TrialTermsRegistry.CurrentVersion));
    }
}
