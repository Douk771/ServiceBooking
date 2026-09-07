using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BookingsController(AppDbContext db, SlotService slotService, CaptchaService captchaService, SubscriptionResolver subscriptionResolver) : ControllerBase
{
    [HttpGet("occupied")]
    [Authorize]
    public async Task<ActionResult<List<OccupiedRangeDto>>> GetOccupied(
        [FromQuery] string masterId,
        [FromQuery] DateOnly date)
    {
        // masterId isn't secret (the public GET /api/companies/{id}/masters lists every master's id),
        // so this endpoint used to let anyone anonymous pull any master's occupied hours across every
        // company they work in (audit E3/Q9). Now it requires the caller to actually have a reason to
        // know: SuperAdmin, the master themselves, or staff of at least one company the master also
        // belongs to.
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var canView = User.IsInRole("SuperAdmin") || userId == masterId ||
            await db.CompanyMembers.AnyAsync(cm => cm.UserId == userId &&
                (cm.Role == UserRole.Master || cm.Role == UserRole.CompanyOwner) &&
                db.CompanyMembers.Any(m => m.UserId == masterId && m.CompanyId == cm.CompanyId));
        if (!canView) return Forbid();

        // Occupancy is deliberately NOT scoped by company: a master who works for two businesses is
        // still one person, so a booking made in company A must block the same time in company B.
        // Working hours ARE scoped by company (a master can keep different schedules) — the asymmetry
        // is intentional (ARCHITECTURE.md §2.3, decision Q9). No companyId parameter is introduced here.
        var bookings = await db.Bookings
            .Where(b => b.MasterId == masterId && b.Date == date && b.Status != BookingStatus.Cancelled)
            .Select(b => new OccupiedRangeDto(b.StartTime, b.EndTime))
            .ToListAsync();
        return Ok(bookings);
    }

    [HttpGet("slots")]
    public async Task<ActionResult<List<TimeSlotResult>>> GetSlots(
        [FromQuery] Guid companyId,
        [FromQuery] string masterId,
        [FromQuery] Guid serviceId,
        [FromQuery] DateOnly date,
        [FromQuery] bool manual = false)
    {
        // `manual` is client-supplied, so only honor it once we've independently verified the caller
        // actually works in THIS company — same trust bar BookingsController.Create uses for
        // isStaffManualBooking. Anyone else gets the regular schedule-gated grid, same as a guest
        // (closes the A1 bypass: previously any authenticated user could pass manual=true).
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isStaff = userId is not null &&
            (User.IsInRole("SuperAdmin") || await CompanyMembership.IsStaffAsync(db, companyId, userId));

        // The same triplet check POST /api/bookings performs, for the same reason: without it the
        // caller picks companyId for the membership check but masterId/serviceId from anywhere. Staff
        // of company A could then ask for a master of company B with manual=true and get that master's
        // whole day minus their occupancy — and occupancy is deliberately cross-company (Q9), so this
        // would be a weaker back door to exactly what GetOccupied above closes. 400, not 404: every
        // object exists, it is the combination that is wrong (API_CONTRACT.md §2.2).
        var service = await db.Services.FindAsync(serviceId);
        if (service is null) return NotFound("Service not found");
        if (service.CompanyId != companyId) return BadRequest("Service does not belong to this company");
        if (!await CompanyMembership.IsStaffAsync(db, companyId, masterId))
            return BadRequest("Master does not work for this company");

        var allowWithoutSchedule = manual && isStaff;
        var slots = await slotService.GetAvailableSlotsAsync(companyId, masterId, serviceId, date, allowWithoutSchedule);
        return Ok(slots);
    }

    [HttpPost]
    public async Task<ActionResult<BookingDto>> Create(CreateBookingDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAuthenticated = userId is not null;

        // Company must exist before anything else — including for an authenticated caller. Previously
        // this check only ran on the guest branch, so a logged-in caller with a bad CompanyId fell
        // through to a 500 from a broken FK instead of a clean 404 (US-05).
        var company = await db.Companies.FindAsync(dto.CompanyId);
        if (company is null) return NotFound("Company not found");

        // A booking is a "staff manual booking" when an authenticated caller who actually works in THIS
        // company supplies guest details to record a walk-in for a third party. Everything else is an
        // online self-booking: a guest booking for themselves, or an authenticated client booking for
        // themselves.
        var isManualBooking = !string.IsNullOrEmpty(dto.GuestName);
        var isStaff = isAuthenticated &&
            (User.IsInRole("SuperAdmin") || await CompanyMembership.IsStaffAsync(db, dto.CompanyId, userId!));
        var isStaffManualBooking = isManualBooking && isStaff;

        // Anyone who supplies guest details without actually working here is not staff — they are a guest
        // with an account, and they go through the exact same gates a guest does (self-booking toggle,
        // captcha, tariff). This is the A1 bypass: previously `guestName` alone was enough to skip all four.
        var isGuestPath = !isAuthenticated || (isManualBooking && !isStaff);

        // The self-booking toggle governs every booking the public makes of its own accord, not just
        // anonymous ones: a logged-in client booking themselves is online self-booking too. It used to
        // sit inside the guest branch, so an authenticated client could ignore the switch entirely by
        // calling the API directly — the same shape of hole as the guestName bypass this cycle closed.
        // Staff are exempt: recording a walk-in is their tool, and the toggle is about the storefront.
        if (!isStaff && !company.AllowSelfBooking) return Forbid();

        if (isGuestPath)
        {
            // Guest booking is bot-protected by Yandex SmartCaptcha. When enforced (server key set, or
            // Production) a valid token is mandatory and validation fails closed; in Development without
            // a key it's skipped. See CaptchaService.
            if (captchaService.IsEnforced)
            {
                if (string.IsNullOrEmpty(dto.CaptchaToken))
                    return BadRequest("Captcha required for guest booking");

                var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                if (!await captchaService.ValidateAsync(dto.CaptchaToken, ip))
                    return BadRequest("Invalid captcha");
            }

            if (string.IsNullOrEmpty(dto.GuestName) || string.IsNullOrEmpty(dto.GuestPhone))
                return BadRequest("Name and phone are required for guest booking");
        }

        // US-26: canonical form is what gets stored, for guest bookings same as everywhere else —
        // otherwise the same walk-in phoned in as "8 999..." and booked online as "+7 999..." would
        // show up as two different people in MastersController.GetClients.
        var guestPhone = dto.GuestPhone;
        if (!string.IsNullOrEmpty(guestPhone))
        {
            if (!PhoneNormalizer.TryNormalize(guestPhone, out var canonicalGuestPhone))
                return BadRequest("Phone number must contain 10 to 15 digits.");
            guestPhone = canonicalGuestPhone;
        }

        // Plan: Free permits ONLY staff manual bookings. Any online self-booking — a guest booking for
        // themselves, or an authenticated client booking for themselves — requires the company's owner
        // account to be on a plan whose tariff config allows online booking (the resolver already
        // treats an inactive/expired subscription as Free).
        var effectivePlan = await subscriptionResolver.GetEffectivePlanAsync(dto.CompanyId);
        if (!effectivePlan.AllowOnlineBooking && !isStaffManualBooking)
            return StatusCode(402, "Online booking requires a paid subscription.");

        var service = await db.Services.FindAsync(dto.ServiceId);
        if (service is null) return NotFound("Service not found");
        // Objects exist but their combination doesn't make sense — 400, not 404 (ARCHITECTURE.md §14.4).
        if (service.CompanyId != dto.CompanyId) return BadRequest("Service does not belong to this company");
        if (!service.IsActive) return BadRequest("Service is not available");
        if (!await CompanyMembership.IsStaffAsync(db, dto.CompanyId, dto.MasterId))
            return BadRequest("Master is not a staff member of this company");

        var slotEnd = dto.StartTime.AddMinutes(service.DurationMinutes);

        // Not in the past, and doesn't wrap past midnight (TimeOnly can't represent 24:00, so an
        // overflowing slot would otherwise silently give EndTime < StartTime). Applies to every path,
        // including staff manual bookings — Q7's relaxation is about working hours, not about the past.
        if (!IsBookableMoment(dto.Date, dto.StartTime, service.DurationMinutes))
            return Conflict("Time slot is no longer available");

        AppUser? client = null;
        if (isAuthenticated && !isManualBooking)
            client = await db.Users.FindAsync(userId);

        // Prepayment is gated the same way as public listing: the owner's own toggle
        // (Company.RequirePrepayment) AND the tariff's AllowOnlinePayment — mirrors CompanyDto.PrepaymentEnabled.
        // Applies to every non-staff path (a client who supplied guestName is still an online self-booking).
        var requiresPrepayment = !isStaffManualBooking && effectivePlan.AllowOnlinePayment && company.RequirePrepayment;

        // Snapshot the master's CURRENT commission rate for this company onto the booking, the same way
        // Price snapshots the service's price — see the comment on Booking.CommissionPercent. Read here,
        // at creation time, not looked up later by the report from a CompanyMembers row that may have
        // changed rate or been deleted entirely.
        var masterCommissionPercent = await db.CompanyMembers
            .Where(cm => cm.CompanyId == dto.CompanyId && cm.UserId == dto.MasterId)
            .Select(cm => cm.CommissionPercent)
            .FirstOrDefaultAsync();

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            CompanyId = dto.CompanyId,
            ServiceId = dto.ServiceId,
            MasterId = dto.MasterId,
            // A client who supplies guestName without actually being staff is still the owner of the
            // booking (A6 fix carried through here too) — only a genuine staff manual booking leaves
            // ClientId unset. Previously `isManualBooking ? null : userId` gave such a client an
            // ownerless "guest" booking that anyone could later review (see ReviewsController).
            ClientId = isStaffManualBooking ? null : userId,
            GuestName = dto.GuestName,
            GuestPhone = guestPhone,
            GuestEmail = dto.GuestEmail,
            Date = dto.Date,
            StartTime = dto.StartTime,
            EndTime = slotEnd,
            Notes = dto.Notes,
            Status = BookingStatus.Confirmed,
            PaymentStatus = requiresPrepayment ? PaymentStatus.Pending : PaymentStatus.NotRequired,
            Price = service.Price,
            CommissionPercent = masterCommissionPercent
        };

        // Serialize concurrent create/reschedule requests for the same master+date so the
        // conflict check below and the insert are atomic — otherwise two requests can both pass
        // the check for the same free slot and both succeed, double-booking the master.
        await using var transaction = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"booking-slot:{dto.MasterId}:{dto.Date:O}");

        // Occupancy is deliberately NOT scoped by company: a master who works for two businesses is
        // still one person, so a booking made in company A must block the same time in company B.
        // Working hours ARE scoped by company (a master can keep different schedules) — the asymmetry
        // is intentional (ARCHITECTURE.md §2.3).
        var existingBookings = await db.Bookings
            .Where(b => b.MasterId == dto.MasterId && b.Date == dto.Date && b.Status != BookingStatus.Cancelled)
            .Select(b => new TimeRange(b.StartTime, b.EndTime))
            .ToListAsync();

        bool slotOk;
        if (isStaffManualBooking)
        {
            // Decision Q7: staff may book any free time, no schedule/breaks/grid rule applies — only the
            // existing conflict check (unchanged expression, dating back to before this cycle).
            slotOk = !existingBookings.Any(b => b.Start < slotEnd && b.End > dto.StartTime);
        }
        else
        {
            // Everyone else gets exactly the rule GET /api/bookings/slots would have offered them: the
            // same SlotCalculator, so the two can never drift apart (US-03).
            var workingHours = await db.WorkingHours
                .Include(wh => wh.Breaks)
                .FirstOrDefaultAsync(wh => wh.MasterId == dto.MasterId && wh.CompanyId == dto.CompanyId
                    && wh.Date == dto.Date && wh.IsWorking);
            var breaks = workingHours?.Breaks.Select(b => new TimeRange(b.StartTime, b.EndTime)).ToList() ?? [];

            slotOk = SlotCalculator.IsSlotAllowed(dto.StartTime, service.DurationMinutes,
                workingHours?.StartTime, workingHours?.EndTime, breaks, existingBookings, allowWithoutSchedule: false);
        }

        if (!slotOk) return Conflict("Time slot is no longer available");

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        var master = await db.Users.FindAsync(dto.MasterId);
        booking.Company = company;
        var clientName = client is not null
            ? $"{client.FirstName} {client.LastName}"
            : dto.GuestName ?? "Guest";

        return CreatedAtAction(nameof(GetById), new { id = booking.Id },
            MapToDto(booking, service, master!, clientName));
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<BookingDto>> GetById(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var booking = await db.Bookings
            .Include(b => b.Service)
            .Include(b => b.Master)
            .Include(b => b.Client)
            .Include(b => b.Company)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (booking is null) return NotFound();

        var canView = booking.ClientId == userId || await CanManageBookingAsync(booking, userId);
        if (!canView) return Forbid();

        var clientName = booking.Client is not null
            ? $"{booking.Client.FirstName} {booking.Client.LastName}"
            : booking.GuestName ?? "Guest";

        return Ok(MapToDto(booking, booking.Service, booking.Master, clientName));
    }

    // GET /api/bookings/my removed (US-22, BREAKING № 2, API_CONTRACT.md §3.3): fully superseded by
    // GET /api/bookings/client, which does everything this did plus a status filter. Its only consumer
    // (frontend/src/api/bookings.ts) already moved to /client.

    [HttpGet("client")]
    [Authorize]
    public async Task<ActionResult<List<BookingDto>>> GetClientBookings([FromQuery] string? status)
    {
        if (!BookingFilters.TryParseClientStatus(status, out var filter))
            return BadRequest("Unknown status filter. Expected: upcoming, Pending, Confirmed, Cancelled, Completed, NoShow.");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var query = db.Bookings
            .Include(b => b.Service)
            .Include(b => b.Master)
            .Include(b => b.Client)
            .Include(b => b.Company)
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

        var bookings = await query.OrderByDescending(b => b.Date).ThenByDescending(b => b.StartTime).ToListAsync();
        return Ok(bookings.Select(b =>
        {
            var name = b.Client is not null ? $"{b.Client.FirstName} {b.Client.LastName}" : b.GuestName ?? "Guest";
            return MapToDto(b, b.Service, b.Master, name);
        }));
    }

    [HttpGet("master")]
    [Authorize(Roles = "Master,CompanyOwner")]
    public async Task<ActionResult<List<BookingDto>>> GetMasterBookings([FromQuery] DateOnly? date, [FromQuery] DateOnly? to)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var query = db.Bookings
            .Include(b => b.Service)
            .Include(b => b.Master)
            .Include(b => b.Client)
            .Include(b => b.Company)
            .Where(b => b.MasterId == userId);

        if (date.HasValue)
            query = query.Where(b => b.Date >= date.Value);
        if (to.HasValue)
            query = query.Where(b => b.Date <= to.Value);

        var bookings = await query
            .OrderBy(b => b.Date)
            .ThenBy(b => b.StartTime)
            .ToListAsync();

        return Ok(bookings.Select(b =>
        {
            var name = b.Client is not null ? $"{b.Client.FirstName} {b.Client.LastName}" : b.GuestName ?? "Guest";
            return MapToDto(b, b.Service, b.Master, name);
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
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id:guid}/reschedule")]
    [Authorize]
    public async Task<IActionResult> Reschedule(Guid id, [FromBody] RescheduleDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var booking = await db.Bookings.Include(b => b.Service).FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null) return NotFound();
        if (!await CanManageBookingAsync(booking, userId)) return Forbid();

        if (booking.Status == BookingStatus.Cancelled || booking.Status == BookingStatus.Completed)
            return BadRequest("Cannot reschedule a cancelled or completed booking");

        var slotEnd = dto.StartTime.AddMinutes(booking.Service.DurationMinutes);

        // This endpoint is staff-only (CanManageBookingAsync above lets in only the assigned master,
        // the company's owner, or SuperAdmin — a client can never reach here, they only have Cancel).
        // By decision Q7 that means the SAME relaxed rule as a staff manual booking in Create: any free
        // time, no working-hours/breaks/grid check. Deliberately NOT validated against WorkingHours —
        // see ARCHITECTURE.md §3.4/§14.1: RescheduleModal's grid doesn't know the master's schedule, so a
        // full validation would reject slots the UI itself offered, with no way to explain why.
        if (!IsBookableMoment(dto.Date, dto.StartTime, booking.Service.DurationMinutes))
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

        booking.Date = dto.Date;
        booking.StartTime = dto.StartTime;
        booking.EndTime = slotEnd;
        booking.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return NoContent();
    }

    [HttpPatch("{id:guid}/cancel")]
    [Authorize]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] string? reason)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var booking = await db.Bookings.FindAsync(id);
        if (booking is null) return NotFound();

        var canCancel = booking.ClientId == userId || await CanManageBookingAsync(booking, userId);
        if (!canCancel) return Forbid();

        // US-06: the reason now actually reaches the other side (BookingDto.cancellationReason), so it
        // needs the same length guard every other free-text field in the product gets.
        if (reason is { Length: > 300 })
            return BadRequest("Cancellation reason must be 300 characters or fewer.");

        booking.Status = BookingStatus.Cancelled;
        booking.CancellationReason = reason;
        booking.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return NoContent();
    }

    // Single source of truth for "can this caller act on this booking as staff": the assigned master,
    // SuperAdmin, or the CompanyOwner of the booking's company. Used by every staff operation (view,
    // complete, mark-paid, no-show, reschedule, cancel) so the rule can't drift between them again —
    // cancel used to be missing the CompanyOwner branch that all the others had.
    // The one rule about *when* a booking may sit, shared by Create and Reschedule so the two can't
    // drift apart: not in the past, and not wrapping past midnight (TimeOnly can't represent 24:00, so
    // an overflowing slot would otherwise silently produce EndTime < StartTime). This applies to every
    // path including staff manual bookings — Q7 relaxes working hours, not the past. UTC is the
    // project-wide reference until timezones land; see ARCHITECTURE.md §2.5.
    private static bool IsBookableMoment(DateOnly date, TimeOnly startTime, int durationMinutes)
    {
        var nowUtc = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(nowUtc);
        var nowTime = TimeOnly.FromDateTime(nowUtc);

        var isInThePast = date < today || (date == today && startTime < nowTime);
        var overflowsIntoNextDay =
            startTime.ToTimeSpan() + TimeSpan.FromMinutes(durationMinutes) >= TimeSpan.FromDays(1);

        return !isInThePast && !overflowsIntoNextDay;
    }

    private async Task<bool> CanManageBookingAsync(Booking booking, string userId)
    {
        if (booking.MasterId == userId || User.IsInRole("SuperAdmin")) return true;
        return await db.CompanyMembers.AnyAsync(cm =>
            cm.CompanyId == booking.CompanyId && cm.UserId == userId && cm.Role == UserRole.CompanyOwner);
    }

    private static BookingDto MapToDto(Booking b, Service s, AppUser master, string clientName) =>
        new(b.Id, b.CompanyId, b.Company?.Name ?? "", b.Company?.Slug ?? "", b.ServiceId, s.Name, b.MasterId,
            $"{master.FirstName} {master.LastName}", b.ClientId, clientName,
            b.GuestPhone ?? b.Client?.PhoneNumber, b.GuestEmail ?? b.Client?.Email,
            b.Date, b.StartTime, b.EndTime, b.Status, b.PaymentStatus, b.Price, b.CancellationReason,
            b.Notes, b.CreatedAt);
}
