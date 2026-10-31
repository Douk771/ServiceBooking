using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// CY48-B8 (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.13.1): contracts/legal/operator-public.json conforms to
/// components.schemas.OperatorPublicRequisites of the cycle 48 contract. Reads files only: no server, no database.
/// </summary>
public class Cycle48OperatorRequisitesSchemaTests
{
    private static JsonElement LoadOperatorPublic()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "contracts", "legal", "operator-public.json");
            if (File.Exists(candidate)) return JsonDocument.Parse(File.ReadAllText(candidate)).RootElement.Clone();
        }

        throw new FileNotFoundException("contracts/legal/operator-public.json not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void OperatorPublicJson_ConformsToTheSchema() =>
        OpenApiContract.Load("cycle48").AssertSchema("OperatorPublicRequisites", LoadOperatorPublic());

    [Fact]
    public void Values_HaveNoPlaceholdersNorEmptyStrings()
    {
        var values = LoadOperatorPublic().GetProperty("values").EnumerateObject().ToList();

        values.Should().HaveCount(13);
        values.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.Value.GetString())
                                         && !p.Value.GetString()!.Contains("ЗАПОЛНИТЬ") && !p.Value.GetString()!.Contains("{{"));
    }

    [Fact]
    public void Values_FollowTheDocumentedShapes()
    {
        var values = LoadOperatorPublic().GetProperty("values");
        string V(string key) => values.GetProperty(key).GetString()!;

        V("ИНН_ОПЕРАТОРА").Should().MatchRegex("^[0-9]{12}$");
        V("ТЕЛЕФОН_ОПЕРАТОРА").Should().MatchRegex(@"^\+7[ 0-9()-]{10,16}$");
        V("ПОЧТА_ПОДДЕРЖКИ").Should().MatchRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        V("ГОРОД_ОПЕРАТОРА").Should().StartWith("г. ");
        V("ЮРИДИЧЕСКИЙ_АДРЕС").Should().Contain(V("ГОРОД_ОПЕРАТОРА")["г. ".Length..], "the city is part of the legal address");

        var tel = "+" + Regex.Replace(V("ТЕЛЕФОН_ОПЕРАТОРА"), @"\D", "");
        tel.Should().MatchRegex(@"^\+7\d{10}$");
        LoadOperatorPublic().GetProperty("derived").GetProperty("ТЕЛЕФОН_TEL").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
