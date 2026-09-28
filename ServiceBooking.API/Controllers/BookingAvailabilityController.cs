using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Bookings;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;
/// <summary>
/// Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): the read-only slot/occupancy endpoints of the former
/// <c>BookingsController</c> — <c>GET api/Bookings/occupied|slots|availability</c>, the same resolved prefix
/// (<c>[controller]</c> spelled out), the same per-action authorization and rate-limit policies.
/// </summary>
[ApiController]
[Route("api/Bookings")]
public class BookingAvailabilityController(
    AppDbContext db, SlotService slotService, AvailabilityService availabilityService,
    BookingCreationService bookingCreation) : ControllerBase
{
    [HttpGet("occupied")]
    [Authorize]
    public async Task<ActionResult<List<OccupiedRangeDto>>> GetOccupied(
        [FromQuery] string masterId,
        [FromQuery] DateOnly date,
        CancellationToken ct)
    {
        // masterId isn't secret (the public GET /api/companies/{id}/masters lists every master's id),
        // so this endpoint used to let anyone anonymous pull any master's occupied hours across every
        // company they work in (audit E3/Q9). Now it requires the caller to actually have a reason to
        // know: SuperAdmin, the master themselves, or staff of at least one company the master also
        // belongs to.
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var canView = User.IsInRole("SuperAdmin") || userId == masterId ||
            await db.CompanyMembers.Where(CompanyMembership.IsStaffRole).AnyAsync(cm => cm.UserId == userId &&
                db.CompanyMembers.Any(m => m.UserId == masterId && m.CompanyId == cm.CompanyId), ct);
        if (!canView) return Forbid();

        // Occupancy is deliberately NOT scoped by company: a master who works for two businesses is
        // still one person, so a booking made in company A must block the same time in company B.
        // Working hours ARE scoped by company (a master can keep different schedules) — the asymmetry
        // is intentional (ARCHITECTURE.md §2.3, decision Q9). No companyId parameter is introduced here.
        var bookings = await db.Bookings
            .Where(b => b.MasterId == masterId && b.Date == date && b.Status != BookingStatus.Cancelled)
            .Select(b => new OccupiedRangeDto(b.StartTime, b.EndTime))
            .ToListAsync(ct);
        return Ok(bookings);
    }

    [HttpGet("slots")]
    public async Task<ActionResult<List<TimeSlotResult>>> GetSlots(
        [FromQuery] Guid companyId,
        [FromQuery] string masterId,
        [FromQuery] Guid? serviceId,
        [FromQuery] DateOnly date,
        [FromQuery] bool manual = false,
        [FromQuery] bool extendedHours = false,
        [FromQuery] List<Guid>? serviceIds = null,
        [FromQuery] Guid? excludeBookingId = null,
        CancellationToken ct = default)
    {
        // `manual` is client-supplied, so only honor it once we've independently verified the caller
        // actually works in THIS company — same trust bar BookingsController.Create uses for
        // isStaffManualBooking. Anyone else gets the regular schedule-gated grid, same as a guest
        // (closes the A1 bypass: previously any authenticated user could pass manual=true).
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isStaff = userId is not null &&
            (User.IsInRole("SuperAdmin") || await CompanyMembership.IsStaffAsync(db, companyId, userId));

        int totalDuration;
        if (excludeBookingId is not null)
        {
            // R2/R3 (`SPEC_CYCLE6_BOOKING_FIXES.md` §0.1 Q7, review of cycle 6): reschedule's own grid must (a) not block the
            // booking's own current interval against itself, and (b) keep working when the service was
            // later deactivated, dropped from the master's capability list, or the master left the
            // company — none of that should make an existing booking un-reschedulable. Duration is
            // therefore taken from the booking's own stored BookingServices/Service, never re-resolved
            // and re-validated against the service/master catalog the way a NEW booking's serviceId is.
            if (userId is null) return NotFound();
            var booking = await db.Bookings.AsNoTracking().Include(b => b.BookingServices).Include(b => b.Service)
                .FirstOrDefaultAsync(b => b.Id == excludeBookingId, ct);
            // §257.5/§290 (BREAKING № 1): a bare 404 with an EMPTY body — identical to the
            // "not yours" answer below. A body here ("Booking not found") would make the two cases
            // distinguishable again and turn the endpoint back into an existence oracle.
            if (booking is null) return NotFound();
            // ARCHITECTURE_CYCLE15.md §257.5/§290 (BREAKING № 1): a caller who is neither staff of this
            // booking nor its own client gets a bare 404, same as a booking that doesn't exist —
            // otherwise this endpoint would confirm "this booking id belongs to someone" to anyone who
            // holds it. Order matters, same reasoning as before: authority first, the pair-match second.
            var isOwnBooking = booking.ClientId == userId;
            if (!isOwnBooking && !await CanManageBookingAsync(booking, userId)) return NotFound();
            if (booking.CompanyId != companyId || booking.MasterId != masterId)
                return BadRequest("excludeBookingId does not match companyId/masterId");

            // NB: the "master works in this company" check below deliberately does NOT run on this
            // path. The pair (companyId, masterId) is pinned to the booking's own CompanyId/MasterId
            // by the equality check above, which is strictly stronger than a membership lookup — so
            // nothing extra becomes addressable. Running it here would instead re-break the case all
            // three documents (API_CONTRACT_CYCLE6.md §41.1, ARCHITECTURE_CYCLE6.md §46.3 and the
            // comment right above) promise works: a master who has since left the company still has
            // future bookings, and the owner must be able to move them.
            totalDuration = booking.TotalDurationMinutes();
        }
        else
        {
            // The same triplet check POST /api/bookings performs, for the same reason: without it the
            // caller picks companyId for the membership check but masterId/serviceId from anywhere. Staff
            // of company A could then ask for a master of company B with manual=true and get that master's
            // whole day minus their occupancy — and occupancy is deliberately cross-company (Q9), so this
            // would be a weaker back door to exactly what GetOccupied above closes. 400, not 404: every
            // object exists, it is the combination that is wrong (API_CONTRACT.md §2.2).
            if (!await CompanyMembership.IsStaffAsync(db, companyId, masterId))
                return BadRequest("Этот мастер не работает в выбранной компании");

            // US-67 (ARCHITECTURE_CYCLE6.md §47.2): serviceIds is the multi-service form of serviceId;
            // when absent this is exactly the pre-cycle single-service path.
            var (resolved, error) = await ResolveTotalDurationAsync(companyId, masterId, serviceId, serviceIds);
            if (error is not null) return error;
            totalDuration = resolved!.Value;
        }

        // ARCHITECTURE_CYCLE6.md §46.3: manual+staff -> DefaultWindow; manual+extendedHours+staff ->
        // WholeDay; anything else (including extendedHours without manual, or a non-staff caller) -> None.
        var fallback = ScheduleFallbackPolicy.For(manual, extendedHours, isStaff);
        var slots = await slotService.GetAvailableSlotsAsync(companyId, masterId, totalDuration, date, fallback, excludeBookingId);
        return Ok(slots);
    }
    /// <summary>
    /// Shared by GetSlots/GetAvailability: resolves the effective service list (single serviceId, or
    /// serviceIds when supplied), validates it exactly as POST /api/bookings does
    /// (<see cref="BookingCreationService.ResolveServicesAsync"/>) and returns the summed duration. Returns a non-null
    /// ActionResult when validation fails, which callers must return directly.
    /// </summary>
    private async Task<(int? TotalDuration, ActionResult? Error)> ResolveTotalDurationAsync(
        Guid companyId, string masterId, Guid? serviceId, List<Guid>? serviceIds)
    {
        var (orderedIds, services, error) = await bookingCreation.ResolveServicesAsync(companyId, masterId, serviceId, serviceIds,
            checkMasterIsStaff: false);
        if (error is not null) return (null, error);

        var (totalDuration, _, _) = BookingServiceSelection.Aggregate(orderedIds, services.ToDictionary(s => s.Id));
        return (totalDuration, null);
    }

    /// <summary>
    /// US-65 (ARCHITECTURE_CYCLE6.md §45): the whole-month state in one anonymous request, instead of
    /// one GetSlots call per day. Same trust/validation shape as GetSlots — manual/extendedHours are
    /// only honored for staff of this company; everyone else always gets ScheduleFallback.None.
    /// </summary>
    [HttpGet("availability")]
    [EnableRateLimiting("availability")]
    public async Task<ActionResult<AvailabilityDto>> GetAvailability(
        [FromQuery] Guid companyId,
        [FromQuery] string masterId,
        [FromQuery] Guid? serviceId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] bool manual = false,
        [FromQuery] bool extendedHours = false,
        [FromQuery] List<Guid>? serviceIds = null,
        CancellationToken ct = default)
    {
        if (to < from) return BadRequest("to must not be before from");
        if (to.DayNumber - from.DayNumber > 30) return BadRequest("Диапазон не может превышать 31 день");

        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        if (from < todayUtc.AddDays(-1)) return BadRequest("from is too far in the past");

        var company = await db.Companies.FindAsync([companyId], ct);
        if (company is null) return NotFound("Company not found");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isStaff = userId is not null &&
            (User.IsInRole("SuperAdmin") || await CompanyMembership.IsStaffAsync(db, companyId, userId));
        var honorManual = manual && isStaff;

        var horizonDays = BookingHorizon.Normalize(company.BookingHorizonDays);
        var horizonLastDate = BookingHorizon.LastBookableDate(todayUtc, horizonDays);
        if (!honorManual && to > horizonLastDate)
            return BadRequest($"Записаться можно не дальше чем на {horizonDays} дней вперёд");

        if (!await CompanyMembership.IsStaffAsync(db, companyId, masterId))
            return BadRequest("Этот мастер не работает в выбранной компании");

        // US-67 (ARCHITECTURE_CYCLE6.md §47.2): same resolution GetSlots uses, so the calendar and the
        // day's slot grid can never disagree about the visit's total duration.
        var (totalDuration, durationError) = await ResolveTotalDurationAsync(companyId, masterId, serviceId, serviceIds);
        if (durationError is not null) return durationError;

        // ARCHITECTURE_CYCLE10.md §103.1: exactly the same trust table as GetSlots (BookingsController
        // ~lines 121-125) — extendedHours without honorManual is None, same as everyone else.
        var fallback = ScheduleFallbackPolicy.For(manual, extendedHours, isStaff);
        var (defaultStart, defaultEnd) = slotService.GetDefaultWindow();
        var days = await availabilityService.GetAvailabilityAsync(
            companyId, masterId, totalDuration!.Value, from, to, fallback, defaultStart, defaultEnd, honorManual);

        return Ok(new AvailabilityDto(from, to, totalDuration.Value, SlotCalculator.StepMinutes,
            horizonDays, horizonLastDate, days, honorManual));
    }

    // Cycle 22 P5 (§385): the shared rule lives in BookingEndpointHelpers, unchanged.
    private Task<bool> CanManageBookingAsync(Booking booking, string userId) =>
        BookingEndpointHelpers.CanManageBookingAsync(db, User, booking, userId);
}
