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

// `Attribution` (§234, review finding — cycle 13 review, blocking #4): contracts/cycle13/openapi.yaml
// declares it `type: string`, required, without `nullable: true` — "" for Disabled/Unavailable (no map
// was reached), never null. Both DTOs below carry it as a non-nullable `string`; callers must coalesce
// the geocoder's own `string?` Attribution (null exactly when nothing was reached) to "" before building
// these records.
public record AddressLookupResultDto(
    string Outcome,
    string QueriedAddress,
    IReadOnlyList<AddressCandidateDto> Candidates,
    IReadOnlyList<AddressWarningDto> Warnings,
    string Attribution);

// ── §234: PUT /api/companies/{id}/address ───────────────────────────────────────────────────────────

public record SaveCompanyAddressDto(string Address, bool Verify = false);

public record AddressVerificationResultDto(
    string Outcome,
    string Status,
    DateTime? VerifiedAt,
    string? Precision,
    IReadOnlyList<AddressWarningDto> Warnings,
    string Attribution);

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

    // `storeResults` (§209.2/P3, review finding — cycle 13 review, blocking #1): coordinates are exposed
    // ONLY when the extended licence's own flag is on, same gate PUT /api/companies/{id}/address already
    // applies before writing AddressLatitude/AddressLongitude. Without this, lookup results leaked real
    // coordinates regardless of the switch — contracts/cycle13/openapi.yaml:624-631 requires `point: null`
    // whenever StoreResults is false, which is the normal, unlicensed state in production.
    public static AddressCandidateDto ToDto(this GeocodeCandidate candidate, IReadOnlyList<AddressWarning> warnings, bool storeResults) =>
        new(candidate.FormattedAddress, candidate.Precision.ToString(), candidate.CityName,
            storeResults && candidate.Point is { } p ? new GeoPointDto(p.Latitude, p.Longitude) : null,
            warnings.Select(w => w.ToDto()).ToList());
}
