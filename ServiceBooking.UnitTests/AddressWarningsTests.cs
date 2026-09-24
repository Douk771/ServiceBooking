using FluentAssertions;
using ServiceBooking.API.Services.Geo;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE13.md §209.1/§214, API_CONTRACT_CYCLE13.md §236 — pure code-to-text
/// mapping, no DB, no network.</summary>
public class AddressWarningsTests
{
    [Fact]
    public void ForPrecision_House_ReturnsNull() =>
        AddressWarnings.ForPrecision(AddressPrecision.House).Should().BeNull();

    [Fact]
    public void ForPrecision_Street_ReturnsPrecisionStreetCode() =>
        AddressWarnings.ForPrecision(AddressPrecision.Street)!.Code.Should().Be("PrecisionStreet");

    [Fact]
    public void ForPrecision_Locality_ReturnsPrecisionLocalityCode() =>
        AddressWarnings.ForPrecision(AddressPrecision.Locality)!.Code.Should().Be("PrecisionLocality");

    [Fact]
    public void ForPrecision_Other_ReturnsPrecisionOtherCode() =>
        AddressWarnings.ForPrecision(AddressPrecision.Other)!.Code.Should().Be("PrecisionOther");

    [Theory]
    [InlineData(AddressPrecision.Street)]
    [InlineData(AddressPrecision.Locality)]
    [InlineData(AddressPrecision.Other)]
    public void ForPrecision_NonHouse_AlwaysHasNonEmptyMessage(AddressPrecision precision) =>
        AddressWarnings.ForPrecision(precision)!.Message.Should().NotBeNullOrWhiteSpace();

    [Fact]
    public void ForCityMismatch_SameCity_ReturnsNull() =>
        AddressWarnings.ForCityMismatch("Барнаул", "Барнаул").Should().BeNull();

    [Fact]
    public void ForCityMismatch_SameCity_CaseInsensitive_ReturnsNull() =>
        AddressWarnings.ForCityMismatch("барнаул", "БАРНАУЛ").Should().BeNull();

    [Fact]
    public void ForCityMismatch_SameCity_DifferingWhitespace_ReturnsNull() =>
        AddressWarnings.ForCityMismatch(" Барнаул ", "Барнаул").Should().BeNull();

    [Fact]
    public void ForCityMismatch_DifferentCity_ReturnsCityMismatchCode() =>
        AddressWarnings.ForCityMismatch("Новосибирск", "Барнаул")!.Code.Should().Be("CityMismatch");

    [Fact]
    public void ForCityMismatch_CandidateCityUnknown_ReturnsNull() =>
        AddressWarnings.ForCityMismatch(null, "Барнаул").Should().BeNull();

    [Fact]
    public void ForCityMismatch_CompanyCityUnknown_ReturnsNull() =>
        AddressWarnings.ForCityMismatch("Барнаул", null).Should().BeNull();

    [Fact]
    public void ForCityMismatch_BothUnknown_ReturnsNull() =>
        AddressWarnings.ForCityMismatch(null, null).Should().BeNull();

    [Fact]
    public void NotFound_HasFixedCode() => AddressWarnings.NotFound.Code.Should().Be("NotFound");

    [Fact]
    public void Unavailable_HasFixedCode() => AddressWarnings.Unavailable.Code.Should().Be("Unavailable");
}
