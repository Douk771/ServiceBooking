using ServiceBooking.API.Services.Geo;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.Companies;

// ── §232: additive CompanyDto fields ────────────────────────────────────────────────────────────────

/// <summary>ARCHITECTURE_CYCLE13.md §208/API_CONTRACT_CYCLE13.md §232. Only ever computed for a caller
/// who MANAGES the company (owner/SuperAdmin) — null everywhere else, same convention as
/// employeeCount/accountSeatsUsed.</summary>
public record CompanyAddressVerificationDto(
    bool Available,
    string Status,
    DateTime? VerifiedAt,
    string? Precision);

public record GeoPointDto(double Latitude, double Longitude);

// ── §233: POST /api/companies/address/lookup ────────────────────────────────────────────────────────

public record LookupAddressDto(string Address, int? CityId, Guid? CompanyId);

public record AddressCandidateDto(
    string FormattedAddress,
    string Precision,
    string? CityName,
    GeoPointDto? Point,
    IReadOnlyList<AddressWarningDto> Warnings);

public record AddressWarningDto(string Code, string Message);

public record AddressLookupResultDto(
    string Outcome,
    string QueriedAddress,
    IReadOnlyList<AddressCandidateDto> Candidates,
    IReadOnlyList<AddressWarningDto> Warnings,
    string? Attribution);

// ── §234: PUT /api/companies/{id}/address ───────────────────────────────────────────────────────────

public record SaveCompanyAddressDto(string Address, bool Verify = false);

public record AddressVerificationResultDto(
    string Outcome,
    string Status,
    DateTime? VerifiedAt,
    string? Precision,
    IReadOnlyList<AddressWarningDto> Warnings,
    string? Attribution);

public record CompanyAddressUpdateResultDto(CompanyDto Company, AddressVerificationResultDto Verification);

// ── §242: POST /api/companies/address/notice ────────────────────────────────────────────────────────

public record SubmitAddressNoticeDto(string TextVersion, bool Confirmed);

public record AddressNoticeResultDto(string Version, DateTime AcknowledgedAt);

/// <summary>Small mapping helpers shared by <c>CompanyAddressController</c> — kept next to the DTOs they
/// build so the enum-to-string mapping (the one place these C# enums become the contract's exact string
/// values) lives in one spot.</summary>
public static class CompanyAddressMapping
{
    public static AddressWarningDto ToDto(this AddressWarning warning) => new(warning.Code, warning.Message);

    public static AddressCandidateDto ToDto(this GeocodeCandidate candidate, IReadOnlyList<AddressWarning> warnings) =>
        new(candidate.FormattedAddress, candidate.Precision.ToString(), candidate.CityName,
            candidate.Point is { } p ? new GeoPointDto(p.Latitude, p.Longitude) : null,
            warnings.Select(w => w.ToDto()).ToList());
}
