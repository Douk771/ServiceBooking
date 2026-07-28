using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Core.Entities;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BookingsController(AppDbContext db, SlotService slotService, CaptchaService captchaService, SubscriptionResolver subscriptionResolver) : ControllerBase
{
    [HttpGet("occupied")]
    public async Task<ActionResult<List<OccupiedRangeDto>>> GetOccupied(
        [FromQuery] string masterId,
        [FromQuery] DateOnly date)
    {
        var bookings = await db.Bookings
            .Where(b => b.MasterId == masterId && b.Date == date && b.Status != BookingStatus.Cancelled)
            .Select(b => new OccupiedRangeDto(b.StartTime, b.EndTime))
            .ToListAsync();
        return Ok(bookings);
    }

    [HttpGet("slots")]
    public async Task<ActionResult<List<TimeSlotResult>>> GetSlots(
        [FromQuery] string masterId,
        [FromQuery] Guid serviceId,
        [FromQuery] DateOnly date,
        [FromQuery] bool manual = false)
    {
        // `manual` is client-supplied, so only honor it once we've independently verified the caller
        // is actually logged in — same trust bar BookingsController.Create already uses for
        // isStaffManualBooking.
        var allowWithoutSchedule = manual && User.Identity?.IsAuthenticated == true;
        var slots = await slotService.GetAvailableSlotsAsync(masterId, serviceId, date, allowWithoutSchedule);
        return Ok(slots);
    }

    [HttpPost]
    public async Task<ActionResult<BookingDto>> Create(CreateBookingDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAuthenticated = userId is not null;

        // A booking is a "staff manual booking" when an authenticated caller (a master/owner) supplies
        // guest details to record a walk-in for a third party. Everything else is an online self-booking:
        // either a guest booking for themselves, or an authenticated client booking for themselves.
        var isManualBooking = !string.IsNullOrEmpty(dto.GuestName);
        var isStaffManualBooking = isAuthenticated && isManualBooking;

        // Guest booking requires captcha
        if (!isAuthenticated)
        {
            var company = await db.Companies.FindAsync(dto.CompanyId);
            if (company is null) return NotFound("Company not found");
            if (!company.AllowSelfBooking) return Forbid();

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

        // Plan: Free permits ONLY staff manual bookings. Any online self-booking — a guest booking for
        // themselves, or an authenticated client booking for themselves — requires the company's owner
        // account to be on a plan whose tariff config allows online booking (the resolver already
        // treats an inactive/expired subscription as Free).
        var effectivePlan = await subscriptionResolver.GetEffectivePlanAsync(dto.CompanyId);
        if (!effectivePlan.AllowOnlineBooking && !isStaffManualBooking)
            return StatusCode(402, "Online booking requires a paid subscription.");

        var service = await db.Services.FindAsync(dto.ServiceId);
        if (service is null) return NotFound("Service not found");

        var slotEnd = dto.StartTime.AddMinutes(service.DurationMinutes);

        AppUser? client = null;
        if (isAuthenticated && !isManualBooking)
            client = await db.Users.FindAsync(userId);

        // Prepayment is gated the same way as public listing: the owner's own toggle
        // (Company.RequirePrepayment) AND the tariff's AllowOnlinePayment — mirrors CompanyDto.PrepaymentEnabled.
        var requiresPrepayment = !isManualBooking && effectivePlan.AllowOnlinePayment
            && (await db.Companies.FindAsync(dto.CompanyId))?.RequirePrepayment == true;

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            CompanyId = dto.CompanyId,
            ServiceId = dto.ServiceId,
            MasterId = dto.MasterId,
            ClientId = isManualBooking ? null : userId,
            GuestName = dto.GuestName,
            GuestPhone = dto.GuestPhone,
            GuestEmail = dto.GuestEmail,
            Date = dto.Date,
            StartTime = dto.StartTime,
            EndTime = slotEnd,
            Notes = dto.Notes,
            Status = BookingStatus.Confirmed,
            PaymentStatus = requiresPrepayment ? PaymentStatus.Pending : PaymentStatus.NotRequired,
            Price = service.Price
        };

        // Serialize concurrent create/reschedule requests for the same master+date so the
        // conflict check below and the insert are atomic — otherwise two requests can both pass
        // the check for the same free slot and both succeed, double-booking the master.
        await using var transaction = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"booking-slot:{dto.MasterId}:{dto.Date:O}");

        var conflict = await db.Bookings.AnyAsync(b =>
            b.MasterId == dto.MasterId &&
            b.Date == dto.Date &&
            b.Status != BookingStatus.Cancelled &&
            b.StartTime < slotEnd && b.EndTime > dto.StartTime);

        if (conflict) return Conflict("Time slot is no longer available");

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        var master = await db.Users.FindAsync(dto.MasterId);
        var bookingCompany = await db.Companies.FindAsync(dto.CompanyId);
        booking.Company = bookingCompany!;
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

    [HttpGet("my")]
    [Authorize]
    public async Task<ActionResult<List<BookingDto>>> GetMyBookings()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var bookings = await db.Bookings
            .Include(b => b.Service)
            .Include(b => b.Master)
            .Include(b => b.Client)
            .Include(b => b.Company)
            .Where(b => b.ClientId == userId)
            .OrderByDescending(b => b.Date)
            .ThenByDescending(b => b.StartTime)
            .ToListAsync();

        return Ok(bookings.Select(b =>
        {
            var name = b.Client is not null ? $"{b.Client.FirstName} {b.Client.LastName}" : b.GuestName ?? "Guest";
            return MapToDto(b, b.Service, b.Master, name);
        }));
    }

    [HttpGet("client")]
    [Authorize]
    public async Task<ActionResult<List<BookingDto>>> GetClientBookings([FromQuery] string? status)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var query = db.Bookings
            .Include(b => b.Service)
            .Include(b => b.Master)
            .Include(b => b.Client)
            .Include(b => b.Company)
            .Where(b => b.ClientId == userId);

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<BookingStatus>(status, out var s))
            query = query.Where(b => b.Status == s);

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
    private async Task<bool> CanManageBookingAsync(Booking booking, string userId)
    {
        if (booking.MasterId == userId || User.IsInRole("SuperAdmin")) return true;
        return await db.CompanyMembers.AnyAsync(cm =>
            cm.CompanyId == booking.CompanyId && cm.UserId == userId && cm.Role == UserRole.CompanyOwner);
    }

    private static BookingDto MapToDto(Booking b, Service s, AppUser master, string clientName) =>
        new(b.Id, b.CompanyId, b.Company?.Name ?? "", b.ServiceId, s.Name, b.MasterId,
            $"{master.FirstName} {master.LastName}", b.ClientId, clientName,
            b.GuestPhone ?? b.Client?.PhoneNumber, b.GuestEmail ?? b.Client?.Email,
            b.Date, b.StartTime, b.EndTime, b.Status, b.PaymentStatus, b.Notes, b.CreatedAt);
}
