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
using static ServiceBooking.API.Controllers.BookingEndpointHelpers;

namespace ServiceBooking.API.Controllers;
[ApiController]
[Route("api/[controller]")]
public class BookingsController(
    AppDbContext db, SlotService slotService,
    SubscriptionResolver subscriptionResolver,
    NotificationScheduler notificationScheduler, ServiceBooking.API.Services.Notifications.StaffPushScheduler staffPushScheduler,
    BookingEventLog eventLog, BookingActorResolver actorResolver,
    ILogger<BookingsController> logger, BookingCreationService bookingCreation) : ControllerBase
{
    // Cycle 22 P5 (§378): occupied/slots/availability moved to BookingAvailabilityController; the body of
    // Create to BookingCreationService, unchanged.
    [HttpPost]
    [EnableRateLimiting("booking-create")]
    public async Task<ActionResult<BookingDto>> Create(CreateBookingDto dto)
    {        var result = await bookingCreation.CreateAsync(
            dto, User, HttpContext.Connection.RemoteIpAddress?.ToString(), HttpContext.RequestAborted);
        if (result.Error is not null) return result.Error;

        return CreatedAtAction(nameof(GetById), new { id = result.BookingId }, result.Dto);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<BookingDto>> GetById(Guid id, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var booking = await db.Bookings
            .AsNoTracking()
            .Include(b => b.Service)
            .Include(b => b.Master)
            .Include(b => b.Client)
            .Include(b => b.Company)
            .Include(b => b.BookingServices)
            .FirstOrDefaultAsync(b => b.Id == id, ct);

        if (booking is null) return NotFound();

        var canView = booking.ClientId == userId || await CanManageBookingAsync(booking, userId);
        if (!canView) return Forbid();

        var clientName = booking.Client is not null
            ? $"{booking.Client.FirstName} {booking.Client.LastName}"
            : booking.GuestName ?? "Guest";

        var reminderStatus = await ReminderStatusForAsync(booking.Id, ct);

        // API_CONTRACT_CYCLE10.md §123: historyEventCount is filled here only for staff of this booking's
        // company (or SuperAdmin) — the same "personnel of the company" bar §122's history endpoint uses,
        // not the narrower CanManageBookingAsync (assigned master + owner) that gated `canView` above.
        var isStaffOfCompany = User.IsInRole("SuperAdmin") ||
            (userId is not null && await CompanyMembership.IsStaffAsync(db, booking.CompanyId, userId));
        int? historyEventCount = null;
        if (isStaffOfCompany)
            historyEventCount = await db.BookingEvents.CountAsync(e => e.BookingId == booking.Id, ct);

        return Ok(MapToDto(booking, booking.Service, booking.Master, clientName, reminderStatus, historyEventCount));
    }

    // GET /api/bookings/my removed (US-22, BREAKING № 2, API_CONTRACT.md §3.3): fully superseded by
    // GET /api/bookings/client, which does everything this did plus a status filter. Its only consumer
    // (frontend/src/api/bookings.ts) already moved to /client.

    [HttpGet("client")]
    [Authorize]
    public async Task<ActionResult<List<BookingDto>>> GetClientBookings([FromQuery] string? status, CancellationToken ct)
    {
        if (!BookingFilters.TryParseClientStatus(status, out var filter))
            return BadRequest("Unknown status filter. Expected: upcoming, Pending, Confirmed, Cancelled, Completed, NoShow.");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var query = db.Bookings
            .AsNoTracking()
            .Include(b => b.Service)
            .Include(b => b.Master)
            .Include(b => b.Client)
            .Include(b => b.Company)
            .Include(b => b.BookingServices)
            .Where(b => b.ClientId == userId);

        var nowUtc = DateTime.UtcNow;
        query = filter.Kind switch
        {
            // One clock read, not two: reading DateTime.UtcNow twice can straddle midnight and produce
            // a mismatched (date, time) pair. UTC is the project-wide reference until timezones land
            // (deliberately out of this cycle) — for the target zones (UTC+3..+12) it errs toward
            // showing a booking slightly longer, never toward hiding an upcoming one.
            ClientStatusFilterKind.Upcoming => query.Where(BookingFilters.Upcoming(
                DateOnly.FromDateTime(nowUtc), TimeOnly.FromDateTime(nowUtc))),
            ClientStatusFilterKind.ByStatus => query.Where(b => b.Status == filter.Status),
            _ => query
        };

        var bookings = await query.OrderByDescending(b => b.Date).ThenByDescending(b => b.StartTime).ToListAsync(ct);

        // ARCHITECTURE_CYCLE15.md §257.8/§286 — one batched plan lookup for the whole page, not one
        // query per booking (Company is already Include()d above, so this is the only extra round trip,
        // and it's per-caller-page, not per-booking — §286's "zero extra requests per booking" promise).
        var plansByCompany = await subscriptionResolver.GetEffectivePlansAsync(bookings.Select(b => b.CompanyId));
        var nowForWindowUtc = DateTime.UtcNow;

        return Ok(bookings.Select(b =>
        {
            var name = b.Client is not null ? $"{b.Client.FirstName} {b.Client.LastName}" : b.GuestName ?? "Guest";
            var company = b.Company;
            var minHours = ClientRescheduleWindow.Normalize(company.ClientRescheduleMinHours);
            var plan = plansByCompany.GetValueOrDefault(b.CompanyId);
            var currentVisitStartUtc = NotificationTiming.ComputeVisitStartUtc(b.Date, b.StartTime, company.TimeZoneId);
            // §286 — a hint, not a re-check of the target time (there isn't one yet): only the CURRENT
            // visit's end of the window and the other server-side gates are evaluated here.
            var rescheduleAllowed =
                (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed) &&
                company.AllowSelfBooking &&
                (plan?.AllowOnlineBooking ?? false) &&
                nowForWindowUtc <= currentVisitStartUtc - TimeSpan.FromHours(minHours);

            // ARCHITECTURE_CYCLE17.md §304.3, API_CONTRACT_CYCLE17.md §323 — deliberately does NOT
            // fold in AllowSelfBooking/plan.AllowOnlineBooking: cancel depends on neither (§0-bis).
            var cancelAllowed =
                (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed) &&
                ClientRescheduleWindow.CanClientCancel(nowForWindowUtc, currentVisitStartUtc, minHours);

            // П8/API_CONTRACT_CYCLE10.md §123: reminderStatus/historyEventCount always null on the
            // client's own endpoint — not filtered on the frontend, simply never computed here.
            return MapToDto(b, b.Service, b.Master, name, reminderStatus: null, historyEventCount: null,
                clientRescheduleAllowed: rescheduleAllowed, clientRescheduleMinHours: minHours,
                companyBookingHorizonDays: BookingHorizon.Normalize(company.BookingHorizonDays),
                clientCancelAllowed: cancelAllowed);
        }));
    }

    [HttpGet("master")]
    [Authorize(Roles = "Master,CompanyOwner")]
    public async Task<ActionResult<List<BookingDto>>> GetMasterBookings([FromQuery] DateOnly? date, [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var query = db.Bookings
            .AsNoTracking()
            .Include(b => b.Service)
            .Include(b => b.Master)
            .Include(b => b.Client)
            .Include(b => b.Company)
            .Include(b => b.BookingServices)
            .Where(b => b.MasterId == userId);

        if (date.HasValue)
            query = query.Where(b => b.Date >= date.Value);
        if (to.HasValue)
            query = query.Where(b => b.Date <= to.Value);

        var bookings = await query
            .OrderBy(b => b.Date)
            .ThenBy(b => b.StartTime)
            .ToListAsync(ct);

        // API_CONTRACT_CYCLE4.md §30.3: one batched query for the whole page's reminder status, not one
        // per booking — same "batch, don't loop" convention as everything else added this cycle.
        var reminderStatusByBooking = await ReminderStatusesForAsync(bookings.Select(b => b.Id), ct);
        // API_CONTRACT_CYCLE10.md §123: same batching convention for historyEventCount — one grouping
        // query for the whole page, not N+1.
        var historyCountByBooking = await HistoryEventCountsForAsync(bookings.Select(b => b.Id), ct);

        return Ok(bookings.Select(b =>
        {
            var name = b.Client is not null ? $"{b.Client.FirstName} {b.Client.LastName}" : b.GuestName ?? "Guest";
            return MapToDto(b, b.Service, b.Master, name, reminderStatusByBooking.GetValueOrDefault(b.Id),
                historyCountByBooking.GetValueOrDefault(b.Id));
        }));
    }

    [HttpPatch("{id:guid}/complete")]
    [Authorize(Roles = "Master,CompanyOwner,SuperAdmin")]
    public async Task<IActionResult> Complete(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var booking = await db.Bookings.FindAsync(id);
        if (booking is null) return NotFound();
        if (!await CanManageBookingAsync(booking, userId)) return Forbid();

        if (booking.Status == BookingStatus.Cancelled) return BadRequest("Booking is cancelled");
        booking.Status = BookingStatus.Completed;
        booking.UpdatedAt = DateTime.UtcNow;

        // ARCHITECTURE_CYCLE10.md §105: no transaction needed here — a single SaveChangesAsync is
        // already atomic, and no try/catch (deliberately, unlike NotificationScheduler elsewhere).
        var completeActor = await actorResolver.ResolveAsync(User, booking);
        eventLog.Append(booking, BookingEventKind.Completed,
            completeActor.Kind, completeActor.UserId, completeActor.NameSnapshot, completeActor.RoleSnapshot);

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id:guid}/mark-paid")]
    [Authorize(Roles = "Master,CompanyOwner,SuperAdmin")]
    public async Task<IActionResult> MarkPaid(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var booking = await db.Bookings.FindAsync(id);
        if (booking is null) return NotFound();
        if (!await CanManageBookingAsync(booking, userId)) return Forbid();

        booking.PaymentStatus = PaymentStatus.Paid;
        booking.UpdatedAt = DateTime.UtcNow;

        var markPaidActor = await actorResolver.ResolveAsync(User, booking);
        eventLog.Append(booking, BookingEventKind.PaymentMarked,
            markPaidActor.Kind, markPaidActor.UserId, markPaidActor.NameSnapshot, markPaidActor.RoleSnapshot);

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id:guid}/noshow")]
    [Authorize(Roles = "Master,CompanyOwner,SuperAdmin")]
    public async Task<IActionResult> NoShow(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var booking = await db.Bookings.FindAsync(id);
        if (booking is null) return NotFound();
        if (!await CanManageBookingAsync(booking, userId)) return Forbid();

        if (booking.Status == BookingStatus.Cancelled) return BadRequest("Booking is cancelled");
        booking.Status = BookingStatus.NoShow;
        booking.UpdatedAt = DateTime.UtcNow;

        var noShowActor = await actorResolver.ResolveAsync(User, booking);
        eventLog.Append(booking, BookingEventKind.NoShow,
            noShowActor.Kind, noShowActor.UserId, noShowActor.NameSnapshot, noShowActor.RoleSnapshot);

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id:guid}/reschedule")]
    [Authorize]
    [EnableRateLimiting("booking-reschedule")]
    public async Task<IActionResult> Reschedule(Guid id, [FromBody] RescheduleDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var booking = await db.Bookings.Include(b => b.Service).Include(b => b.BookingServices)
            .Include(b => b.Company)
            .FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null) return NotFound();

        // ARCHITECTURE_CYCLE15.md §257.1/§287.1 — one route, the caller's authority is computed from the
        // database, never from the request body (RescheduleDto gets no new fields this cycle — a client
        // physically cannot ask for staff's relaxed rules). Staff is checked first: a master who is ALSO
        // the booking's own client (shouldn't normally happen, but the DB doesn't forbid it) still gets
        // staff's relaxed rules, not the stricter client ones.
        var authority = await CanManageBookingAsync(booking, userId)
            ? RescheduleAuthority.Staff
            : booking.ClientId == userId ? RescheduleAuthority.ClientOwner : RescheduleAuthority.None;

        // §257.5/§287.3 (BREAKING № 1): a bare 404 for anyone else, same body as "booking doesn't
        // exist" — this endpoint must not confirm someone else's booking id is real.
        if (authority == RescheduleAuthority.None) return NotFound();

        // US-67 (ARCHITECTURE_CYCLE6.md §47.2): duration comes from the sum of the visit's
        // BookingService rows, not booking.Service.DurationMinutes — a pre-cycle booking has exactly
        // one such row (backfilled), so this is a no-op change for it.
        // The fallback (BookingServiceExtensions): if BookingServices is somehow empty, fall back to the
        // single legacy service rather than summing to zero — a row-less visit would otherwise
        // reschedule to EndTime == StartTime and silently collapse to nothing.
        var totalDurationMinutes = booking.TotalDurationMinutes();
        var slotEnd = dto.StartTime.AddMinutes(totalDurationMinutes);

        if (authority == RescheduleAuthority.Staff)
        {
            // Same as before this cycle: any status other than Cancelled/Completed; any free time, no
            // working-hours/breaks/grid check (Q7).
            if (booking.Status == BookingStatus.Cancelled || booking.Status == BookingStatus.Completed)
                return BadRequest("Cannot reschedule a cancelled or completed booking");
        }
        else
        {
            // §257.2/§287.2 — the client's own, strictly narrower rule set. Checked in the documented
            // order so the FIRST violated rule is the one the caller learns about.
            if (booking.Status != BookingStatus.Pending && booking.Status != BookingStatus.Confirmed)
                return BadRequest("Перенести можно только предстоящую запись");

            var company = booking.Company;
            if (!company.AllowSelfBooking) return Forbid();

            var plan = await subscriptionResolver.GetEffectivePlanAsync(booking.CompanyId);
            if (!plan.AllowOnlineBooking) return StatusCode(402, "Online booking requires a paid subscription.");

            var minHours = ClientRescheduleWindow.Normalize(company.ClientRescheduleMinHours);
            var nowUtc = DateTime.UtcNow;
            var currentVisitStartUtc = NotificationTiming.ComputeVisitStartUtc(booking.Date, booking.StartTime, company.TimeZoneId);
            var newVisitStartUtc = NotificationTiming.ComputeVisitStartUtc(dto.Date, dto.StartTime, company.TimeZoneId);
            if (!ClientRescheduleWindow.IsWithinWindow(nowUtc, currentVisitStartUtc, newVisitStartUtc, minHours))
                return BadRequest($"Перенести запись можно не позже чем за {minHours} ч до визита");

            var horizonDays = BookingHorizon.Normalize(company.BookingHorizonDays);
            if (!BookingHorizon.IsWithin(dto.Date, DateOnly.FromDateTime(nowUtc), horizonDays))
                return BadRequest($"Записаться можно не дальше чем на {horizonDays} дней вперёд");

            // §257.3/§287.2 п.8 — exactly the same grid GET /api/bookings/slots would compute for this
            // client (ScheduleFallback.None, own interval excluded). manual/extendedHours don't exist on
            // this path at all.
            var slots = await slotService.GetAvailableSlotsAsync(
                booking.CompanyId, booking.MasterId, totalDurationMinutes, dto.Date,
                ScheduleFallback.None, excludeBookingId: booking.Id);
            if (!slots.Any(s => s.Start == dto.StartTime))
                return Conflict("Time slot is no longer available");
        }

        // Deliberately NOT validated against WorkingHours for staff, and DO NOT "fix" that. The old
        // reason — "the reschedule grid is generated client-side and knows nothing of the master's
        // schedule" — stopped being true when F7 moved that grid onto GET /api/bookings/slots. The
        // reason now is the requirement itself: staff may book any time that suits them, schedule or no
        // schedule (`SPEC_CYCLE6_BOOKING_FIXES.md` §0.1, Q7). Validating here would take that away.
        // Applies to both branches (§257.2 п.9/§287.2 п.9): not in the past, not wrapping past midnight.
        if (!IsBookableMoment(dto.Date, dto.StartTime, totalDurationMinutes))
            return Conflict("Time slot is no longer available");

        // Same TOCTOU concern as Create: serialize concurrent reschedules/creates targeting this
        // master+date before checking for a conflict.
        await using var transaction = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"booking-slot:{booking.MasterId}:{dto.Date:O}");

        var conflict = await db.Bookings.AnyAsync(b =>
            b.Id != id &&
            b.MasterId == booking.MasterId &&
            b.Date == dto.Date &&
            b.Status != BookingStatus.Cancelled &&
            b.StartTime < slotEnd && b.EndTime > dto.StartTime);

        if (conflict) return Conflict("Time slot is no longer available");

        var previousDate = booking.Date;
        var previousStartTime = booking.StartTime;

        booking.Date = dto.Date;
        booking.StartTime = dto.StartTime;
        booking.EndTime = slotEnd;
        booking.UpdatedAt = DateTime.UtcNow;

        // ARCHITECTURE_CYCLE10.md §105: inside the same transaction as the update above, before
        // SaveChangesAsync — deliberately no try/catch.
        var rescheduleActor = await actorResolver.ResolveAsync(User, booking);
        eventLog.Append(booking, BookingEventKind.Rescheduled,
            rescheduleActor.Kind, rescheduleActor.UserId, rescheduleActor.NameSnapshot, rescheduleActor.RoleSnapshot,
            previousDate: previousDate, previousStartTime: previousStartTime,
            newDate: dto.Date, newStartTime: dto.StartTime);

        // ARCHITECTURE_CYCLE4.md §25.3: reschedules the queued Reminder and queues a BookingRescheduled
        // notification, in the same transaction as the booking's own update — see the comment in Create
        // for why a failure here is caught and logged rather than allowed to fail the reschedule itself.
        try
        {
            await notificationScheduler.OnBookingRescheduledAsync(booking, HttpContext.RequestAborted);

            // ARCHITECTURE_CYCLE15.md §257.6/§287.5 — only when the CLIENT made the move: staff moving
            // their own booking must not push themselves a notification about their own action (same
            // rule StaffPushScheduler.OnBookingCreatedAsync already applies to creation).
            if (authority == RescheduleAuthority.ClientOwner)
            {
                var serviceNames = booking.ServiceNames();
                await staffPushScheduler.OnBookingRescheduledAsync(booking, serviceNames, userId, HttpContext.RequestAborted);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reschedule notifications for booking {BookingId}", booking.Id);
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return NoContent();
    }

    // ARCHITECTURE_CYCLE15.md §257.1 — who is allowed to reschedule, computed strictly server-side.
    private enum RescheduleAuthority { None, Staff, ClientOwner }

    [HttpPatch("{id:guid}/cancel")]
    [Authorize]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] string? reason)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        // ARCHITECTURE_CYCLE17.md §304 — Company is needed for the client-owner window check; a single
        // Include() replaces FindAsync, not a second round trip (§316 "Масштабирование").
        var booking = await db.Bookings.Include(b => b.Company).FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null) return NotFound();

        // ARCHITECTURE_CYCLE17.md §304.1 — authority computed strictly server-side, staff checked
        // first (dead-on with Reschedule's RescheduleAuthority, §257.1 cycle 15). Not 404 for None —
        // that would be a breaking change to an already-shipped endpoint (§304.2/§322.3, долг C17-2).
        var authority = await CanManageBookingAsync(booking, userId)
            ? RescheduleAuthority.Staff
            : booking.ClientId == userId ? RescheduleAuthority.ClientOwner : RescheduleAuthority.None;
        if (authority == RescheduleAuthority.None) return Forbid();

        // US-06: the reason now actually reaches the other side (BookingDto.cancellationReason), so it
        // needs the same length guard every other free-text field in the product gets. Validation of
        // input comes before the window check (§313 CY17-B-07) — 400 is already spoken for by this.
        if (reason is { Length: > 300 })
            return BadRequest("Cancellation reason must be 300 characters or fewer.");

        // ARCHITECTURE_CYCLE17.md §304.1/§304.2 — window applies ONLY to the client-owner path; staff
        // are unaffected, byte-for-byte as before. 409, not 400 (§304.2 explains the asymmetry with
        // Reschedule's 400): 400 here is already occupied by the reason-length check above.
        if (authority == RescheduleAuthority.ClientOwner)
        {
            var minHours = ClientRescheduleWindow.Normalize(booking.Company.ClientRescheduleMinHours);
            var visitStartUtc = NotificationTiming.ComputeVisitStartUtc(booking.Date, booking.StartTime, booking.Company.TimeZoneId);
            if (!ClientRescheduleWindow.CanClientCancel(DateTime.UtcNow, visitStartUtc, minHours))
            {
                return Conflict(
                    $"Отменить запись можно не позже чем за {minHours} ч до визита. Чтобы отменить, свяжитесь с салоном.");
            }
        }

        booking.Status = BookingStatus.Cancelled;
        booking.CancellationReason = reason;
        booking.UpdatedAt = DateTime.UtcNow;

        var cancelActor = await actorResolver.ResolveAsync(User, booking);
        eventLog.Append(booking, BookingEventKind.Cancelled,
            cancelActor.Kind, cancelActor.UserId, cancelActor.NameSnapshot, cancelActor.RoleSnapshot,
            cancellationReason: reason);

        // ARCHITECTURE_CYCLE4.md §25.3: cancels the queued rows for this booking and queues a
        // BookingCancelled notification, in the same (implicit) transaction as the booking's own
        // SaveChangesAsync below — see the comment in Create for why a failure here is caught and logged.
        try
        {
            await notificationScheduler.OnBookingCancelledAsync(booking, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to queue cancellation notification for booking {BookingId}", booking.Id);
        }

        await db.SaveChangesAsync();

        return NoContent();
    }

    // Single source of truth for "can this caller act on this booking as staff": the assigned master,
    // SuperAdmin, or the CompanyOwner of the booking's company. Used by every staff operation (view,
    // complete, mark-paid, no-show, reschedule, cancel) so the rule can't drift between them again —
    // cancel used to be missing the CompanyOwner branch that all the others had.
    // Cycle 22 P5 (§385): the shared rule lives in BookingEndpointHelpers, unchanged.
    private Task<bool> CanManageBookingAsync(Booking booking, string userId) =>
        BookingEndpointHelpers.CanManageBookingAsync(db, User, booking, userId);


    // API_CONTRACT_CYCLE4.md §30.3. Picks the highest-Generation Reminder row for a booking (§23.5: a
    // reschedule supersedes the previous generation's row rather than mutating it), so a rescheduled
    // booking's card reflects the CURRENT reminder, not one already Cancelled by NotificationScheduler.
    private async Task<ReminderStatusDto?> ReminderStatusForAsync(Guid bookingId, CancellationToken ct)
    {
        var map = await ReminderStatusesForAsync([bookingId], ct);
        return map.GetValueOrDefault(bookingId);
    }

    private async Task<Dictionary<Guid, ReminderStatusDto>> ReminderStatusesForAsync(IEnumerable<Guid> bookingIds, CancellationToken ct)
    {
        var ids = bookingIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        // §375 F8: only the columns the status line needs — never the rendered Body.
        var rows = await db.OutboundNotifications.AsNoTracking()
            .Where(n => n.BookingId != null && ids.Contains(n.BookingId!.Value) && n.Type == NotificationType.Reminder)
            .Select(n => new
            {
                BookingId = n.BookingId, n.Generation, n.CreatedAt, n.Status, n.Reason, n.ChannelId, n.ReadAtUtc, n.AttemptCount,
            })
            .ToListAsync(ct);

        return rows.GroupBy(n => n.BookingId!.Value)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var latest = g.OrderByDescending(n => n.Generation).ThenByDescending(n => n.CreatedAt).First();
                    var text = NotificationTexts.StatusText(latest.Status, latest.Reason, latest.ChannelId, latest.ReadAtUtc, latest.AttemptCount);
                    return new ReminderStatusDto(latest.Status, $"напоминание {text}");
                });
    }

    /// <summary>
    /// ARCHITECTURE_CYCLE10.md §105/§123: one grouping query for the whole page's historyEventCount, the
    /// same "batch, don't loop" pattern as ReminderStatusesForAsync above.
    /// </summary>
    private async Task<Dictionary<Guid, int>> HistoryEventCountsForAsync(IEnumerable<Guid> bookingIds, CancellationToken ct)
    {
        var ids = bookingIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        return await db.BookingEvents.AsNoTracking()
            .Where(e => ids.Contains(e.BookingId))
            .GroupBy(e => e.BookingId)
            .Select(g => new { BookingId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BookingId, x => x.Count, ct);
    }

    /// <summary>
    /// ARCHITECTURE_CYCLE10.md §122: the full change journal for one booking. Access: staff of the
    /// booking's company (Master/CompanyOwner) or SuperAdmin — deliberately WIDER than
    /// CanManageBookingAsync (assigned master + owner), because US-123 says "персонал компании" sees it,
    /// not only whoever can act on the booking.
    ///
    /// ⚠️ 404, not 403, for everyone else — INCLUDING the client who owns this very booking. The journal
    /// is internal staff information (П8); the response code must not double as an oracle for "does a
    /// booking with this id exist", the same principle GetSlots already follows. Do not "fix" this to a
    /// 403 for an authenticated caller — that would leak existence to exactly the audience §122.3 says
    /// must not learn it.
    /// </summary>
    [HttpGet("{id:guid}/history")]
    [Authorize]
    public async Task<ActionResult<BookingHistoryDto>> GetHistory(Guid id, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var booking = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null) return NotFound();

        var isStaffOfCompany = User.IsInRole("SuperAdmin") ||
            await CompanyMembership.IsStaffAsync(db, booking.CompanyId, userId);
        if (!isStaffOfCompany) return NotFound();

        var events = await db.BookingEvents.AsNoTracking()
            .Where(e => e.BookingId == id)
            .OrderBy(e => e.OccurredAtUtc)
            .ToListAsync(ct);

        // §122.2: a booking predates the journal exactly when it has no Created event — no backfill
        // (decision П3), so this is computed, not stored.
        var precedesJournal = events.All(e => e.Kind != BookingEventKind.Created);

        // TD-05 read side (ARCHITECTURE_CYCLE16.md §247.3, no migration/backfill — §240.3). Catches
        // every row already accumulated BEFORE this cycle too, not only future deletions: one extra
        // indexed query per call (ids ≤ number of events on one booking), no separate phone-matching
        // logic here — that would be a sixth TD-03 place (§245.2/§247.2).
        const string deletedActorTombstone = "Удалённый пользователь";
        var actorIds = events.Where(e => e.ActorUserId != null).Select(e => e.ActorUserId!).Distinct().ToList();
        var deletedActorIds = actorIds.Count == 0 ? []
            : await db.Users.AsNoTracking()
                .Where(u => actorIds.Contains(u.Id) && u.DeletedAtUtc != null)
                .Select(u => u.Id).ToListAsync(ct);
        var deletedActorIdSet = deletedActorIds.ToHashSet();

        var eventDtos = events.Select(e =>
        {
            // §247.3, exactly: ActorKind == Client && ActorUserId ∈ deletedActorIds. Guest events have
            // no ActorUserId to look up (that's §247.5's named residual risk, closed on the WRITE side
            // instead — see DeleteAccount). Staff/SuperAdmin/System are a different subject and a
            // different retention schedule (D1/TD-18), untouched here.
            var isDeletedClient = e.ActorKind == BookingActorKind.Client
                && e.ActorUserId is not null && deletedActorIdSet.Contains(e.ActorUserId);
            var actorName = isDeletedClient ? deletedActorTombstone : e.ActorNameSnapshot;
            var actorLabel = isDeletedClient
                ? deletedActorTombstone
                : BookingEventTexts.ActorLabel(e.Kind, e.ActorKind, e.ActorNameSnapshot, e.ActorRoleSnapshot);

            return new BookingEventDto(
                e.Id, e.Kind, e.OccurredAtUtc,
                BookingEventTexts.Title(e.Kind),
                new BookingEventActorDto(e.ActorKind, actorName, e.ActorRoleSnapshot, actorLabel),
                e.Kind == BookingEventKind.Rescheduled && e.PreviousDate is not null && e.PreviousStartTime is not null
                    && e.NewDate is not null && e.NewStartTime is not null
                    ? new BookingRescheduleDto(e.PreviousDate.Value, e.PreviousStartTime.Value, e.NewDate.Value, e.NewStartTime.Value)
                    : null,
                e.Kind == BookingEventKind.Cancelled ? e.CancellationReason : null
            );
        }).ToList();

        return Ok(new BookingHistoryDto(id, precedesJournal, eventDtos));
    }
}
