using System.Security.Cryptography;
using System.Text;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// Cycle 18, Т1 (ARCHITECTURE_CYCLE18.md §335.4, §338.4) — versioning of
/// <see cref="TrialLegalNotices.TrialActivationTerms"/> and the promised warning thresholds for each
/// edition. 🔴 A released edition is NEVER deleted or edited: <c>TrialGrant</c> rows carry its version
/// and hash, and without the template there would be nothing to verify that hash against. Changing the
/// text means adding a NEW entry here and bumping <see cref="CurrentVersion"/> — old entries stay
/// forever. "TrialActivationTermsVersionTests" pins the hash of every released version so
/// an accidental edit fails loudly instead of silently changing what a past grant is proven to have
/// shown.
/// </summary>
public static class TrialTermsRegistry
{
    /// <summary>The edition currently shown to owners. Format <c>yyyy-MM-dd</c>.</summary>
    public const string CurrentVersion = "2026-09-26";

    private static readonly IReadOnlyDictionary<string, string> TextsByVersion =
        new Dictionary<string, string> { [CurrentVersion] = TrialLegalNotices.TrialActivationTerms };

    /// <summary>Д19 — which warning thresholds (in days) this edition literally promises ("за 7, 3 и 1
    /// день"). <c>PUT /api/admin/platform-settings</c> must reject a thresholds value that disagrees
    /// with the CURRENT edition's promise (§339.1).</summary>
    public static readonly IReadOnlyDictionary<string, int[]> PromisedThresholdsByVersion =
        new Dictionary<string, int[]> { [CurrentVersion] = [7, 3, 1] };

    public static int[] CurrentPromisedThresholds => PromisedThresholdsByVersion[CurrentVersion];

    /// <summary>Raw, unsubstituted template for a given version, or null if the version is unknown.</summary>
    public static string? TryGetTemplate(string version) => TextsByVersion.GetValueOrDefault(version);

    /// <summary>
    /// SHA-256 of the TEMPLATE bytes (UTF-8), lowercase hex — deliberately hashed BEFORE substituting
    /// {0}-{3}, so the hash answers "is this edition of the text?" rather than depending on the plan
    /// name/duration/date that happened to be substituted for one particular activation.
    /// </summary>
    public static string? Sha256Of(string version)
    {
        var template = TryGetTemplate(version);
        if (template is null) return null;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(template));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Renders the CURRENT edition's template with the four substitutions
    /// (§4.1: план, длительность, дата окончания, окно рассылок).</summary>
    public static string RenderCurrent(string planName, int durationDays, DateTime endsAtUtc, int mailingWindowDays) =>
        string.Format(TextsByVersion[CurrentVersion], planName, durationDays, endsAtUtc.ToString("dd.MM.yyyy"), mailingWindowDays);
}
