namespace ServiceBooking.API.DTOs.Companies;

// ARCHITECTURE_CYCLE19.md §388.1/§413 — the geocoder ("проверка адреса по карте", cycle 13) is removed
// целиком: CompanyAddressVerificationDto, GeoPointDto, LookupAddressDto, AddressCandidateDto,
// AddressWarningDto, AddressLookupResultDto, AddressVerificationResultDto and CompanyAddressMapping are
// gone. What remains is address SAVING and the public-address-notice legal gate.

// ── §234/§413.2: PUT /api/companies/{id}/address ────────────────────────────────────────────────────

// `verify` is accepted and ignored (ARCHITECTURE_CYCLE19.md §388.2) — kept on the DTO so an old cached
// tab that still sends `verify: true` doesn't 400 on an unknown-but-harmless field.
public record SaveCompanyAddressDto(string Address, bool Verify = false);

public record CompanyAddressUpdateResultDto(CompanyDto Company);

// ── §242/§413.1: POST /api/companies/address/notice ─────────────────────────────────────────────────

public record SubmitAddressNoticeDto(string TextVersion, bool Confirmed);

public record AddressNoticeResultDto(string Version, DateTime AcknowledgedAt);
