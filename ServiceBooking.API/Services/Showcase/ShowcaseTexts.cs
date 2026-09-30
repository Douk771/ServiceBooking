namespace ServiceBooking.API.Services.Showcase;

/// <summary>The JSON refusal of <c>POST /api/bookings</c> for a closed showcase company (API_CONTRACT_CYCLE28.md §592). Not a
/// <c>text/plain</c> string like the other 409s of the route: the frontend tells it apart from "slot taken" by the content type and <c>code</c>.</summary>
public sealed record ShowcaseRefusalDto(string Code, string Message);

/// <summary>
/// Server-side fallback wording of the showcase texts (API_CONTRACT_CYCLE28.md §600, placeholders L28-1). The frontend prefers the interface
/// texts <c>ShowcaseNotice</c> / <c>ShowcaseBookingClosed</c> when <c>GET /api/legal/texts/{key}</c> answers 200; these are what the API itself says.
/// No lawyer was engaged (customer decision 30.09): neutral wording, recorded as debt L28-1.
/// </summary>
public static class ShowcaseTexts
{
    public const string BookingClosedCode = "ShowcaseBookingClosed";

    public const string BookingClosed = "Это пример страницы салона: компания вымышленная, запись к ней не принимается.";

    public static ShowcaseRefusalDto BookingClosedRefusal() => new(BookingClosedCode, BookingClosed);
}
