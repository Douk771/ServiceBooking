namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.11.1, Т40-L-12 — the version stored with a messenger tick when the lawyer's text is not in the manifest yet:
/// <c>fallback:&lt;text key&gt;:&lt;revision&gt;</c>. The fallback texts live in the frontend (they must be shown before any lawyer's text exists), so the SERVER cannot hash
/// them; instead <see cref="FallbackRevision"/> names the edition of the fallback texts the person saw, and a frontend test
/// (<c>messengerOptInFallbackRevision.test.ts</c>) pins a hash of those texts — changing a fallback text fails that test until this revision is raised, so the
/// stored version always says WHICH wording the person ticked.
/// </summary>
public static class MessengerConsentVersions
{
    /// <summary>Raise it (and the pinned hash in the frontend test) whenever a fallback text of the messenger tick changes.</summary>
    public const string FallbackRevision = "2026-10-09";

    public static string Fallback(string textKey) => $"fallback:{textKey}:{FallbackRevision}";
}
