using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE11.md §102.6/§104.3: <c>contracts/cycle11/legal-routes.json</c> is the one map
/// backend, CLI and frontend all read; <see cref="LegalRoutes"/> is the backend's typed copy. This test
/// reads the JSON file straight off disk and asserts it against the typed copy so the two can never
/// silently diverge — no DB, no HTTP.
/// </summary>
public class LegalRoutesTests
{
    private static JsonDocument LoadContractFile()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "contracts", "cycle11", "legal-routes.json");
            if (File.Exists(candidate))
                return JsonDocument.Parse(File.ReadAllText(candidate));
            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException("contracts/cycle11/legal-routes.json not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Documents_MatchContractFile()
    {
        using var contract = LoadContractFile();
        var documents = contract.RootElement.GetProperty("documents");

        foreach (var property in documents.EnumerateObject())
        {
            var type = Enum.Parse<LegalDocumentType>(property.Name);
            LegalRoutes.Documents[type].Should().Be(property.Value.GetString());
        }

        LegalRoutes.Documents.Count.Should().Be(documents.EnumerateObject().Count());
    }

    [Fact]
    public void Aliases_MatchContractFile()
    {
        using var contract = LoadContractFile();
        var aliases = contract.RootElement.GetProperty("aliases");

        foreach (var property in aliases.EnumerateObject())
            LegalRoutes.Aliases[property.Name].Should().Be(property.Value.GetString());

        LegalRoutes.Aliases.Count.Should().Be(aliases.EnumerateObject().Count());
    }

    [Fact]
    public void Anchors_MatchContractFile()
    {
        using var contract = LoadContractFile();
        var anchors = contract.RootElement.GetProperty("anchors");

        foreach (var property in anchors.EnumerateObject())
        {
            var expected = property.Value.EnumerateArray().Select(e => e.GetString()).ToList();
            LegalRoutes.Anchors[property.Name].Should().BeEquivalentTo(expected);
        }
    }

    [Fact]
    public void EveryDocumentType_HasARoute()
    {
        foreach (var type in Enum.GetValues<LegalDocumentType>())
            LegalRoutes.UrlFor(type).Should().NotBeNullOrEmpty();
    }

    /// <summary>ARCHITECTURE_CYCLE11.md §111 п.4 names three defenses for a link that doesn't resolve:
    /// `legal links`/`legal check` on the backend side, `legalRoutes.test.ts` on the frontend side, and —
    /// per the review this test file responds to — this project's own unit-test layer, which used to stop
    /// short at "does the typed copy match the JSON file" and never actually read the committed
    /// <c>App_Data/legal/</c> artifact's `href="/…"` attributes or its required anchors. This closes that
    /// gap by running the exact same checks `legal check`/the readiness endpoint run
    /// (<see cref="LegalReadinessReportBuilder.CheckLinks"/>/<c>CheckAnchors</c>), against the real,
    /// checked-in files — no server, no HTTP, no database.</summary>
    [Fact]
    public void CommittedArtifact_HasNoBrokenLinksOrMissingAnchors()
    {
        var root = FindRepoLegalRoot();
        var provider = new LegalDocumentProvider(
            Options.Create(new LegalOptions { Root = root, ReloadSeconds = 0 }),
            new FakeWebHostEnvironment(),
            NullLogger<LegalDocumentProvider>.Instance);
        var snapshot = provider.LoadStrict();

        var links = LegalReadinessReportBuilder.CheckLinks(snapshot);
        var anchors = LegalReadinessReportBuilder.CheckAnchors(snapshot);

        links.Broken.Should().BeEmpty("every internal href in the committed artifact must resolve to a known route/alias");
        anchors.Missing.Should().BeEmpty("every anchor legal-routes.json requires must exist in the committed HTML");
    }

    /// <summary>ARCHITECTURE_CYCLE11.md §112 A4/§118: <c>legal-routes.json</c>'s own `allowedTargets` is
    /// meant to be the full set of link destinations a legal text may point at. Nothing in the backend
    /// actually reads that field today — <see cref="LegalReadinessReportBuilder.CheckLinks"/> derives its
    /// valid-target set itself from <see cref="LegalRoutes.Documents"/>/<see cref="LegalRoutes.Aliases"/>.
    /// The two sets happen to agree, but nothing catches it if they stop agreeing — this test is that
    /// catch.</summary>
    [Fact]
    public void AllowedTargets_MatchDocumentsAndAliasesUnion()
    {
        using var contract = LoadContractFile();
        var allowedTargets = contract.RootElement.GetProperty("allowedTargets")
            .EnumerateArray().Select(e => e.GetString()).ToHashSet(StringComparer.Ordinal);

        var derivedTargets = LegalRoutes.Documents.Values
            .Concat(LegalRoutes.Aliases.Keys)
            .Select(r => r.Split('#')[0])
            .ToHashSet(StringComparer.Ordinal);

        derivedTargets.Should().BeEquivalentTo(allowedTargets,
            "allowedTargets in legal-routes.json is documented as the full set of valid internal link " +
            "destinations, so it must not silently drift from what the backend actually accepts");
    }

    private static string FindRepoLegalRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "ServiceBooking.API", "App_Data", "legal");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException("ServiceBooking.API/App_Data/legal not found above " + AppContext.BaseDirectory);
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
