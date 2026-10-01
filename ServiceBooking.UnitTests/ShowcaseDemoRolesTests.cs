using FluentAssertions;
using ServiceBooking.API.Services.Showcase;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE35.md §35.7.1 — the six demo roles of the two products: names, captions, grouping by product, stable account ids.</summary>
public class ShowcaseDemoRolesTests
{
    [Fact]
    public void All_IsTheSixRolesInTheOrderOfTheContract_WithTheirCaptions() =>
        ShowcaseDemoRoles.All.Should().Equal(
            ("owner", "Войти как владелец салона"),
            ("master", "Войти как мастер"),
            ("client", "Войти как клиент"),
            ("shop-owner", "Войти как владелец магазина"),
            ("shop-staff", "Войти как сотрудник магазина"),
            ("shop-customer", "Войти как покупатель"));

    [Fact]
    public void ForProduct_GivesTheThreeRolesOfOneProduct()
    {
        ShowcaseDemoRoles.ForProduct(DemoProduct.Services).Select(r => r.Role).Should().Equal("owner", "master", "client");
        ShowcaseDemoRoles.ForProduct(DemoProduct.Orders).Select(r => r.Role).Should().Equal("shop-owner", "shop-staff", "shop-customer");
    }

    [Theory]
    [InlineData("owner", DemoProduct.Services)]
    [InlineData("client", DemoProduct.Services)]
    [InlineData("shop-owner", DemoProduct.Orders)]
    [InlineData("shop-customer", DemoProduct.Orders)]
    public void ProductOf_KnowsTheProductOfEveryRole(string role, DemoProduct product) =>
        ShowcaseDemoRoles.ProductOf(role).Should().Be(product);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("admin")]
    [InlineData("Shop-Owner")]
    public void UnknownRoles_HaveNoProductNoIdAndAreNotKnown(string? role)
    {
        ShowcaseDemoRoles.ProductOf(role).Should().BeNull();
        ShowcaseDemoRoles.UserIdOf(role).Should().BeNull();
        ShowcaseDemoRoles.IsKnown(role).Should().BeFalse();
    }

    [Fact]
    public void EverySixAccountIsADemoUserId_AndTheIdsAreDistinct()
    {
        var ids = ShowcaseDemoRoles.All.Select(r => ShowcaseDemoRoles.UserIdOf(r.Role)!).ToList();
        ids.Should().OnlyHaveUniqueItems().And.OnlyContain(id => ShowcaseDemoRoles.IsDemoUserId(id));
        ShowcaseDemoRoles.IsDemoUserId(Guid.NewGuid().ToString()).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, true, DemoProduct.Services)]
    [InlineData("", true, DemoProduct.Services)]
    [InlineData("  ", true, DemoProduct.Services)]
    [InlineData("services", true, DemoProduct.Services)]
    [InlineData("Orders", true, DemoProduct.Orders)]
    [InlineData("ORDERS", true, DemoProduct.Orders)]
    [InlineData("goods", false, DemoProduct.Services)]
    [InlineData("x", false, DemoProduct.Services)]
    public void TryParseProduct_DefaultsToServices_AndRejectsAnythingElse(string? value, bool ok, DemoProduct expected)
    {
        ShowcaseDemoRoles.TryParseProduct(value, out var product).Should().Be(ok);
        product.Should().Be(expected);
    }
}
