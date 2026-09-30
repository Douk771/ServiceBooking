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
        hash.Should().Be("5c8782a31ad0f9c8caf57f25fe44bbe11788aa69f1bf99fabe4ff88a63ec15df");
    }

    [Fact]
    public void ReleasedEdition_20260926_IsKeptForeverAndUnchanged()
    {
        // The first released edition: TrialGrant rows created before cycle 28 carry this version and hash (§335.4).
        TrialTermsRegistry.Sha256Of(TrialTermsRegistry.LegacyVersion20260926)
            .Should().Be("babf6890d1a403a04d073160c8d3fd031ea319977a200897742207d6e85f50d2");
        TrialTermsRegistry.PromisedThresholdsByVersion[TrialTermsRegistry.LegacyVersion20260926].Should().Equal(7, 3, 1);
    }

    [Fact]
    public void CurrentEdition_IsNewerThanTheLegacyOne_AndSaysMailingsAreNotIncluded()
    {
        // Cycle 28, Q28-4: the trial has no mailings. The sentence promising them is gone, and so is the {3} substitution.
        TrialTermsRegistry.CurrentVersion.Should().NotBe(TrialTermsRegistry.LegacyVersion20260926);
        var current = TrialTermsRegistry.TryGetTemplate(TrialTermsRegistry.CurrentVersion)!;
        current.Should().Contain("Рассылки клиентам в мессенджеры в пробный период не входят; их можно подключить отдельной платной опцией, когда она станет доступна.");
        current.Should().NotContain("{3}");
        current.Should().NotContain("входят в пробный период");
        TrialTermsRegistry.TryGetTemplate(TrialTermsRegistry.LegacyVersion20260926)!.Should().Contain("{3}");
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
        var rendered1 = TrialTermsRegistry.RenderCurrent("Пробный период", 14, new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc));
        var rendered2 = TrialTermsRegistry.RenderCurrent("Другой тариф", 30, new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        rendered1.Should().NotBe(rendered2);
        TrialTermsRegistry.Sha256Of(TrialTermsRegistry.CurrentVersion).Should().Be(TrialTermsRegistry.Sha256Of(TrialTermsRegistry.CurrentVersion));
    }
}
