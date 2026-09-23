using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// No DB, no HTTP — a temp directory standing in for App_Data/legal, exercised directly through
/// LegalDocumentProvider's public surface (ARCHITECTURE.md §4.3/§4.4, extended by
/// ARCHITECTURE_CYCLE5.md §43.3 to five document types and six uiTexts keys).
/// </summary>
public class LegalDocumentProviderTests : IDisposable
{
    private readonly string _root;

    public LegalDocumentProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sb-legal-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private LegalDocumentProvider CreateProvider(int reloadSeconds = 0) =>
        new(Options.Create(new LegalOptions { Root = _root, ReloadSeconds = reloadSeconds }),
            new FakeWebHostEnvironment(),
            NullLogger<LegalDocumentProvider>.Instance);

    private void WriteManifest(string json) => File.WriteAllText(Path.Combine(_root, "legal.json"), json);
    private void WriteDoc(string fileName, string html) => File.WriteAllText(Path.Combine(_root, fileName), html);

    // CYCLE5-BREAKING: "Terms" (cycle 3's JSON string) is renamed to "TermsClient", and a valid manifest
    // now requires all five document types plus all six uiTexts keys to load ANY of them
    // (ARCHITECTURE_CYCLE5.md §43.3) — a two-document manifest that used to be "valid" is not any more.
    // This helper writes the full five/six manifest every test needs, and also drops content files for
    // the three new documents and six texts so every test starts from a state that actually loads.
    private static string ValidManifest(string privacyVersion = "2026-09-08-draft", string termsVersion = "2026-09-08-draft") => $$"""
        {
          "documents": [
            { "type": "Privacy", "version": "{{privacyVersion}}", "effectiveFrom": "2026-09-08", "isDraft": true, "changeKind": "Material", "gate": "Global", "title": "Privacy", "file": "privacy.html" },
            { "type": "TermsClient", "version": "{{termsVersion}}", "effectiveFrom": "2026-09-08", "isDraft": true, "changeKind": "Material", "gate": "Global", "title": "Terms", "file": "terms.html" },
            { "type": "TermsOwner", "version": "v1-draft", "effectiveFrom": "2026-09-08", "isDraft": true, "changeKind": "Material", "gate": "OwnerScope", "title": "Terms owner", "file": "terms-owner.html" },
            { "type": "PdnConsent", "version": "v1-draft", "effectiveFrom": "2026-09-08", "isDraft": true, "changeKind": "Material", "gate": "None", "title": "Pdn", "file": "pdn.html", "purposes": [ { "key": "ProviderDelivery", "title": "Доставка" }, { "key": "WorkPhotos", "title": "Фото" }, { "key": "HealthData", "title": "Здоровье" } ] },
            { "type": "ChannelRiskNotice", "version": "v1-draft", "effectiveFrom": "2026-09-08", "isDraft": true, "changeKind": "Material", "gate": "None", "title": "Risk", "file": "risk.html" }
          ],
          "uiTexts": [
            { "key": "BookingNotice", "version": "v1-draft", "isDraft": true, "file": "booking-notice.html" },
            { "key": "TemplateAdWarning", "version": "v1-draft", "isDraft": true, "file": "ad-warning.html" },
            { "key": "UnsubscribePage", "version": "v1-draft", "isDraft": true, "file": "unsubscribe.html" },
            { "key": "PhotoConsent", "version": "v1-draft", "isDraft": true, "file": "photo-consent.html" },
            { "key": "HealthDataConsent", "version": "v1-draft", "isDraft": true, "file": "health-consent.html" },
            { "key": "GuardianConfirmation", "version": "v1-draft", "isDraft": true, "file": "guardian.html" }
          ]
        }
        """;

