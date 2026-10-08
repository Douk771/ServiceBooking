using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.7.2, §37.8, API_CONTRACT_CYCLE37.md §37.26 — the booking through the guest's eyes. The 256-bit token in the link IS the access:
/// an unknown token is a 404 with an empty body on every route. The page is polled every 15 s by the frontend.
/// </summary>
[ApiController]
[Route("api/stays/bookings")]
public class StayBookingsPublicController(
    AppDbContext db, StayDtoMapper mapper, StayPaymentProofService proofs, StayBookingTransitionService transitions, StayActorResolver actors,
    StayProofIpLimiter proofIpLimiter, IStaysClock clock) : ControllerBase
{
    private const int MaxTokenLength = 100;

    [HttpGet("public/{token}")]
    [EnableRateLimiting("stay-public")]
    public async Task<ActionResult<PublicStayBookingDto>> Get(string token, CancellationToken ct)
    {
        var booking = await FindAsync(token, ct);
        return booking is null ? NotFound() : Ok(await mapper.ToPublicAsync(booking, ct));
    }

    [HttpPost("public/{token}/payment-proofs")]
    [EnableRateLimiting("stay-proof")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<ActionResult<PublicStayBookingDto>> AttachProof(string token, IFormFile? file, CancellationToken ct)
    {
        if (token.Length > MaxTokenLength) return NotFound();
        if (!proofIpLimiter.TryAcquire(HttpContext.Connection.RemoteIpAddress?.ToString()))
            return StatusCode(StatusCodes.Status429TooManyRequests, StayProofIpLimiter.Text);

        var result = await proofs.AttachAsync(token, file, ct);
        switch (result.Outcome)
        {
            case ProofOutcome.NotFound: return NotFound();
            case ProofOutcome.BadFile: return BadRequest(result.Error);
        }
        var dto = await mapper.ToPublicAsync(result.Booking!, ct);
        var phone = dto.Company.Phone;
        return result.Outcome switch
        {
            ProofOutcome.Ok => StatusCode(StatusCodes.Status201Created, dto),
            ProofOutcome.NotAllowed => Conflict(new StayGuestConflictDto("ProofNotAllowed", $"Бронь уже {StaysTexts.StatusText(dto.DisplayStatus).ToLowerInvariant()} — подтверждение оплаты не нужно", dto)),
            ProofOutcome.LimitReached => Conflict(new StayGuestConflictDto("ProofLimitReached", $"Можно приложить не больше {dto.Proofs.MaxCount} файлов", dto)),
            _ => Conflict(new StayGuestConflictDto("HoldExpired", StaysTexts.HoldExpiredMessage(phone), dto)),
        };
    }

    [HttpGet("public/{token}/payment-proofs/{proofId:guid}")]
    [EnableRateLimiting("stay-public")]
    public async Task<IActionResult> GetProof(string token, Guid proofId, CancellationToken ct)
    {
        var booking = await FindAsync(token, ct);
        if (booking is null) return NotFound();
        return await ProofFileResultAsync(booking.Id, proofId, ct);
    }

    [HttpPost("public/{token}/cancel")]
    [EnableRateLimiting("stay-public")]
    public async Task<ActionResult<PublicStayBookingDto>> Cancel(string token, EmptyInput? _, CancellationToken ct)
    {
        if (token.Length > MaxTokenLength) return NotFound();
        var existing = await FindAsync(token, ct);
        if (existing is null) return NotFound();
        var actor = await actors.ResolveGuestAsync(User, existing.GuestName, ct);
        var result = await transitions.CancelByGuestAsync(token, actor, ct);
        if (result.Outcome == TransitionOutcome.NotFound) return NotFound();
        var dto = await mapper.ToPublicAsync((await FindAsync(token, ct))!, ct);
        if (result.Outcome == TransitionOutcome.Ok) return Ok(dto);
        var message = dto.Cancellation.CannotCancelText ?? StaysTexts.CannotCancelAlready;
        return Conflict(new StayGuestConflictDto("CancelNotAllowed", message, dto));
    }

    /// <summary>"Мои брони" (P1): the signed-in account's own bookings, active first, then the past 12 months. Bookings made as a guest on the same number are NOT pulled in.</summary>
    [HttpGet("my")]
    [Authorize]
    public async Task<ActionResult<List<StayMyBookingDto>>> My(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var now = clock.UtcNow;
        var since = DateOnly.FromDateTime(now.AddMonths(-12));
        var rows = await db.StayBookings.AsNoTracking()
            .Where(b => b.GuestUserId == userId && b.CheckOutDate >= since)
            .Select(b => new { Booking = b, HouseName = b.House.Name, CompanyName = b.Company.Name }).ToListAsync(ct);
        var items = rows.Select(r =>
        {
            var b = r.Booking;
            var display = mapper.DisplayStatusOf(b, now);
            var active = !StayStateMachine.IsTerminal(b.Status) && display != "Completed";
            return (Active: active, Dto: new StayMyBookingDto(mapper.BookingUrl(b), r.HouseName, r.CompanyName, b.CheckInDate, b.CheckOutDate, b.Status, display, StaysTexts.StatusText(display), b.TotalRub), b.CheckInDate);
        }).OrderByDescending(x => x.Active).ThenBy(x => x.Active ? x.CheckInDate.DayNumber : -x.CheckInDate.DayNumber).Select(x => x.Dto).ToList();
        return Ok(items);
    }

    private async Task<StayBooking?> FindAsync(string token, CancellationToken ct) =>
        token.Length > MaxTokenLength ? null : await db.StayBookings.AsNoTracking().FirstOrDefaultAsync(b => b.PublicToken == token, ct);

    private async Task<IActionResult> ProofFileResultAsync(Guid bookingId, Guid proofId, CancellationToken ct)
    {
        var opened = await proofs.OpenAsync(bookingId, proofId, ct);
        if (opened is null) return NotFound();
        StayProofResponse.Apply(Response, opened.Value.ContentType);
        return File(opened.Value.Stream, opened.Value.ContentType);
    }
}

/// <summary>ARCHITECTURE_CYCLE37.md §37.8 — headers of a payment-proof file: never cached, never sniffed, sandboxed; a PDF is only ever downloaded.</summary>
public static class StayProofResponse
{
    public static void Apply(HttpResponse response, string contentType)
    {
        response.Headers.CacheControl = "private, no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Content-Security-Policy"] = "sandbox";
        response.Headers.ContentDisposition = contentType == "application/pdf" ? "attachment; filename=\"payment-proof.pdf\"" : "inline";
    }
}
