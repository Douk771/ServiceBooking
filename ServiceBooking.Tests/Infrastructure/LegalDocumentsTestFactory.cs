using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// A second, independent host pointed at a throw-away copy of App_Data/legal, so LEG- tests can rewrite
/// legal.json/*.html on disk mid-test (material vs. editorial version bumps, a live "swap the file, no
/// rebuild" replacement) without disturbing the documents every other functional test registers/logs in
/// against. ReloadSeconds is set to 1 so tests don't have to sleep 30s per version bump.
///
/// Shares the same Postgres database as the main "Api" collection (TestDatabaseFixture wipes it exactly
/// once for the whole assembly) — this factory never calls EnsureDeletedAsync itself, only boots its own
/// in-process TestServer against the already-migrated schema. Assembly-level
/// [CollectionBehavior(DisableTestParallelization = true)] guarantees no other collection's tests run
/// concurrently with this one, so the two hosts never race on the same rows.
///
/// Root-caused during a QA pass on a LATER cycle (see that cycle's CURRENT_STATE.md/report for the
/// full trace): <c>SuperAdmin:Phone</c> is deliberately its OWN value here, distinct from every other
/// factory's shared "+70000000001". Program.cs seeds the SuperAdmin account — including its
/// <c>UserConsent</c> rows, stamped with whichever <c>Legal:Root</c> manifest THIS PARTICULAR host
/// happened to have loaded — exactly once per phone number, on whichever host's startup reaches that
/// code FIRST after the database is wiped (`if (admin is null)`, Program.cs). This factory's
/// <c>Legal:Root</c> is a throw-away temp directory with an intentionally one-off, random version tag
/// (<see cref="ResetToDefault"/>) — if it ever won that race using the SAME phone as every other
/// factory, the shared SuperAdmin's consent would be permanently stamped with a version nothing else
/// recognizes, and <c>LegalConsentFilter</c> would then 451 every OTHER test's SuperAdmin-authenticated
/// request for the rest of that process, non-deterministically depending on collection execution order.
/// A dedicated phone means this factory's random manifest can never poison the shared account no matter
/// which host boots first — every OTHER factory still points at the default/canonical
/// <c>App_Data/legal</c> (or an explicit copy of it, see <c>UploadsStaticFilesTestFactory</c>), so
/// THEIR seeding races each other harmlessly onto a consistent, matching version.
/// </summary>
public sealed class LegalDocumentsTestFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public string LegalRoot { get; }

    public LegalDocumentsTestFactory(string connectionString)
    {
        _connectionString = connectionString;
        LegalRoot = Path.Combine(Path.GetTempPath(), "sb-legal-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(LegalRoot);
        ResetToDefault();
    }

    /// <summary>Restores the manifest to five Material, draft documents (+ six ui-texts) with a fresh
    /// version tag (unique per call so tests don't collide with whatever a previous test in this run
    /// already accepted). Only Privacy/TermsClient's version is what LEG- tests bump in this file — the
    /// other three documents and the six texts get their own, always-valid version so
    /// LegalDocumentProvider.LoadSnapshot's "all five types / all six keys present" requirement
    /// (LegalDocumentProvider.cs's own LoadSnapshot) is satisfied on every call, not just the first.</summary>
    public string ResetToDefault()
    {
        var version = "test-" + DateTime.UtcNow.Ticks + "-draft";
        WriteManifest(version, isDraft: true, changeKind: "Material");
        return version;
    }

    /// <summary>
    /// CYCLE5: rewritten against the five-document/six-text manifest LegalDocumentProvider.LoadSnapshot
    /// now requires (was two documents, "Privacy"/"Terms") — every LEG- test in this file only ever cares
    /// about Privacy/TermsClient's version/isDraft/changeKind (the two `gate: Global` documents a Material
    /// change on either one actually blocks), so those two are the ones driven by this method's
    /// parameters; TermsOwner/PdnConsent/ChannelRiskNotice and the six ui-texts are written with their own
    /// fixed, always-non-draft version so they never trip the "isDraft without -draft suffix" or
    /// "PdnConsent needs purposes" checks. `version` must already end with "-draft" if isDraft is true —
    /// the provider rejects an isDraft document whose version doesn't carry the suffix
    /// (LegalDocumentProvider.LoadDocument), which is itself covered by LEG-018 below.
    ///
    /// QA cycle 11 addition: <paramref name="termsOwnerIsDraft"/>/<paramref name="termsOwnerVersion"/>
    /// let LegalPricingGateTests flip TermsOwner (the channel offer's carrier document, §102.2) between
    /// draft and published without touching the Privacy/TermsClient behaviour every other LEG- test in
    /// this file relies on. Default (false / "fixed-owner-1") is byte-for-byte what every pre-existing
    /// caller already got.
    /// </summary>
    public void WriteManifest(string version, bool isDraft, string changeKind, string? bodyMarker = null,
        bool termsOwnerIsDraft = false, string termsOwnerVersion = "fixed-owner-1")
    {
        File.WriteAllText(Path.Combine(LegalRoot, "privacy.html"),
            $"<p>{(isDraft ? "<strong>Черновая редакция.</strong> " : "")}Политика {version}. {bodyMarker}</p>");
        File.WriteAllText(Path.Combine(LegalRoot, "terms.html"),
            $"<p>{(isDraft ? "<strong>Черновая редакция.</strong> " : "")}Соглашение {version}. {bodyMarker}</p>");
        File.WriteAllText(Path.Combine(LegalRoot, "terms-owner.html"),
            $"<p>{(termsOwnerIsDraft ? "<strong>Черновая редакция.</strong> " : "")}Соглашение с владельцем {termsOwnerVersion}.</p>");
        foreach (var (file, title) in new[]
                 {
                     ("pdn-consent.html", "Согласие на обработку ПДн"),
                     ("channel-risk-notice.html", "Уведомление о рисках канала"),
                     ("booking-notice.html", "Уведомление при записи"), ("template-ad-warning.html", "Предупреждение о рекламе"),
                     ("unsubscribe-page.html", "Страница отписки"), ("photo-consent.html", "Согласие на фото"),
                     ("health-data-consent.html", "Согласие на сведения о здоровье"),
                     ("guardian-confirmation.html", "Подтверждение полномочий"),
                     ("public-address-notice.html", "Предупреждение о публичности адреса"),
                 })
            File.WriteAllText(Path.Combine(LegalRoot, file), $"<p>{title}.</p>");

        File.WriteAllText(Path.Combine(LegalRoot, "legal.json"), $$"""
        {
          "documents": [
            { "type": "Privacy", "version": "{{version}}", "effectiveFrom": "2026-01-01", "isDraft": {{isDraft.ToString().ToLowerInvariant()}}, "changeKind": "{{changeKind}}", "gate": "Global", "title": "Политика обработки персональных данных", "file": "privacy.html" },
            { "type": "TermsClient", "version": "{{version}}", "effectiveFrom": "2026-01-01", "isDraft": {{isDraft.ToString().ToLowerInvariant()}}, "changeKind": "{{changeKind}}", "gate": "Global", "title": "Пользовательское соглашение", "file": "terms.html" },
            { "type": "TermsOwner", "version": "{{termsOwnerVersion}}", "effectiveFrom": "2026-01-01", "isDraft": {{termsOwnerIsDraft.ToString().ToLowerInvariant()}}, "changeKind": "Material", "gate": "OwnerScope", "title": "Соглашение владельца", "file": "terms-owner.html" },
            { "type": "PdnConsent", "version": "fixed-pdn-1", "effectiveFrom": "2026-01-01", "isDraft": false, "changeKind": "Material", "gate": "None", "title": "Согласие на обработку ПДн", "file": "pdn-consent.html",
              "purposes": [
                { "key": "ProviderDelivery", "title": "Передача привлекаемым лицам для доставки уведомлений" },
                { "key": "WorkPhotos", "title": "Фотофиксация выполненной работы" },
                { "key": "HealthData", "title": "Обработка сведений о состоянии здоровья" }
              ] },
            { "type": "ChannelRiskNotice", "version": "fixed-risk-1", "effectiveFrom": "2026-01-01", "isDraft": false, "changeKind": "Material", "gate": "None", "title": "Уведомление о рисках канала", "file": "channel-risk-notice.html" }
          ],
          "uiTexts": [
            { "key": "BookingNotice", "version": "fixed-text-1", "isDraft": false, "file": "booking-notice.html" },
            { "key": "TemplateAdWarning", "version": "fixed-text-1", "isDraft": false, "file": "template-ad-warning.html" },
            { "key": "UnsubscribePage", "version": "fixed-text-1", "isDraft": false, "file": "unsubscribe-page.html" },
            { "key": "PhotoConsent", "version": "fixed-text-1", "isDraft": false, "file": "photo-consent.html" },
            { "key": "HealthDataConsent", "version": "fixed-text-1", "isDraft": false, "file": "health-data-consent.html" },
            { "key": "GuardianConfirmation", "version": "fixed-text-1", "isDraft": false, "file": "guardian-confirmation.html" },
            { "key": "PublicAddressNotice", "version": "fixed-text-1", "isDraft": false, "file": "public-address-notice.html" }
          ]
        }
        """);

        // File.WriteAllText can land within the same mtime tick as the previous write on some
        // filesystems; the provider's cache key is purely mtime-based (LegalDocumentProvider.TryReload),
        // so back-to-back rewrites in the same test need a nudge to guarantee a new mtime is observed.
        File.SetLastWriteTimeUtc(Path.Combine(LegalRoot, "legal.json"), DateTime.UtcNow);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Deliberately NOT the shared "api" factoryTag's SuperAdmin — see this class's own doc comment
        // for why colliding on that phone here specifically poisons every other test's SuperAdmin login
        // with a UserConsent version nothing else recognizes. No test in this class ever logs in as
        // SuperAdmin, so a dedicated, never-asserted-on phone (TestHostSettings' "legal" entry) costs
        // nothing here.
        TestHostSettings.Apply(builder, "legal", _connectionString);

        // Overrides TestHostSettings' own copy of App_Data/legal — this factory manages its own
        // rewritable manifest (ResetToDefault/WriteManifest) so LEG- tests can bump versions mid-test.
        builder.UseSetting("Legal:Root", LegalRoot);
        builder.UseSetting("Legal:ReloadSeconds", "1");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(LegalRoot, recursive: true); } catch { /* best effort */ }
    }
}
