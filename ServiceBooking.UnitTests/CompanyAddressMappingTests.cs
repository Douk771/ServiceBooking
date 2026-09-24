using FluentAssertions;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.Services.Geo;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE13.md §209.2/P3, API_CONTRACT_CYCLE13.md §233 —
/// contracts/cycle13/openapi.yaml:624-631 requires <c>candidates[].point</c> to be null whenever
/// AddressVerification:StoreResults is off, the normal, unlicensed production state. Review finding
/// (cycle 13 review, blocking #1): <c>CompanyAddressMapping.ToDto</c> previously ignored StoreResults
/// entirely and always exposed the geocoder's own coordinates. Pure mapping, no DB, no network.</summary>
public class CompanyAddressMappingTests
{
    private static readonly GeocodeCandidate CandidateWithPoint = new(
        "Россия, край, город, проспект Ленина, 5", AddressPrecision.House, "Город", new GeoPoint(53.1, 83.2));

    [Fact]
    public void ToDto_StoreResultsFalse_PointIsAlwaysNull_EvenWhenCandidateHasCoordinates()
    {
        var dto = CandidateWithPoint.ToDto(warnings: [], storeResults: false);

        dto.Point.Should().BeNull();
    }

    [Fact]
    public void ToDto_StoreResultsTrue_PointIsExposed()
    {
        var dto = CandidateWithPoint.ToDto(warnings: [], storeResults: true);

        dto.Point.Should().NotBeNull();
        dto.Point!.Latitude.Should().Be(53.1);
        dto.Point.Longitude.Should().Be(83.2);
    }

    [Fact]
    public void ToDto_StoreResultsTrue_CandidateWithoutCoordinates_PointStillNull()
    {
        var candidateWithoutPoint = CandidateWithPoint with { Point = null };

        var dto = candidateWithoutPoint.ToDto(warnings: [], storeResults: true);

        dto.Point.Should().BeNull();
    }

    [Fact]
    public void ToDto_PassesThroughFormattedAddressPrecisionAndWarnings_Unchanged()
    {
        var warnings = new[] { new AddressWarning("PrecisionStreet", "some text") };

        var dto = CandidateWithPoint.ToDto(warnings, storeResults: false);

        dto.FormattedAddress.Should().Be(CandidateWithPoint.FormattedAddress);
        dto.Precision.Should().Be("House");
        dto.CityName.Should().Be("Город");
        dto.Warnings.Should().ContainSingle(w => w.Code == "PrecisionStreet");
    }
}
