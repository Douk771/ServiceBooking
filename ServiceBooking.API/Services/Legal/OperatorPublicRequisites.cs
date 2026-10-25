using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ServiceBooking.API.Services.Legal;

/// <summary>
/// Public requisites of the operator — the backend's reader of the ONE source
/// <c>contracts/legal/operator-public.json</c> (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.3, API_CONTRACT_CYCLE48_STOREFRONT.md §48.24.4),
/// embedded into the assembly. The frontend, LegalKit (publish/status) and deploy-remote.sh read the same file.
/// </summary>
public static class OperatorPublicRequisites
{
    public const string ResourceName = "operator-public.json";

    /// <summary>Derived placeholder: "+" and all digits of ТЕЛЕФОН_ОПЕРАТОРА. Never stored.</summary>
    public const string TelKey = "ТЕЛЕФОН_TEL";

    private const string PhoneKey = "ТЕЛЕФОН_ОПЕРАТОРА";

    private static readonly Regex Placeholder = new(@"\{\{([^{}]+)\}\}", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private sealed record Loaded(IReadOnlyDictionary<string, string> Values, IReadOnlyList<string> Shared);

    private static readonly Lazy<Loaded> Source = new(Load);

    /// <summary>All 13 site values, ordinal keys.</summary>
    public static IReadOnlyDictionary<string, string> Values => Source.Value.Values;

    /// <summary>The 8 keys that must be equal to legal.values.json (ТЛ48-06).</summary>
    public static IReadOnlyList<string> SharedWithLegalValues => Source.Value.Shared;

    /// <exception cref="KeyNotFoundException">The key is not one of the file's values.</exception>
    public static string Get(string key) =>
        Values.TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException($"Operator requisite '{key}' is not defined in {ResourceName}.");

    /// <summary>"tel:+79132258915" — the same derivation as the frontend's <c>telHref()</c>.</summary>
    public static string TelHref() => "tel:" + TelValue();

    /// <summary>Replaces {{KEY}} (any of the 13 values, and {{ТЕЛЕФОН_TEL}}) in a template.</summary>
    /// <exception cref="InvalidOperationException">The template mentions an unknown key.</exception>
    public static string Fill(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return Placeholder.Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            if (key == TelKey) return TelValue();
            return Values.TryGetValue(key, out var value)
                ? value
                : throw new InvalidOperationException($"Unknown operator requisite placeholder '{{{{{key}}}}}'.");
        });
    }

    private static string TelValue()
    {
        var digits = new StringBuilder("+");
        foreach (var c in Get(PhoneKey))
        {
            if (char.IsAsciiDigit(c)) digits.Append(c);
        }

        return digits.ToString();
    }

    private static Loaded Load()
    {
        using var stream = typeof(OperatorPublicRequisites).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' is missing — check the EmbeddedResource item in ServiceBooking.API.csproj.");
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        var values = root.GetProperty("values").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
        var shared = root.GetProperty("sharedWithLegalValues").EnumerateArray()
            .Select(e => e.GetString()!).ToList();
        return new Loaded(values, shared);
    }
}
