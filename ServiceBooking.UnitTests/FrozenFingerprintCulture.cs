using System.Globalization;

namespace ServiceBooking.UnitTests;

/// <summary>
/// The frozen fingerprints of the showcase (cycle 35, R35-6) were computed on a machine with the ru-RU culture: <c>DateOnly</c>, <c>decimal</c> and <c>bool</c>
/// are appended to the hashed string through <c>ToString()</c>, so the hash depends on the culture of the machine (CI runs with the invariant one and got another
/// hash from the same graph). The literals stay as they are; the fingerprint is calculated under the same culture on any machine.
/// </summary>
internal static class FrozenFingerprintCulture
{
    public static string Run(Func<string> compute)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        try { return compute(); }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
