namespace ServiceBooking.Core.Enums;

/// <summary>
/// How precisely the geocoder located an address at the moment it was checked
/// (ARCHITECTURE_CYCLE13.md §202, §206). Stored as <c>int</c> (project convention — enums in the
/// database are plain integers, no <c>HasConversion&lt;string&gt;</c>), append-only.
///
/// This is a RECOGNIZED FACT about our own verification attempt, not a copy of anything the geocoder
/// returned verbatim — the standard Yandex Geocoder licence permits storing this kind of derived
/// signal but not the geocoder's own normalized address string (LEGAL_REVIEW.md §16.2, Q-L10).
/// </summary>
public enum AddressPrecision
{
    House = 0,
    Street = 1,
    Locality = 2,
    Other = 3
}
