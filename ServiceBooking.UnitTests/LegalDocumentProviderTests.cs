using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Legal;

namespace ServiceBooking.UnitTests;

/// <summary>
/// No DB, no HTTP — a temp directory standing in for App_Data/legal, exercised directly through
/// LegalDocumentProvider's public surface (ARCHITECTURE.md §4.3/§4.4).
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

    private static string ValidManifest(string privacyVersion = "2026-09-08-draft", string termsVersion = "2026-09-08-draft") => $$"""
        {
          "documents": [
            { "type": "Privacy", "version": "{{privacyVersion}}", "effectiveFrom": "2026-09-08", "isDraft": true, "changeKind": "Material", "title": "Privacy", "file": "privacy.html" },
            { "type": "Terms", "version": "{{termsVersion}}", "effectiveFrom": "2026-09-08", "isDraft": true, "changeKind": "Material", "title": "Terms", "file": "terms.html" }
          ]
        }
        """;

    [Fact]
    public void Current_ValidManifest_ReturnsBothDocuments()
    {
        WriteManifest(ValidManifest());
        WriteDoc("privacy.html", "<p>privacy text</p>");
        WriteDoc("terms.html", "<p>terms text</p>");

        var snapshot = CreateProvider().Current;

        snapshot.Should().NotBeNull();
        snapshot!.Get(LegalDocumentType.Privacy)!.ContentHtml.Should().Be("<p>privacy text</p>");
        snapshot.Get(LegalDocumentType.Terms)!.Version.Should().Be("2026-09-08-draft");
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

        CreateProvider().Current.Should().BeNull();
    }

    [Fact]
    public void Current_UnrecognizedChangeKind_DefaultsToMaterial()
    {
        WriteManifest("""
            {
              "documents": [
                { "type": "Privacy", "version": "v1", "effectiveFrom": "2026-09-08", "isDraft": false, "changeKind": "Nonsense", "title": "Privacy", "file": "privacy.html" },
                { "type": "Terms", "version": "v1", "effectiveFrom": "2026-09-08", "isDraft": false, "changeKind": "Editorial", "title": "Terms", "file": "terms.html" }
              ]
            }
            """);
        WriteDoc("privacy.html", "<p>privacy</p>");
        WriteDoc("terms.html", "<p>terms</p>");

        var snapshot = CreateProvider().Current;

        snapshot!.Get(LegalDocumentType.Privacy)!.ChangeKind.Should().Be(LegalChangeKind.Material);
        snapshot.Get(LegalDocumentType.Terms)!.ChangeKind.Should().Be(LegalChangeKind.Editorial);
    }

    [Fact]
    public void Current_OnlyOneDocumentPresent_DoesNotPublishEither()
    {
        WriteManifest("""
            {
              "documents": [
                { "type": "Privacy", "version": "v1", "effectiveFrom": "2026-09-08", "isDraft": false, "changeKind": "Material", "title": "Privacy", "file": "privacy.html" }
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

        var provider = CreateProvider(reloadSeconds: 0); // reload on every access, for the test
        var first = provider.Current;
        first.Should().NotBeNull();

        // Break the manifest, but the mtime must actually change for a reload to even attempt re-parsing.
        Thread.Sleep(10);
        File.WriteAllText(Path.Combine(_root, "legal.json"), "{ not json");

        var second = provider.Current;
        second.Should().BeSameAs(first, "a broken reload must never discard the last valid snapshot");
    }

    [Fact]
    public void LoadAtStartup_ValidManifest_PopulatesCurrentImmediately()
    {
        WriteManifest(ValidManifest());
        WriteDoc("privacy.html", "<p>privacy</p>");
        WriteDoc("terms.html", "<p>terms</p>");

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
