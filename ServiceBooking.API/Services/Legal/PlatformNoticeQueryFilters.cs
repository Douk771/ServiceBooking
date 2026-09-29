using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Legal;

/// <summary>
/// Pure parsing logic for GET /api/admin/notices?kind= (ARCHITECTURE_CYCLE20.md §404, US-20-03) — split
/// out from AdminNoticesController the same way BookingFilters.TryParseClientStatus is split out of
/// BookingsController, so the exact same rule can be unit-tested without a host/DB and reused by the
/// controller.
///
/// Contract check finding (cycle 20 QA pass): `kind` used to be bound straight to `PlatformNoticeKind?`
/// on the action itself. [ApiController]'s automatic model validation rejects any string that isn't one
/// of the enum's member names BEFORE the action runs, with ModelValidationErrorFormatter's field-agnostic
/// fallback sentence — not a message that names `kind` at all. Parsing it here ourselves, exactly the
/// way AdminController.GetSubjectRequests already treats its own `dueState` filter, gives the caller an
/// actionable "which value, which field" message instead (same "tell the caller their query string was
/// wrong" reasoning as that code review comment).
/// </summary>
public static class PlatformNoticeQueryFilters
{
    /// <summary>
    /// False = unknown value → the caller should get a 400 naming the offending value. Null/empty is
    /// "no filter" — not this method's business to reject (same convention as every other optional list
    /// filter in the product, e.g. BookingFilters.TryParseClientStatus's own empty-string branch).
    ///
    /// Unlike BookingFilters.TryParseClientStatus, numeric input ("0") is rejected outright rather than
    /// accepted as an ordinal alias: this filter is brand new in cycle 20, so — unlike BookingStatus's
    /// query filter — there is no existing caller anywhere that could depend on the numeric form, and
    /// accepting it would silently reopen the exact hole LegalController.GetDocument's own `{type}`
    /// parsing was written to close (Enum.TryParse maps ANY integer literal to the enum's underlying
    /// value, whether or not the caller meant to name a member).
    /// </summary>
    public static bool TryParseKind(string? kind, out PlatformNoticeKind? parsedKind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            parsedKind = null;
            return true;
        }

        if (int.TryParse(kind, out _) || !Enum.TryParse<PlatformNoticeKind>(kind, ignoreCase: true, out var value))
        {
            parsedKind = null;
            return false;
        }

        parsedKind = value;
        return true;
    }
}
