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
public class BookingsController(AppDbContext db, SlotService slotService, CaptchaService captchaService, IConfiguration config) : ControllerBase
{
    [HttpGet("slots")]
    public async Task<ActionResult<List<TimeSlotResult>>> GetSlots(
        [FromQuery] string masterId,
        [FromQuery] Guid serviceId,
        [FromQuery] DateOnly date)
    {
        var slots = await slotService.GetAvailableSlotsAsync(masterId, serviceId, date);
        return Ok(slots);
    }

    [HttpPost]
    public async Task<ActionResult<BookingDto>> Create(CreateBookingDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAuthenticated = userId is not null;

        // Guest booking requires captcha
        if (!isAuthenticated)
        {
            var company = await db.Companies.FindAsync(dto.CompanyId);
            if (company is null) return NotFound("Company not found");
            if (!company.AllowSelfBooking) return Forbid();

            // Require captcha token only when a secret key is actually configured (skip in dev)
            var captchaConfigured = !string.IsNullOrEmpty(config["Recaptcha:SecretKey"]);
            if (captchaConfigured && string.IsNullOrEmpty(dto.CaptchaToken))
                return BadRequest("Captcha required for guest booking");

            if (!string.IsNullOrEmpty(dto.CaptchaToken) && !await captchaService.ValidateAsync(dto.CaptchaToken))
                return BadRequest("Invalid captcha");

            if (string.IsNullOrEmpty(dto.GuestName) || string.IsNullOrEmpty(dto.GuestPhone))
                return BadRequest("Name and phone are required for guest booking");
        }

        var service = await db.Services.FindAsync(dto.ServiceId);
        if (service is null) return NotFound("Service not found");

        var slotEnd = dto.StartTime.AddMinutes(service.DurationMinutes);

        // Check slot is available
        var conflict = await db.Bookings.AnyAsync(b =>
            b.MasterId == dto.MasterId &&
            b.Date == dto.Date &&
            b.Status != BookingStatus.Cancelled &&
            b.StartTime < slotEnd && b.EndTime > dto.StartTime);

        if (conflict) return Conflict("Time slot is no longer available");

        AppUser? client = null;
        if (isAuthenticated)
            client = await db.Users.FindAsync(userId);

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            CompanyId = dto.CompanyId,
            ServiceId = dto.ServiceId,
            MasterId = dto.MasterId,
            ClientId = userId,
            GuestName = dto.GuestName,
            GuestPhone = dto.GuestPhone,
            GuestEmail = dto.GuestEmail,
            Date = dto.Date,
            StartTime = dto.StartTime,
            EndTime = slotEnd,
            Notes = dto.Notes,
            Status = BookingStatus.Confirmed
        };

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var master = await db.Users.FindAsync(dto.MasterId);
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
            .FirstOrDefaultAsync(b => b.Id == id);

        if (booking is null) return NotFound();

        var canView = booking.ClientId == userId ||
                      booking.MasterId == userId ||
                      User.IsInRole("SuperAdmin");
        if (!canView)
        {
            var isCompanyAdmin = await db.CompanyMembers.AnyAsync(cm =>
                cm.CompanyId == booking.CompanyId && cm.UserId == userId &&
                (cm.Role == UserRole.CompanyOwner));
            if (!isCompanyAdmin) return Forbid();
        }

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

    [HttpGet("master")]
    [Authorize(Roles = "Master,CompanyOwner")]
    public async Task<ActionResult<List<BookingDto>>> GetMasterBookings([FromQuery] DateOnly? date)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var query = db.Bookings
            .Include(b => b.Service)
            .Include(b => b.Master)
            .Include(b => b.Client)
            .Where(b => b.MasterId == userId);

        if (date.HasValue)
            query = query.Where(b => b.Date == date.Value);

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

    [HttpPatch("{id:guid}/cancel")]
    [Authorize]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] string? reason)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var booking = await db.Bookings.FindAsync(id);
        if (booking is null) return NotFound();

        var canCancel = booking.ClientId == userId || booking.MasterId == userId || User.IsInRole("SuperAdmin");
        if (!canCancel) return Forbid();

        booking.Status = BookingStatus.Cancelled;
        booking.CancellationReason = reason;
        booking.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return NoContent();
    }

    private static BookingDto MapToDto(Booking b, Service s, AppUser master, string clientName) =>
        new(b.Id, b.CompanyId, b.ServiceId, s.Name, b.MasterId,
            $"{master.FirstName} {master.LastName}", b.ClientId, clientName,
            b.GuestPhone ?? b.Client?.PhoneNumber, b.GuestEmail ?? b.Client?.Email,
            b.Date, b.StartTime, b.EndTime, b.Status, b.Notes, b.CreatedAt);
}
