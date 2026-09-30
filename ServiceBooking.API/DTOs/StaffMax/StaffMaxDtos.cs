using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.StaffMax;

/// <summary>API_CONTRACT_CYCLE25.md §523.1. Shape: contracts/cycle25/openapi.yaml.</summary>
public enum StaffMaxStatus
{
    NotLinked,
    Pending,
    Linked,
    StoppedInMax
}

public sealed record StaffMaxShopDto(Guid ShopId, string Name, bool StaffMaxEnabled);

public sealed record StaffMaxPendingSessionDto(Guid SessionId, DateTime ExpiresAtUtc);

public sealed record StaffMaxStatusDto(
    bool Available, bool CanLink, string? UnavailableText, bool Eligible, StaffMaxStatus Status, string StatusText,
    DateTime? LinkedAtUtc, DateTime? StoppedAtUtc, StaffMaxPendingSessionDto? PendingSession, IReadOnlyList<StaffMaxShopDto> Shops,
    int PollIntervalSeconds);

public sealed record StaffMaxLinkSessionDto(
    Guid SessionId, string DeepLink, string WebLink, string? QrPngBase64, DateTime ExpiresAtUtc, int TtlSeconds, int PollIntervalSeconds);
