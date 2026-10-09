using System.Text.RegularExpressions;
using FluentAssertions;
using ServiceBooking.API.Services.Baths;
using ServiceBooking.API.Services.Shops;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE42.md §42.10.1 — address policy from the embedded contracts/cycle42/bani-routes.json.</summary>
public class BathsSlugPolicyTests
{
    [Theory]
    [InlineData("parilka", SlugCheck.Ok)]
    [InlineData("  Parilka-2 ", SlugCheck.Ok)]
    [InlineData("ab", SlugCheck.Invalid)]
    [InlineData("-bad", SlugCheck.Invalid)]
    [InlineData("two--dashes", SlugCheck.Invalid)]
    [InlineData("primer-bani", SlugCheck.Invalid)]
    [InlineData("sauna", SlugCheck.Reserved)]
    [InlineData("BATHS", SlugCheck.Reserved)]
    [InlineData("cabinet", SlugCheck.Reserved)]
    public void Validate_Company(string slug, SlugCheck expected) => BathsSlugPolicy.Validate(slug).Should().Be(expected);

    [Fact]
    public void Validate_TooLong_IsInvalid() => BathsSlugPolicy.Validate(new string('a', 51)).Should().Be(SlugCheck.Invalid);

    [Theory]
    [InlineData("chan", SlugCheck.Ok)]
    [InlineData("a", SlugCheck.Invalid)]
    [InlineData("Banya-1", SlugCheck.Ok)]
    [InlineData("uslugi", SlugCheck.Reserved)]
    [InlineData("ICAL", SlugCheck.Reserved)]
    [InlineData("bad_slug", SlugCheck.Invalid)]
    public void Validate_Resource(string slug, SlugCheck expected) => BathsSlugPolicy.ValidateResource(slug).Should().Be(expected);

    [Fact]
    public void ReservedSlugs_IncludeEverythingOfDom()
    {
        var dom = ContractFiles.Load("cycle39", "dom-routes.json").RootElement.GetProperty("reservedSlugs").EnumerateArray().Select(e => e.GetString()!);
        BathsSlugPolicy.ReservedSlugs.Should().Contain(dom);
        BathsSlugPolicy.ReservedSlugs.Should().Contain(new[] { "bath", "baths", "sauna", "saunas", "resources", "resource" });
    }

    [Fact]
    public void FirstSegmentOfEverySpaRoute_IsReserved()
    {
        foreach (var route in BathsSlugPolicy.SpaRoutes.Where(r => r != "/"))
        {
            var first = route.Split('/', StringSplitOptions.RemoveEmptyEntries)[0];
            if (first.StartsWith(':')) continue;
            BathsSlugPolicy.ReservedSlugs.Should().Contain(first, $"route {route}");
        }
    }

    [Fact]
    public void ReservedResourceSlugs_AllMatchTheResourcePattern()
    {
        foreach (var word in BathsSlugPolicy.ReservedResourceSlugs)
            BathsSlugPolicy.ValidateResource(word).Should().Be(SlugCheck.Reserved, word);
    }
}
