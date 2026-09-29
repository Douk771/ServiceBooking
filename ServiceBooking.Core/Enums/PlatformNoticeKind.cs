namespace ServiceBooking.Core.Enums;

/// <summary>
/// US-20-03 (ARCHITECTURE_CYCLE20.md §404.1, Т20-02) — what kind of in-cabinet notice a
/// <see cref="Entities.PlatformNotice"/> row is. Append-only, stored as <c>int</c>; the numeric values
/// below are pinned so a later member never renumbers an existing one.
/// </summary>
public enum PlatformNoticeKind
{
    PriceChange = 0,
    TermsChange = 1,
    Suspension = 2,
    NewProcessor = 3,
    PhotoRemoved = 4,
    Other = 5,
}
