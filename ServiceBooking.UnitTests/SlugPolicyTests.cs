using FluentAssertions;
using ServiceBooking.API.Services.Shops;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE23.md §390 — SlugPolicy against contracts/cycle23/goods-routes.json.</summary>
public class SlugPolicyTests
{
    [Theory]
    [InlineData("shaurma-na-lenina", SlugCheck.Ok)]
    [InlineData("abc", SlugCheck.Ok)]
    [InlineData("Shaurma", SlugCheck.Ok)]          // normalized to lowercase before checking
    [InlineData("  shop-7  ", SlugCheck.Ok)]
    [InlineData("ab", SlugCheck.Invalid)]
    [InlineData("", SlugCheck.Invalid)]
    [InlineData("a--b", SlugCheck.Invalid)]
    [InlineData("-abc", SlugCheck.Invalid)]
    [InlineData("abc-", SlugCheck.Invalid)]
    [InlineData("шаурма", SlugCheck.Invalid)]
    [InlineData("my_shop", SlugCheck.Invalid)]
    [InlineData("my shop", SlugCheck.Invalid)]
    [InlineData("cabinet", SlugCheck.Reserved)]
    [InlineData("CABINET", SlugCheck.Reserved)]
    [InlineData("terms-owner", SlugCheck.Reserved)]
    [InlineData("api", SlugCheck.Reserved)]
    public void Validate(string slug, SlugCheck expected) => SlugPolicy.Validate(slug).Should().Be(expected);

    [Fact]
    public void Length_Boundaries()
    {
        SlugPolicy.MinLength.Should().Be(3);
        SlugPolicy.MaxLength.Should().Be(50);
        SlugPolicy.Validate(new string('a', 50)).Should().Be(SlugCheck.Ok);
        SlugPolicy.Validate(new string('a', 51)).Should().Be(SlugCheck.Invalid);
    }

    [Fact]
    public void EverySpaRoute_FirstSegment_IsReserved()
    {
        // R-4: a new goods route must come with a reserved word, or it could collide with an existing shop's address.
        using var doc = ContractFiles.Load("cycle23", "goods-routes.json");
        foreach (var route in doc.RootElement.GetProperty("spaRoutes").EnumerateArray().Select(r => r.GetString()!))
        {
            var first = SlugPolicy.FirstSegment(route);
            if (first is null) continue; // "/"
            SlugPolicy.ReservedSlugs.Should().Contain(first, $"route {route} must be reserved");
        }
    }

    [Fact]
    public void Reserved_ContainsOrderPageSegmentAndServicePaths() =>
        SlugPolicy.ReservedSlugs.Should().Contain(["o", "api", "uploads", "login", "orders"]);
}
