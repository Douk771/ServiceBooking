namespace ServiceBooking.API.DTOs.Cities;

/// <summary>API_CONTRACT_CYCLE4.md §31.1. <c>Label</c> is assembled server-side — the frontend does not
/// concatenate name and region itself.</summary>
public record CityDto(int Id, string Name, string Region, string TimeZoneId, int UtcOffsetMinutes, string Label);

public record CityListDto(IReadOnlyList<CityDto> Items);
