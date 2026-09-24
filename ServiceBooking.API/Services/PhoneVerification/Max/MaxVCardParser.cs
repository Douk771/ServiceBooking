using System.Text;

namespace ServiceBooking.API.Services.PhoneVerification.Max;

/// <summary>
/// Pure vCard <c>TEL</c> extraction (ARCHITECTURE_CYCLE12.md §147.3, Q11). The rule is deliberately
/// simple and entirely test-fixed: every <c>TEL</c> property, canonicalized, <c>TYPE=CELL</c>/
/// <c>TYPE=MOBILE</c> ones first, everything else afterward in the order it appeared. A match against the
/// session's phone is then searched for among ALL of them (§147.3 p.4) — a person with two numbers in
/// their card must not be rejected just because the mobile one happens to come second.
/// </summary>
public static class MaxVCardParser
{
    /// <summary>§147.3 p.6 — more <c>TEL</c> properties than this and the whole card is treated as
    /// having none (<see cref="Core.Enums.PhoneVerificationFailureReason.NoPhoneInContact"/>), not parsed
    /// partially. A body this vCard couldn't possibly need this many phone numbers for is itself a signal
    /// something is wrong with the input.</summary>
    public const int MaxTelProperties = 20;

    /// <summary>
    /// </summary>
    /// <param name="vcfInfo">Raw vCard text as delivered in the MAX update.</param>
    /// <param name="maxBytes"><c>PhoneVerification:Max:MaxVcardBytes</c> — a body larger than this is
    /// treated the same as one with no usable <c>TEL</c> at all, without ever being parsed.</param>
    /// <returns>Canonical (<c>PhoneNormalizer.TryNormalizeRussian</c>) phone numbers, mobile-typed ones
    /// first, in the order they appeared. Empty when nothing usable was found — the caller maps that to
    /// <c>NoPhoneInContact</c>.</returns>
    public static IReadOnlyList<string> ExtractPhones(string? vcfInfo, int maxBytes)
    {
        if (string.IsNullOrWhiteSpace(vcfInfo)) return [];
        if (Encoding.UTF8.GetByteCount(vcfInfo) > maxBytes) return [];

        var lines = Unfold(vcfInfo)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var mobile = new List<string>();
        var other = new List<string>();
        var telCount = 0;

        foreach (var line in lines)
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex < 0) continue;

            var head = line[..colonIndex];
            var value = line[(colonIndex + 1)..];

            var headParts = head.Split(';');
            if (!headParts[0].Equals("TEL", StringComparison.OrdinalIgnoreCase)) continue;

            telCount++;
            if (telCount > MaxTelProperties) return []; // §147.3 p.6 — whole card discarded, not truncated

            if (!ServiceBooking.API.Services.PhoneNormalizer.TryNormalizeRussian(value, out var canonical)) continue;

            var isMobile = headParts.Skip(1).Any(p =>
                p.Contains("CELL", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("MOBILE", StringComparison.OrdinalIgnoreCase));

            (isMobile ? mobile : other).Add(canonical);
        }

        return [.. mobile, .. other];
    }

    /// <summary>vCard line folding (RFC 6350 §3.2): a continuation line starts with exactly one space or
    /// tab, which must be removed before the logical line is complete.</summary>
    private static string Unfold(string vcard) =>
        vcard.Replace("\r\n", "\n").Replace("\n ", "").Replace("\n\t", "");
}
