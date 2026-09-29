using FluentAssertions;
using ServiceBooking.API.Services.Companies;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE23.md §389 — the kind guard decision and the ?kind= query parsing.</summary>
public class CompanyKindTests
{
    [Theory]
    [InlineData(null, CompanyKind.Services, CompanyKindCheck.NotFound)]
    [InlineData(null, CompanyKind.Orders, CompanyKindCheck.NotFound)]
    [InlineData(CompanyKind.Services, CompanyKind.Services, CompanyKindCheck.Ok)]
    [InlineData(CompanyKind.Orders, CompanyKind.Orders, CompanyKindCheck.Ok)]
    [InlineData(CompanyKind.Orders, CompanyKind.Services, CompanyKindCheck.WrongKind)]
    [InlineData(CompanyKind.Services, CompanyKind.Orders, CompanyKindCheck.WrongKind)]
    public void Evaluate_ReturnsExpected(CompanyKind? actual, CompanyKind expected, CompanyKindCheck result) =>
        CompanyKindGuard.Evaluate(actual, expected).Should().Be(result);

    [Fact]
    public void RejectShop_Shop_Is409WithFixedText()
    {
        var result = CompanyKindGuard.RejectShop(CompanyKind.Orders);
        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(409);
        result.Value.Should().Be("Это магазин: записи, услуги и расписание для него недоступны.");
    }

    [Fact]
    public void RejectShop_Salon_IsNull() => CompanyKindGuard.RejectShop(CompanyKind.Services).Should().BeNull();

    [Theory]
    [InlineData(null, CompanyKind.Services)]
    [InlineData("", CompanyKind.Services)]
    [InlineData("  ", CompanyKind.Services)]
    [InlineData("Services", CompanyKind.Services)]
    [InlineData("Orders", CompanyKind.Orders)]
    [InlineData("orders", CompanyKind.Orders)]
    public void TryParse_ValidOrEmpty(string? raw, CompanyKind expected)
    {
        CompanyKindQuery.TryParse(raw, out var kind).Should().BeTrue();
        kind.Should().Be(expected);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("5")]
    [InlineData("Shop")]
    [InlineData("Orders,Services")]
    public void TryParse_Unknown_IsRejected(string raw) => CompanyKindQuery.TryParse(raw, out _).Should().BeFalse();
}