    /// <summary>Content files for every entry ValidManifest() references, beyond privacy.html/terms.html
    /// which each test writes itself (their content is what most tests actually assert on).</summary>
    private void WriteRemainingDefaultDocs()
    {
        WriteDoc("terms-owner.html", "<p>owner</p>");
        WriteDoc("pdn.html", "<p>pdn</p>");
        WriteDoc("risk.html", "<p>risk</p>");
        WriteDoc("booking-notice.html", "<p>booking notice</p>");
        WriteDoc("ad-warning.html", "<p>ad warning</p>");
        WriteDoc("unsubscribe.html", "<p>unsubscribe</p>");
        WriteDoc("photo-consent.html", "<p>photo consent</p>");
        WriteDoc("health-consent.html", "<p>health consent</p>");
        WriteDoc("guardian.html", "<p>guardian</p>");
    }

    [Fact]
    public void Current_ValidManifest_ReturnsAllFiveDocumentsAndSixTexts()
    {
        WriteManifest(ValidManifest());
        WriteDoc("privacy.html", "<p>privacy text</p>");
        WriteDoc("terms.html", "<p>terms text</p>");
        WriteRemainingDefaultDocs();

        var snapshot = CreateProvider().Current;

        snapshot.Should().NotBeNull();
        snapshot!.Get(LegalDocumentType.Privacy)!.ContentHtml.Should().Be("<p>privacy text</p>");
        snapshot.Get(LegalDocumentType.TermsClient)!.Version.Should().Be("2026-09-08-draft");
        snapshot.Get(LegalDocumentType.TermsOwner).Should().NotBeNull();
        snapshot.Get(LegalDocumentType.PdnConsent).Should().NotBeNull();
        snapshot.Get(LegalDocumentType.ChannelRiskNotice).Should().NotBeNull();
        snapshot.GetText(LegalTextKey.BookingNotice)!.ContentHtml.Should().Be("<p>booking notice</p>");
        snapshot.GetText(LegalTextKey.GuardianConfirmation).Should().NotBeNull();
    }

    [Fact]
    public void Current_ManifestMissing_ReturnsNull()
    {
        CreateProvider().Current.Should().BeNull();
    }

    [Fact]
    public void Current_DraftVersionWithoutDraftSuffix_DoesNotPublish()
    {
        WriteManifest(ValidManifest(privacyVersion: "2026-09-08")); // isDraft: true but no "-draft" suffix
        WriteDoc("privacy.html", "<p>privacy text</p>");
        WriteDoc("terms.html", "<p>terms text</p>");
        WriteRemainingDefaultDocs();

        CreateProvider().Current.Should().BeNull();
    }

    [Theory]
    [InlineData("<p>hello <script>alert(1)</script></p>")]
    [InlineData("<img src=x onerror=\"alert(1)\">")]
    public void Current_DocumentWithScriptOrEventHandler_DoesNotPublish(string maliciousHtml)
    {
        WriteManifest(ValidManifest());
        WriteDoc("privacy.html", maliciousHtml);
        WriteDoc("terms.html", "<p>terms text</p>");
        WriteRemainingDefaultDocs();

        CreateProvider().Current.Should().BeNull();
    }

    [Fact]
    public void Current_UnrecognizedChangeKind_DefaultsToMaterial()
    {
        // Only the Privacy entry's changeKind is corrupted to an unrecognized value — checks the default
        // independently of the "everything else is Material" baseline the rest of the manifest already is.
        WriteManifest(ValidManifest().Replace(
            "\"changeKind\": \"Material\", \"gate\": \"Global\", \"title\": \"Privacy\"",
            "\"changeKind\": \"Nonsense\", \"gate\": \"Global\", \"title\": \"Privacy\""));
        WriteDoc("privacy.html", "<p>privacy</p>");
        WriteDoc("terms.html", "<p>terms</p>");
        WriteRemainingDefaultDocs();

        var snapshot = CreateProvider().Current;

        snapshot!.Get(LegalDocumentType.Privacy)!.ChangeKind.Should().Be(LegalChangeKind.Material);
    }

