using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.9, §37.30 — bookings in the cabinet: the list, the card with the journal, payment check (confirm / reject), cancellation by the company,
/// the proof files and the manual booking. Actions carry `expectedVersion`; a stale version or a forbidden transition is a 409 with the CURRENT card.
/// </summary>
[ApiController]
[Route("api/stays/companies/{companyId:guid}/bookings")]
[Authorize]
public class StaysStaffBookingsController(
    AppDbContext db, StaysAccessResolver access, StayDtoMapper mapper, StayBookingTransitionService transitions, StayActorResolver actors,
    StayBookingCreationService creation, StayPaymentProofService proofs, StayBookingEventLog eventLog, IStaysClock clock, IOptions<StaysOptions> options) : ControllerBase
{
    public const string ReasonRequired = "Укажите причину — гость её увидит";
    public const string VersionRequired = "Не указана версия брони — обновите страницу";
    public const string VersionMismatchText = "Бронь уже изменена — проверьте актуальное состояние";

    [HttpGet]
    [EnableRateLimiting("stays-board")]
    public async Task<ActionResult<StaffStayBookingPage>> List(
        Guid companyId, [FromQuery] StayBookingStatus[]? status, [FromQuery] Guid? houseId, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ViewBookings, asNoTracking: true, ct: ct);
        if (!r.Ok) return r.Error!;
        var statuses = status is { Length: > 0 } ? status.Distinct().ToArray() : [StayBookingStatus.AwaitingPaymentCheck];
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var q = db.StayBookings.AsNoTracking().Where(b => b.CompanyId == companyId && statuses.Contains(b.Status));
        if (houseId is { } hid) q = q.Where(b => b.HouseId == hid);
        if (!string.IsNullOrWhiteSpace(from))
        {
            if (!StaysCatalogService.TryDate(from, out var f)) return BadRequest("Неверный формат даты");
            q = q.Where(b => b.CheckInDate >= f);
        }
        if (!string.IsNullOrWhiteSpace(to))
        {
            if (!StaysCatalogService.TryDate(to, out var t)) return BadRequest("Неверный формат даты");
            q = q.Where(b => b.CheckInDate <= t);
        }
        var total = await q.CountAsync(ct);
        var awaitingOnly = statuses.Length == 1 && statuses[0] == StayBookingStatus.AwaitingPaymentCheck;
        var rows = await (awaitingOnly
                // oldest payment proof first: the queue the owner works through
                ? q.OrderBy(b => b.PaymentProofs.Min(p => (DateTime?)p.UploadedAtUtc) ?? b.CreatedAtUtc)
                : q.OrderByDescending(b => b.CheckInDate).ThenByDescending(b => b.CreatedAtUtc))
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(b => new { Booking = b, HouseName = b.House.Name, FirstProof = b.PaymentProofs.Min(p => (DateTime?)p.UploadedAtUtc) }).ToListAsync(ct);

        var now = clock.UtcNow;
        var items = rows.Select(x =>
        {
            var b = x.Booking;
            var display = mapper.DisplayStatusOf(b, now);
            return new StaffStayBookingListItemDto(b.Id, b.HouseId, x.HouseName, b.CheckInDate, b.CheckOutDate, b.Nights, b.GuestName, b.GuestPhone, b.Status, display,
                StaysTexts.StatusText(display), b.TotalRub, b.PrepayRub, b.Status == StayBookingStatus.Held ? b.HoldExpiresAtUtc : null, x.FirstProof, b.IsManual, b.CreatedAtUtc);
        }).ToList();
        return Ok(new StaffStayBookingPage(items, total, page, pageSize));
    }

    [HttpGet("{bookingId:guid}")]
    public async Task<ActionResult<StaffStayBookingCardDto>> Get(Guid companyId, Guid bookingId, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ViewBookings, asNoTracking: true, ct: ct);
        if (!r.Ok) return r.Error!;
        var booking = await db.StayBookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookingId && b.CompanyId == companyId, ct);
        return booking is null ? NotFound() : Ok(await mapper.ToStaffCardAsync(booking, withMessages: true, ct));
    }

    [HttpPost("{bookingId:guid}/confirm-payment")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<StaffStayBookingCardDto>> ConfirmPayment(Guid companyId, Guid bookingId, ExpectedVersionInput input, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ManageBookings, asNoTracking: true, ct: ct);
        if (!r.Ok) return r.Error!;
        if (input.ExpectedVersion is not { } version) return BadRequest(VersionRequired);
        var result = await transitions.ConfirmPaymentAsync(companyId, bookingId, version, await actors.ResolveStaffAsync(User, ct), ct);
        return await RespondAsync(result, ct);
    }

    [HttpPost("{bookingId:guid}/reject-payment")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<StaffStayBookingCardDto>> RejectPayment(Guid companyId, Guid bookingId, ExpectedVersionReasonInput input, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ManageBookings, asNoTracking: true, ct: ct);
        if (!r.Ok) return r.Error!;
        var reason = (input.Reason ?? string.Empty).Trim();
        if (reason.Length is < 1 or > 300) return BadRequest(ReasonRequired);
        if (input.ExpectedVersion is not { } version) return BadRequest(VersionRequired);
        var result = await transitions.RejectPaymentAsync(companyId, bookingId, version, reason, await actors.ResolveStaffAsync(User, ct), ct);
        return await RespondAsync(result, ct);
    }

    [HttpPost("{bookingId:guid}/cancel")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<StaffStayBookingCardDto>> Cancel(Guid companyId, Guid bookingId, ExpectedVersionReasonInput input, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ManageBookings, asNoTracking: true, ct: ct);
        if (!r.Ok) return r.Error!;
        var reason = (input.Reason ?? string.Empty).Trim();
        if (reason.Length is < 1 or > 300) return BadRequest(ReasonRequired);
        if (input.ExpectedVersion is not { } version) return BadRequest(VersionRequired);
        var result = await transitions.CancelByOwnerAsync(companyId, bookingId, version, reason, await actors.ResolveStaffAsync(User, ct), ct);
        return await RespondAsync(result, ct);
    }

    private async Task<ActionResult<StaffStayBookingCardDto>> RespondAsync(TransitionResult result, CancellationToken ct)
    {
        if (result.Outcome == TransitionOutcome.NotFound) return NotFound();
        var card = await mapper.ToStaffCardAsync(result.Booking!, withMessages: true, ct);
        return result.Outcome switch
        {
            TransitionOutcome.Ok => Ok(card),
            TransitionOutcome.VersionMismatch => Conflict(new StayStaffConflictDto("VersionMismatch", VersionMismatchText, card)),
            _ => Conflict(new StayStaffConflictDto("InvalidTransition", $"Действие недоступно в статусе «{StaysTexts.StatusText(card.DisplayStatus)}»", card)),
        };
    }

    [HttpGet("{bookingId:guid}/payment-proofs/{proofId:guid}")]
    public async Task<IActionResult> GetProof(Guid companyId, Guid bookingId, Guid proofId, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ViewBookings, asNoTracking: true, ct: ct);
        if (!r.Ok) return r.Error!;
        var booking = await db.StayBookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookingId && b.CompanyId == companyId, ct);
        if (booking is null) return NotFound();
        var opened = await proofs.OpenAsync(bookingId, proofId, ct);
        if (opened is null) return NotFound();

        await LogViewAsync(booking, proofId, ct);
        StayProofResponse.Apply(Response, opened.Value.ContentType);
        return File(opened.Value.Stream, opened.Value.ContentType);
    }

    /// <summary>"PaymentProofViewed" — at most once per (staff member, file) in <c>ViewedEventMinutes</c>, so a preview does not flood the journal.</summary>
    private async Task LogViewAsync(StayBooking booking, Guid proofId, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var since = clock.UtcNow.AddMinutes(-options.Value.PaymentProofs.ViewedEventMinutes);
        var marker = $"\"proofId\":\"{proofId}\"";
        var recent = await db.StayBookingEvents.AsNoTracking().AnyAsync(e => e.StayBookingId == booking.Id && e.Kind == StayBookingEventKind.PaymentProofViewed &&
            e.ActorUserId == userId && e.OccurredAtUtc >= since && EF.Functions.JsonContains(e.DetailsJson!, $"{{{marker}}}"), ct);
        if (recent) return;
        await eventLog.AppendAsync(booking, StayBookingEventKind.PaymentProofViewed, await actors.ResolveStaffAsync(User, ct), booking.Status, booking.Status, detailsJson: $"{{{marker}}}");
        await db.SaveChangesAsync(ct);
    }

    // ── manual booking (P1) ──

    [HttpPost("quote")]
    public async Task<ActionResult<StayQuoteDto>> ManualQuote(Guid companyId, StaffStayQuoteInput input, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ManageBookings, asNoTracking: true, ct: ct);
        if (!r.Ok) return r.Error!;
        if (input.HouseId is null || input.CheckIn is null || input.CheckOut is null) return BadRequest("Укажите дом и даты заезда и выезда");
        if (input.Adults is < 1 or > 30) return BadRequest("Взрослых — от 1 до 30");
        if (input.Children is < 0 or > 30) return BadRequest("Детей — от 0 до 30");
        if (input.Dogs is < 0 or > 20) return BadRequest("Собак — от 0 до 20");
        var ctx = await creation.FindHouseOfCompanyAsync(companyId, input.HouseId.Value, ct);
        if (ctx is null) return NotFound();
        return Ok(await creation.QuoteAsync(ctx, new StayStayInput(input.CheckIn.Value, input.CheckOut.Value, input.Adults, input.Children, input.Dogs, input.NeedCot), checkGate: false, ct));
    }

    [HttpPost]
    [RequiresOwnerTerms]
    public async Task<ActionResult<StaffStayBookingCardDto>> CreateManual(Guid companyId, ManualStayBookingInput input, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ManageBookings, asNoTracking: true, ct: ct);
        if (!r.Ok) return r.Error!;
        var result = await creation.CreateManualAsync(companyId, input, await actors.ResolveStaffAsync(User, ct), ct);
        if (result.Error is not null) return result.Error;
        return StatusCode(StatusCodes.Status201Created, await mapper.ToStaffCardAsync(result.Booking!, withMessages: true, ct));
    }
}
