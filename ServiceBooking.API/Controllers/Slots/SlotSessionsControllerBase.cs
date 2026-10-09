using ServiceBooking.API.Controllers.Stays;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;


namespace ServiceBooking.API.Controllers.Slots;

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.7.3, §39.10, API_CONTRACT_CYCLE39.md §39.29, §39.30 — sessions in the cabinet: «День услуг», the list and the card (both kinds), payment check and cancellation
/// with <c>expectedVersion</c>, the proof files, the starts for the staff (no minimum lead; an unpublished service is allowed), adding a service to a booking (with the mandatory basis,
/// ЮР39-6) and the manual stand-alone order. Rights: ViewBookings to read, ManageBookings to act.
/// </summary>

public abstract class SlotSessionsControllerBase(
    AppDbContext db, StaysAccessResolver access, ServiceDayService day, ServiceDtoMapper mapper, StayDtoMapper bookingMapper, ServiceSlotService slots,
    ServiceOrderTransitionService transitions, ServiceSessionAddService sessionAdd, ServiceOrderCreationService orderCreation, ServiceOrderProofService proofs,
    StayServiceOrderEventLog orderLog, StayActorResolver actors, IStaysClock clock, IOptions<StaysOptions> options) : ControllerBase
{
    protected abstract SlotVertical Vertical { get; }

    protected AppDbContext Db => db;
    protected StaysAccessResolver Access => access;
    protected ServiceSessionAddService SessionAdd => sessionAdd;
    protected StayActorResolver Actors => actors;
    protected StayDtoMapper BookingMapper => bookingMapper;

    [HttpGet("service-day")]
    [EnableRateLimiting("stays-board")]
    public async Task<ActionResult<ServiceDayDto>> ServiceDay(Guid companyId, [FromQuery] string? date, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ViewBookings, asNoTracking: true, ct: ct, kind: Vertical.Kind);
        if (!r.Ok) return r.Error!;
        var d = slots.TodayOf(r.Company!);
        if (!string.IsNullOrWhiteSpace(date) && !StaysCatalogService.TryDate(date, out d)) return BadRequest("Неверный формат даты");
        return Ok(await day.BuildAsync(r.Company!, d, ct));
    }

    // ── starts and quote for the staff ──

    [HttpGet("services/{serviceId:guid}/starts")]
    public async Task<ActionResult<ServiceStartsDto>> Starts(Guid companyId, Guid serviceId, [FromQuery] string? date, [FromQuery] Guid? bookingId, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ManageBookings, asNoTracking: true, ct: ct, kind: Vertical.Kind);
        if (!r.Ok) return r.Error!;
        if (!StaysCatalogService.TryDate(date, out var d)) return BadRequest(string.IsNullOrWhiteSpace(date) ? "Выберите дату" : "Неверный формат даты");
        var (scope, stay, error) = await StaffScopeAsync(companyId, serviceId, bookingId, ct);
        if (error is not null) return error;
        var starts = await slots.StartsAsync(scope!, d, staff: true, stay, ct);
        return Ok(starts with { Items = await ItemsAsync(serviceId, ct) });
    }

    /// <summary>The dates with free time for the staff's forms («Добавить услугу», a manual order) — the same calendar as the guest's, without the minimum lead.</summary>
    [HttpGet("services/{serviceId:guid}/availability")]
    public async Task<ActionResult<ServiceAvailabilityDto>> Availability(
        Guid companyId, Guid serviceId, [FromQuery] string? from, [FromQuery] int? days, [FromQuery] Guid? bookingId, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ManageBookings, asNoTracking: true, ct: ct, kind: Vertical.Kind);
        if (!r.Ok) return r.Error!;
        var (scope, stay, error) = await StaffScopeAsync(companyId, serviceId, bookingId, ct);
        if (error is not null) return error;
        var start = slots.TodayOf(scope!.Company);
        if (!string.IsNullOrWhiteSpace(from) && !StaysCatalogService.TryDate(from, out start)) return BadRequest("Неверный период");
        var count = days ?? options.Value.Services.AvailabilityDefaultDays;
        if (count is < 1 or > 31) return BadRequest("Неверный период");
        return Ok(new ServiceAvailabilityDto(serviceId, slots.TodayOf(scope.Company), await slots.AvailabilityAsync(scope, start, count, staff: true, stay, ct)));
    }

    [HttpPost("service-sessions/quote")]
    public async Task<ActionResult<ServiceQuoteDto>> Quote(Guid companyId, StaffServiceQuoteInput input, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ManageBookings, asNoTracking: true, ct: ct, kind: Vertical.Kind);
        if (!r.Ok) return r.Error!;
        if (input.ServiceId is null) return BadRequest("Выберите услугу");
        var formError = ServiceOrderCreationService.ValidateSelection(input.BusinessDate, input.StartMinute, input.Hours, input.Items, out var selection);
        if (formError is not null) return BadRequest(formError);
        var (scope, stay, error) = await StaffScopeAsync(companyId, input.ServiceId.Value, input.BookingId, ct);
        if (error is not null) return error;
        var evaluation = await slots.EvaluateAsync(scope!, selection, staff: true, stay, includeExpiredHolds: false, extraOccupied: null, prepayPercent: null, ct);
        return Ok(ServiceQuoteBuilder.Build(scope!, evaluation, null, GateResult.Ok, scope!.Settings.HoldMinutes));
    }

    // ── list and card ──

    [HttpGet("service-sessions")]
    [EnableRateLimiting("stays-board")]
    public async Task<ActionResult<StaffServiceSessionPage>> List(
        Guid companyId, [FromQuery] StayBookingStatus[]? status, [FromQuery] Guid? serviceId, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ViewBookings, asNoTracking: true, ct: ct, kind: Vertical.Kind);
        if (!r.Ok) return r.Error!;
        DateOnly? fromDate = null, toDate = null;
        if (!string.IsNullOrWhiteSpace(from)) { if (!StaysCatalogService.TryDate(from, out var f)) return BadRequest("Неверный формат даты"); fromDate = f; }
        if (!string.IsNullOrWhiteSpace(to)) { if (!StaysCatalogService.TryDate(to, out var t)) return BadRequest("Неверный формат даты"); toDate = t; }
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var awaitingOnly = status is not { Length: > 0 } && fromDate is null && toDate is null;
        var statuses = status is { Length: > 0 } ? status.Distinct().ToArray() : [StayBookingStatus.AwaitingPaymentCheck];
        var withBookingSessions = status is not { Length: > 0 } && (fromDate is not null || toDate is not null);

        var orderQuery = from s in db.StayServiceSessions.AsNoTracking()
                         join o in db.StayServiceOrders.AsNoTracking() on s.StayServiceOrderId equals o.Id
                         where s.CompanyId == companyId && (withBookingSessions || statuses.Contains(o.Status))
                         select new { Session = s, Order = o };
        if (serviceId is { } sid) orderQuery = orderQuery.Where(x => x.Session.ServiceId == sid);
        if (fromDate is { } fd) orderQuery = orderQuery.Where(x => x.Session.BusinessDate >= fd);
        if (toDate is { } td) orderQuery = orderQuery.Where(x => x.Session.BusinessDate <= td);
        // Sorted and cut IN THE DATABASE: each source gives its first page*pageSize rows in the final order, the merge below slices the page.
        var take = page * pageSize;
        var orderCount = await orderQuery.CountAsync(ct);
        var orderedOrders = awaitingOnly
            ? orderQuery.OrderBy(x => x.Order.PaymentProofs.Min(p => (DateTime?)p.UploadedAtUtc) ?? x.Order.CreatedAtUtc)
            : orderQuery.OrderByDescending(x => x.Session.StartUtc);
        var orderRows = await orderedOrders.Take(take).ToListAsync(ct);
        var pageOrderIds = orderRows.Select(x => x.Order.Id).ToList();
        var firstProofs = await db.StayPaymentProofs.AsNoTracking().Where(p => p.StayServiceOrderId != null && pageOrderIds.Contains(p.StayServiceOrderId.Value))
            .GroupBy(p => p.StayServiceOrderId!.Value).Select(g => new { Id = g.Key, First = g.Min(p => p.UploadedAtUtc) }).ToDictionaryAsync(x => x.Id, x => x.First, ct);

        var now = clock.UtcNow;
        var items = orderRows.Select(x =>
        {
            var display = ServiceDtoMapper.DisplayStatusOf(x.Order, x.Session.EndUtc, now);
            return (Sort: firstProofs.GetValueOrDefault(x.Order.Id, x.Order.CreatedAtUtc), Item: new StaffServiceSessionListItemDto(
                x.Session.Id, ServiceSessionKind.Standalone, x.Session.ServiceId, x.Session.ServiceNameSnapshot, ServiceDtoMapper.TimeOf(x.Session, forStaff: true), null, null,
                x.Order.GuestName, x.Order.GuestPhone, x.Order.Status, x.Session.State, ServiceTexts.StatusText(display), x.Order.TotalRub, x.Order.PrepayRub,
                x.Order.Status == StayBookingStatus.Held ? x.Order.HoldExpiresAtUtc : null, firstProofs.TryGetValue(x.Order.Id, out var fp) ? fp : null, x.Order.CreatedAtUtc), x.Session.StartUtc);
        }).ToList();

        var bookingCount = 0;
        if (withBookingSessions)
        {
            var bq = from s in db.StayServiceSessions.AsNoTracking()
                     join b in db.StayBookings.AsNoTracking() on s.StayBookingId equals b.Id
                     join h in db.Houses.AsNoTracking() on b.HouseId equals h.Id
                     where s.CompanyId == companyId
                     select new { Session = s, Booking = b, HouseName = h.Name };
            if (serviceId is { } sid2) bq = bq.Where(x => x.Session.ServiceId == sid2);
            if (fromDate is { } fd2) bq = bq.Where(x => x.Session.BusinessDate >= fd2);
            if (toDate is { } td2) bq = bq.Where(x => x.Session.BusinessDate <= td2);
            bookingCount = await bq.CountAsync(ct);
            foreach (var x in await bq.OrderByDescending(x => x.Session.StartUtc).Take(take).ToListAsync(ct))
                items.Add((x.Session.CreatedAtUtc, new StaffServiceSessionListItemDto(
                    x.Session.Id, ServiceSessionKind.InBooking, x.Session.ServiceId, x.Session.ServiceNameSnapshot, ServiceDtoMapper.TimeOf(x.Session, forStaff: true), x.HouseName,
                    x.Booking.Id, x.Booking.GuestName, x.Booking.GuestPhone, null, x.Session.State, ServiceTexts.SessionStateText(x.Session.State), x.Session.TotalRub, 0, null, null,
                    x.Session.CreatedAtUtc), x.Session.StartUtc));
        }

        var ordered = awaitingOnly ? items.OrderBy(i => i.Sort).ToList() : items.OrderByDescending(i => i.Item.Time.StartUtc).ToList();
        return Ok(new StaffServiceSessionPage(ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(i => i.Item).ToList(), orderCount + bookingCount, page, pageSize));
    }

    [HttpGet("service-sessions/{sessionId:guid}")]
    public async Task<ActionResult<StaffServiceSessionCardDto>> Card(Guid companyId, Guid sessionId, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ViewBookings, asNoTracking: true, ct: ct, kind: Vertical.Kind);
        if (!r.Ok) return r.Error!;
        var session = await db.StayServiceSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && s.CompanyId == companyId, ct);
        return session is null ? NotFound() : Ok(await mapper.ToStaffCardAsync(session, ct));
    }

    // ── actions ──

    [HttpPost("service-sessions/{sessionId:guid}/confirm-payment")]
    [RequiresOwnerTerms]
    public Task<ActionResult<StaffServiceSessionCardDto>> ConfirmPayment(Guid companyId, Guid sessionId, ExpectedVersionInput input, CancellationToken ct) =>
        ActAsync(companyId, sessionId, StayAction.ConfirmPayment, input.ExpectedVersion, null, ct);

    [HttpPost("service-sessions/{sessionId:guid}/reject-payment")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<StaffServiceSessionCardDto>> RejectPayment(Guid companyId, Guid sessionId, ExpectedVersionReasonInput input, CancellationToken ct)
    {
        var reason = (input.Reason ?? string.Empty).Trim();
        if (reason.Length is < 1 or > 300) return BadRequest(ServiceTexts.ReasonRequired);
        return await ActAsync(companyId, sessionId, StayAction.RejectPayment, input.ExpectedVersion, reason, ct);
    }

    [HttpPost("service-sessions/{sessionId:guid}/cancel")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<StaffServiceSessionCardDto>> Cancel(Guid companyId, Guid sessionId, ExpectedVersionReasonInput input, CancellationToken ct)
    {
        var reason = (input.Reason ?? string.Empty).Trim();
        if (reason.Length is < 1 or > 300) return BadRequest(ServiceTexts.ReasonRequired);
        return await ActAsync(companyId, sessionId, StayAction.CancelByOwner, input.ExpectedVersion, reason, ct);
    }

    private async Task<ActionResult<StaffServiceSessionCardDto>> ActAsync(Guid companyId, Guid sessionId, StayAction action, int? expectedVersion, string? reason, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ManageBookings, asNoTracking: true, ct: ct, kind: Vertical.Kind);
        if (!r.Ok) return r.Error!;
        if (expectedVersion is not { } version) return BadRequest("Не указана версия сеанса — обновите страницу");
        var session = await db.StayServiceSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && s.CompanyId == companyId, ct);
        if (session is null) return NotFound();
        var actor = await actors.ResolveStaffAsync(User, ct);

        if (session.StayServiceOrderId is { } orderId)
        {
            var result = action switch
            {
                StayAction.ConfirmPayment => await transitions.ConfirmPaymentAsync(companyId, orderId, version, actor, ct),
                StayAction.RejectPayment => await transitions.RejectPaymentAsync(companyId, orderId, version, reason!, actor, ct),
                _ => await transitions.CancelByOwnerAsync(companyId, orderId, version, reason!, actor, ct),
            };
            return await RespondAsync(result.Outcome, sessionId, ct);
        }

        // A session of a booking: only the cancellation applies.
        if (action != StayAction.CancelByOwner) return await RespondAsync(TransitionOutcome.InvalidTransition, sessionId, ct);
        var cancel = await sessionAdd.CancelByStaffAsync(companyId, session.StayBookingId!.Value, sessionId, version, reason!, actor, ct);
        var outcome = cancel.Outcome switch
        {
            SessionCancelOutcome.Ok => TransitionOutcome.Ok,
            SessionCancelOutcome.NotFound => TransitionOutcome.NotFound,
            SessionCancelOutcome.VersionMismatch => TransitionOutcome.VersionMismatch,
            _ => TransitionOutcome.InvalidTransition,
        };
        return await RespondAsync(outcome, sessionId, ct);
    }

    private async Task<ActionResult<StaffServiceSessionCardDto>> RespondAsync(TransitionOutcome outcome, Guid sessionId, CancellationToken ct)
    {
        if (outcome == TransitionOutcome.NotFound) return NotFound();
        var card = await mapper.ToStaffCardAsync(await db.StayServiceSessions.AsNoTracking().FirstAsync(s => s.Id == sessionId, ct), ct);
        return outcome switch
        {
            TransitionOutcome.Ok => Ok(card),
            TransitionOutcome.VersionMismatch => Conflict(new ServiceStaffConflictDto("VersionMismatch", ServiceTexts.VersionMismatch, card)),
            _ => Conflict(new ServiceStaffConflictDto("InvalidTransition", $"Действие недоступно в статусе «{card.StatusText}»", card)),
        };
    }

    // ── the proof file ──

    [HttpGet("service-sessions/{sessionId:guid}/payment-proofs/{proofId:guid}")]
    public async Task<IActionResult> GetProof(Guid companyId, Guid sessionId, Guid proofId, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ViewBookings, asNoTracking: true, ct: ct, kind: Vertical.Kind);
        if (!r.Ok) return r.Error!;
        var session = await db.StayServiceSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && s.CompanyId == companyId, ct);
        if (session?.StayServiceOrderId is not { } orderId) return NotFound();
        var order = await db.StayServiceOrders.AsNoTracking().FirstAsync(o => o.Id == orderId, ct);
        var opened = await proofs.OpenAsync(orderId, proofId, ct);
        if (opened is null) return NotFound();

        await LogViewAsync(order, proofId, ct);
        StayProofResponse.Apply(Response, opened.Value.ContentType);
        return File(opened.Value.Stream, opened.Value.ContentType);
    }

    /// <summary>«PaymentProofViewed» — at most once per (staff member, file) in <c>ViewedEventMinutes</c>.</summary>
    private async Task LogViewAsync(StayServiceOrder order, Guid proofId, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var since = clock.UtcNow.AddMinutes(-options.Value.PaymentProofs.ViewedEventMinutes);
        var marker = $"\"proofId\":\"{proofId}\"";
        var recent = await db.StayServiceOrderEvents.AsNoTracking().AnyAsync(e => e.StayServiceOrderId == order.Id && e.Kind == StayServiceOrderEventKind.PaymentProofViewed &&
            e.ActorUserId == userId && e.OccurredAtUtc >= since && EF.Functions.JsonContains(e.DetailsJson!, $"{{{marker}}}"), ct);
        if (recent) return;
        await orderLog.AppendAsync(order, StayServiceOrderEventKind.PaymentProofViewed, await actors.ResolveStaffAsync(User, ct), order.Status, order.Status, detailsJson: $"{{{marker}}}");
        await db.SaveChangesAsync(ct);
    }

    // ── adding ──

    [HttpPost("service-sessions")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<StaffServiceSessionCardDto>> CreateManual(Guid companyId, ManualServiceOrderInput input, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ManageBookings, asNoTracking: true, ct: ct, kind: Vertical.Kind);
        if (!r.Ok) return r.Error!;
        var result = await orderCreation.CreateManualAsync(Vertical.Kind, companyId, input, await actors.ResolveStaffAsync(User, ct), ct);
        if (result.Error is not null) return result.Error;
        var session = await db.StayServiceSessions.AsNoTracking().FirstAsync(s => s.StayServiceOrderId == result.Order!.Id, ct);
        var card = await mapper.ToStaffCardAsync(session, ct);
        return result.Created ? StatusCode(StatusCodes.Status201Created, card) : Ok(card);
    }

    // ── helpers ──

    private async Task<(ServiceScope? Scope, StayRangeSpec? Stay, ActionResult? Error)> StaffScopeAsync(Guid companyId, Guid serviceId, Guid? bookingId, CancellationToken ct)
    {
        var scope = await slots.FindOfCompanyAsync(Vertical.Kind, companyId, serviceId, ct);
        if (scope is null || scope.Service.ArchivedAtUtc is not null) return (null, null, NotFound());
        StayRangeSpec? stay = null;
        if (bookingId is { } bid)
        {
            var booking = await db.StayBookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bid && b.CompanyId == companyId, ct);
            if (booking is null) return (null, null, NotFound());
            stay = ServiceSlotService.StayRangeOf(booking);
        }
        return (scope, stay, null);
    }

    private async Task<List<ServiceItemPublicDto>> ItemsAsync(Guid serviceId, CancellationToken ct) =>
        await db.StayServiceItems.AsNoTracking().Where(i => i.ServiceId == serviceId && i.IsActive).OrderBy(i => i.Position)
            .Select(i => new ServiceItemPublicDto(i.Id, i.Name, i.PriceRub, i.MaxPerSession)).ToListAsync(ct);
}
