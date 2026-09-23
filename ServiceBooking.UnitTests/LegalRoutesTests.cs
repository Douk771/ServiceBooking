using System.Text.Json;
using FluentAssertions;
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
}