    [Fact]
    public void Current_UnrecognizedGate_DefaultsToGlobal()
    {
        WriteManifest(ValidManifest().Replace("\"gate\": \"OwnerScope\"", "\"gate\": \"Nonsense\""));
        WriteDoc("privacy.html", "<p>privacy</p>");
        WriteDoc("terms.html", "<p>terms</p>");
        WriteRemainingDefaultDocs();

        var snapshot = CreateProvider().Current;

        // The entry whose gate was corrupted was TermsOwner's — an unrecognized value must fail closed
        // to the stricter default (Global), never silently stop gating an owner-only document.
        snapshot!.Get(LegalDocumentType.TermsOwner)!.Gate.Should().Be(LegalGate.Global);
    }

    // CYCLE5-BREAKING: a manifest missing even one of the five required document types no longer
    // publishes anything — was "must have both Privacy and Terms", now "must have all five"
    // (ARCHITECTURE_CYCLE5.md §43.3).
    [Fact]
    public void Current_MissingDocumentType_DoesNotPublishAny()
    {
        WriteManifest("""
            {
              "documents": [
                { "type": "Privacy", "version": "v1", "effectiveFrom": "2026-09-08", "isDraft": false, "changeKind": "Material", "gate": "Global", "title": "Privacy", "file": "privacy.html" }
              ],
              "uiTexts": []
            }
            """);
        WriteDoc("privacy.html", "<p>privacy</p>");

        CreateProvider().Current.Should().BeNull();
    }

    [Fact]
    public void Current_MissingUiTextKey_DoesNotPublishAny()
    {
        // Removes a middle array element (with its trailing comma) rather than the last one, so the
        // remaining JSON stays syntactically valid — this must fail the "all six keys present" check
        // specifically, not merely fail to parse.
        var withoutOneKey = ValidManifest().Replace(
            """{ "key": "PhotoConsent", "version": "v1-draft", "isDraft": true, "file": "photo-consent.html" },""",
            "");
        WriteManifest(withoutOneKey);
        WriteDoc("privacy.html", "<p>privacy</p>");
        WriteDoc("terms.html", "<p>terms</p>");
        WriteRemainingDefaultDocs();

        CreateProvider().Current.Should().BeNull();
    }

    [Fact]
    public void Current_PdnConsentWithoutPurposes_DoesNotPublishAny()
    {
        // Removes the "purposes" field (and its leading comma) together, so what's left is valid JSON
        // with the field simply absent — this must fail PdnConsent's "purposes required" check
        // specifically, not merely fail to parse.
        var withoutPurposes = ValidManifest().Replace(
            """, "purposes": [ { "key": "ProviderDelivery", "title": "Доставка" }, { "key": "WorkPhotos", "title": "Фото" }, { "key": "HealthData", "title": "Здоровье" } ] }""",
            "}");
        WriteManifest(withoutPurposes);
        WriteDoc("privacy.html", "<p>privacy</p>");
        WriteDoc("terms.html", "<p>terms</p>");
        WriteRemainingDefaultDocs();

        CreateProvider().Current.Should().BeNull();
    }

    [Fact]
    public void Current_PdnConsentPurposes_PreservedInManifestOrder()
    {
        WriteManifest(ValidManifest());
        WriteDoc("privacy.html", "<p>privacy</p>");
        WriteDoc("terms.html", "<p>terms</p>");
        WriteRemainingDefaultDocs();

        var snapshot = CreateProvider().Current;

        snapshot!.Get(LegalDocumentType.PdnConsent)!.Purposes.Select(p => p.Key).Should().Equal(
            ConsentPurpose.ProviderDelivery, ConsentPurpose.WorkPhotos, ConsentPurpose.HealthData);
    }

    // ARCHITECTURE_CYCLE5.md §43.3: an unresolved {{PLACEHOLDER}} is fine on a draft, and a fail-fast on
    // anything published as final — cheap insurance against a document with a hole reaching a live
    // domain (§13-бис).
    [Fact]
    public void Current_UnresolvedPlaceholder_IsDraftFalse_DoesNotPublish()
    {
        // privacyVersion has no "-draft" suffix (required once isDraft flips to false below); termsVersion
        // is left at the default so only the Privacy entry's draft status changes.
        var manifest = ValidManifest(privacyVersion: "2026-09-08").Replace(
            "\"isDraft\": true, \"changeKind\": \"Material\", \"gate\": \"Global\", \"title\": \"Privacy\"",
            "\"isDraft\": false, \"changeKind\": \"Material\", \"gate\": \"Global\", \"title\": \"Privacy\"");
        WriteManifest(manifest);
        WriteDoc("privacy.html", "<p>оператор: {{НАЗВАНИЕ_ОПЕРАТОРА}}</p>");
        WriteDoc("terms.html", "<p>terms</p>");
        WriteRemainingDefaultDocs();

        CreateProvider().Current.Should().BeNull();
    }

    [Fact]
    public void Current_UnresolvedPlaceholder_IsDraftTrue_StillPublishes()
    {
        WriteManifest(ValidManifest());
        WriteDoc("privacy.html", "<p>оператор: {{НАЗВАНИЕ_ОПЕРАТОРА}}</p>");
        WriteDoc("terms.html", "<p>terms</p>");
        WriteRemainingDefaultDocs();

        CreateProvider().Current.Should().NotBeNull();
    }

    [Fact]
    public void Current_ContentHash_IsSha256OfExactBytesAndStableAcrossReloadsOfIdenticalContent()
    {
        WriteManifest(ValidManifest());
        WriteDoc("privacy.html", "<p>privacy text</p>");
        WriteDoc("terms.html", "<p>terms text</p>");
        WriteRemainingDefaultDocs();

        var expectedHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("<p>privacy text</p>"))).ToLowerInvariant();

        var snapshot = CreateProvider().Current;

        snapshot!.Get(LegalDocumentType.Privacy)!.ContentHash.Should().Be(expectedHash);
    }

    [Fact]
    public void Current_OnlyDocumentsPresent_DoesNotPublishAny()
    {
        WriteManifest("""
            {
              "documents": [
                { "type": "Privacy", "version": "v1", "effectiveFrom": "2026-09-08", "isDraft": false, "changeKind": "Material", "gate": "Global", "title": "Privacy", "file": "privacy.html" }
              ]
            }
            """);
        WriteDoc("privacy.html", "<p>privacy</p>");

        CreateProvider().Current.Should().BeNull();
    }

    [Fact]
    public void Current_BadReloadAfterGoodLoad_KeepsPreviousSnapshot()
    {
        WriteManifest(ValidManifest());
        WriteDoc("privacy.html", "<p>v1</p>");
        WriteDoc("terms.html", "<p>terms</p>");
        WriteRemainingDefaultDocs();

        var provider = CreateProvider(reloadSeconds: 0); // reload on every access, for the test
        var first = provider.Current;
        first.Should().NotBeNull();

        // Break the manifest, but the mtime must actually change for a reload to even attempt re-parsing.
        Thread.Sleep(10);
        File.WriteAllText(Path.Combine(_root, "legal.json"), "{ not json");

        var second = provider.Current;
        second.Should().BeSameAs(first, "a broken reload must never discard the last valid snapshot");
    }

    // Code review finding: reload used to trigger only on legal.json's own mtime. An operator who fixes
    // a typo in privacy.html WITHOUT touching legal.json (no version bump needed for that kind of edit,
    // ARCHITECTURE.md §4.3) used to see the stale text served indefinitely — this pins that the content
    // file's own mtime is now enough to trigger a reload.
    [Fact]
    public void Current_OnlyContentFileTouched_ManifestUntouched_StillReloads()
    {
        WriteManifest(ValidManifest());
        WriteDoc("privacy.html", "<p>v1</p>");
        WriteDoc("terms.html", "<p>terms</p>");
        WriteRemainingDefaultDocs();

        var provider = CreateProvider(reloadSeconds: 9999); // LoadAtStartup below bypasses the timer anyway
        provider.LoadAtStartup();
        var first = provider.Current;
        first!.Get(LegalDocumentType.Privacy)!.ContentHtml.Should().Be("<p>v1</p>");

        // Only the content file changes; legal.json itself is left untouched.
        Thread.Sleep(10);
        WriteDoc("privacy.html", "<p>v2 (typo fixed)</p>");

        // LoadAtStartup forces an immediate reload attempt outside ReloadSeconds' cache window — same
        // mechanism Program.cs's fail-fast startup check uses (§4.4/§13), used here only to bypass the
        // timer, not to change what's under test (the mtime comparison itself).
        provider.LoadAtStartup();
        var second = provider.Current;
        second.Should().NotBeSameAs(first, "a content-only edit must produce a fresh snapshot");
        second!.Get(LegalDocumentType.Privacy)!.ContentHtml.Should().Be("<p>v2 (typo fixed)</p>");
    }

    [Fact]
    public void LoadAtStartup_ValidManifest_PopulatesCurrentImmediately()
    {
        WriteManifest(ValidManifest());
        WriteDoc("privacy.html", "<p>privacy</p>");
        WriteDoc("terms.html", "<p>terms</p>");
        WriteRemainingDefaultDocs();

        var provider = CreateProvider(reloadSeconds: 9999); // would otherwise never check again
        provider.LoadAtStartup();

        provider.Current.Should().NotBeNull();
    }

    [Fact]
    public void LoadAtStartup_MissingManifest_LeavesCurrentNull()
    {
        var provider = CreateProvider();
        provider.LoadAtStartup();

        provider.Current.Should().BeNull();
    }

    // ARCHITECTURE_CYCLE11.md §104.2: LoadStrict is the one-attempt-that-throws sibling of TryReload —
    // a tool (or a startup fail-fast check) needs the real exception, not a swallowed Error log line.
    [Fact]
    public void LoadStrict_ValidManifest_ReturnsSnapshot()
    {
        WriteManifest(ValidManifest());
        WriteDoc("privacy.html", "<p>privacy</p>");
        WriteDoc("terms.html", "<p>terms</p>");
        WriteRemainingDefaultDocs();

        var snapshot = CreateProvider().LoadStrict();

        snapshot.Documents.Should().HaveCount(5);
    }

    [Fact]
    public void LoadStrict_MissingManifest_Throws()
    {
        var act = () => CreateProvider().LoadStrict();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void LoadStrict_InvalidManifest_ThrowsWithoutTouchingPreviousSnapshot()
    {
        WriteManifest(ValidManifest());
        WriteDoc("privacy.html", "<p>privacy</p>");
        WriteDoc("terms.html", "<p>terms</p>");
        WriteRemainingDefaultDocs();
        var provider = CreateProvider();
        provider.LoadAtStartup();
        provider.Current.Should().NotBeNull();

        WriteManifest("{ not valid json");

        var act = () => provider.LoadStrict();

        act.Should().Throw<Exception>();
        provider.Current.Should().NotBeNull(); // TryReload's read path is unaffected by a failed LoadStrict call
    }

    // Code review finding: ResolveContentPath (the freshness-check path resolver) used to reject only
    // ".." and nothing else, so a rooted/absolute "file" value (e.g. "/etc/passwd" on Unix, or
    // "C:\secrets.txt" on Windows) would make Path.Combine(_root, fileName) return a path OUTSIDE
    // _root — Combine discards the first argument entirely when the second is rooted. LoadDocument's
    // own containment check would still reject such a file as content, but the freshness poll would
    // have already started tracking an unrelated file's mtime. IsBareFilename is now the one rule both
    // call sites share, so this can't reappear as a second, slightly different check.
    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData(@"C:\secrets.txt")]
    [InlineData("..")]
    [InlineData("a/../b.html")]
    [InlineData(@"a\b.html")]
    [InlineData("")]
    [InlineData(null)]
    public void IsBareFilename_PathSegmentsOrRootedOrEmpty_ReturnsFalse(string? fileName)
    {
        LegalDocumentProvider.IsBareFilename(fileName).Should().BeFalse();
    }

    [Theory]
    [InlineData("privacy.html")]
    [InlineData("terms-v2.html")]
    public void IsBareFilename_PlainFilename_ReturnsTrue(string fileName)
    {
        LegalDocumentProvider.IsBareFilename(fileName).Should().BeTrue();
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ServiceBooking.UnitTests";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Testing";
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = null!;
    }
}
