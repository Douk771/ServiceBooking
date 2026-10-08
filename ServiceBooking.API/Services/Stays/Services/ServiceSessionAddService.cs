using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

public sealed record SessionAddResult(ActionResult? Error, StayBooking? Booking = null, bool Created = false);

public enum SessionCancelOutcome { Ok, NotFound, NotAllowed, VersionMismatch, InvalidTransition }

public sealed record SessionCancelResult(SessionCancelOutcome Outcome, StayBooking? Booking, string? Message = null);

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.7.2–§39.7.5, API_CONTRACT_CYCLE39.md §39.24 — a session of a service inside a booking of a house: added by the guest from the link, added by the staff
/// (ЮР39-6: with a mandatory basis, the guest is told), cancelled by either. The money of the booking changes by the session's total (a service is paid on site: the prepayment is NOT
/// touched). Lock order (§39.5.2): house → service → booking row → sessions/charges → board revision; a cancellation takes the booking row only.
/// </summary>
public class ServiceSessionAddService(
    AppDbContext db, ServiceSlotService slots, HouseOccupancyWriter occupancy, ServiceSessionWriter sessionWriter, ServiceHoldReleaser holdReleaser, StayBookingEventLog eventLog,
    StaysCompanyService companyService, LegalDocumentProvider legalProvider, StayActorResolver actors, IOptions<StaysOptions> options, IStaysClock clock)
{
    // ── adding ──

    public async Task<SessionAddResult> AddByGuestAsync(string token, AddSessionInput dto, ClaimsPrincipal user, CancellationToken ct)
    {
        var formError = FormError(dto.ServiceId, dto.BusinessDate, dto.StartMinute, dto.Hours, dto.Items, dto.IdempotencyKey, out var selection);
        if (formError is not null) return Bad(formError);
        var booking = await db.StayBookings.AsNoTracking().FirstOrDefaultAsync(b => b.PublicToken == token, ct);
        if (booking is null) return new SessionAddResult(new NotFoundResult());

        var existing = await IdempotentAsync(booking, dto.IdempotencyKey!.Value, ct);
        if (existing is not null) return new SessionAddResult(null, existing);

        var scope = await ServiceOfBookingAsync(booking, dto.ServiceId!.Value, ct);
        if (scope is null) return new SessionAddResult(new NotFoundResult());
        if (!scope.Service.IsPublished || scope.Service.ArchivedAtUtc is not null || !scope.Service.AvailableForHouseBookings)
            return Refuse(ServiceRefusalCode.ServiceNotAvailableForStays, ServiceTexts.NotAvailableForStays);

        var gate = await companyService.EvaluateGateAsync(scope.Company, scope.Settings, 0, ct);
        if (!gate.Accepting) return Refuse(ServiceRefusalCode.NotAcceptingBookings, ServiceTexts.NotAcceptingGuest, gate.ReasonCode);

        var actor = await actors.ResolveGuestAsync(user, booking.GuestName, ct);
        var noticeVersion = legalProvider.Current?.GetText(LegalTextKey.StayServiceAddNotice)?.Version;
        return await AddCoreAsync(booking, scope, selection, dto.IdempotencyKey.Value, dto.ExpectedTotalRub, staff: false, actor, requestBasis: null, noticeVersion, gate, ct);
    }

    public async Task<SessionAddResult> AddByStaffAsync(Guid companyId, Guid bookingId, StaffAddSessionInput dto, StayActor actor, CancellationToken ct)
    {
        var formError = FormError(dto.ServiceId, dto.BusinessDate, dto.StartMinute, dto.Hours, dto.Items, dto.IdempotencyKey, out var selection);
        if (formError is not null) return Bad(formError);
        if (dto.RequestBasis is null || !Enum.IsDefined(dto.RequestBasis.Value)) return Bad(ServiceTexts.BasisRequired);
        var booking = await db.StayBookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookingId && b.CompanyId == companyId, ct);
        if (booking is null) return new SessionAddResult(new NotFoundResult());

        var existing = await IdempotentAsync(booking, dto.IdempotencyKey!.Value, ct);
        if (existing is not null) return new SessionAddResult(null, existing);

        var scope = await ServiceOfBookingAsync(booking, dto.ServiceId!.Value, ct);
        if (scope is null || scope.Service.ArchivedAtUtc is not null) return new SessionAddResult(new NotFoundResult());
        if (!scope.Service.AvailableForHouseBookings) return Refuse(ServiceRefusalCode.ServiceNotAvailableForStays, ServiceTexts.NotAvailableForStays);

        // The tariff gate is not checked for the staff (as for a manual booking); the quote is not compared with an expected total either: the staff's form shows it.
        return await AddCoreAsync(booking, scope, selection, dto.IdempotencyKey.Value, expectedTotalRub: null, staff: true, actor, dto.RequestBasis, noticeVersion: null,
            GateResult.Ok, ct);
    }

    private async Task<SessionAddResult> AddCoreAsync(
        StayBooking snapshot, ServiceScope scope, ServiceSelection selection, Guid idempotencyKey, int? expectedTotalRub, bool staff, StayActor actor,
        StayServiceRequestBasis? requestBasis, string? noticeVersion, GateResult gate, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await occupancy.LockHouseAsync(snapshot.HouseId);
        await sessionWriter.LockServiceAsync(scope.Service.Id);
        await db.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "StayBookings" WHERE "Id" = {snapshot.Id} FOR UPDATE""", ct);
        // A fresh, TRACKED read after the lock: the booking may have been released while we waited.
        var booking = await db.StayBookings.FirstAsync(b => b.Id == snapshot.Id, ct);
        var twin = await IdempotentAsync(booking, idempotencyKey, ct);
        if (twin is not null) return new SessionAddResult(null, twin);

        if (StayStateMachine.IsTerminal(booking.Status) || now >= StayTime.ToUtc(booking.TimeZoneIdSnapshot, booking.CheckOutDate, booking.CheckOutTimeSnapshot))
            return Refuse(ServiceRefusalCode.BookingNotActive, ServiceTexts.BookingNotActive);
        var max = options.Value.Services.MaxSessionsPerBooking;
        if (await db.StayServiceSessions.CountAsync(s => s.StayBookingId == booking.Id && s.State == StayServiceSessionState.Active, ct) >= max)
            return Refuse(ServiceRefusalCode.TooManySessions, ServiceTexts.TooManySessions(max));

        await holdReleaser.ReleaseExpiredAsync(scope, selection.BusinessDate, ct);
        var evaluation = await slots.EvaluateAsync(scope, selection, staff, ServiceSlotService.StayRangeOf(booking), includeExpiredHolds: true, extraOccupied: null, prepayPercent: null, ct);
        if (!evaluation.Ok) return Refuse(evaluation.Problems[0].Code, evaluation.Problems[0].Message);
        var money = evaluation.Money!;
        if (expectedTotalRub is { } expected && money.TotalRub != expected)
        {
            var quote = ServiceQuoteBuilder.Build(scope, evaluation, null, gate, scope.Settings.HoldMinutes);
            return new SessionAddResult(new ConflictObjectResult(new ServiceRefusalDto(ServiceRefusalCode.PriceChanged, ServiceTexts.PriceChanged(money.TotalRub), null, quote)));
        }

        var session = sessionWriter.Add(new NewServiceSession(
            booking.CompanyId, scope.Service, booking.Id, null, evaluation, actor.Kind, actor.UserId, actor.NameSnapshot, requestBasis, noticeVersion,
            idempotencyKey), now);
        var nextPosition = (await db.StayBookingCharges.Where(c => c.StayBookingId == booking.Id).MaxAsync(c => (int?)c.Position, ct) ?? -1) + 1;
        foreach (var charge in ChargesOf(booking.Id, session, evaluation, nextPosition)) db.StayBookingCharges.Add(charge);
        booking.TotalRub += money.TotalRub;
        booking.DueAtCheckInRub += money.TotalRub;
        booking.Version++;
        booking.UpdatedAtUtc = now;
        await eventLog.AppendAsync(booking, StayBookingEventKind.ServiceSessionAdded, actor, booking.Status, booking.Status,
            detailsJson: System.Text.Json.JsonSerializer.Serialize(new { sessionId = session.Id, addedByStaff = staff, requestBasis = requestBasis?.ToString() }),
            serviceSessionId: session.Id);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (ServiceSessionWriter.IsOverlapViolation(ex))
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return Refuse(ServiceRefusalCode.SlotTaken, ServiceTexts.SlotTaken);
        }
        return new SessionAddResult(null, booking, Created: true);
    }

    /// <summary>The charge lines of a session of a booking (§39.6.2): the service itself and every position; never prepay-eligible.</summary>
    public static List<StayBookingCharge> ChargesOf(Guid bookingId, StayServiceSession session, ServiceEvaluation evaluation, int firstPosition)
    {
        var timeLabel = ServiceTimeFormat.Guest(session.BusinessDate, session.StartMinute, session.Hours);
        var lines = new List<StayBookingCharge>
        {
            new()
            {
                Id = Guid.NewGuid(), StayBookingId = bookingId, ServiceSessionId = session.Id, Position = firstPosition, Kind = StayChargeKind.ServiceSlot,
                Label = $"{session.ServiceNameSnapshot} · {timeLabel} · {session.Hours} ч", Quantity = 1, UnitPriceRub = session.ServiceAmountRub, NightsCount = 0,
                AmountRub = session.ServiceAmountRub, PrepayEligible = false,
            }
        };
        var position = firstPosition + 1;
        foreach (var item in evaluation.Items.Where(i => i.Quantity > 0))
            lines.Add(new StayBookingCharge
            {
                Id = Guid.NewGuid(), StayBookingId = bookingId, ServiceSessionId = session.Id, Position = position++, Kind = StayChargeKind.ServiceItem,
                Label = $"{item.Name} × {item.Quantity}", Quantity = item.Quantity, UnitPriceRub = item.UnitPriceRub, NightsCount = 0,
                AmountRub = item.UnitPriceRub * item.Quantity, PrepayEligible = false,
            });
        return lines;
    }

    // ── cancelling ──

    public async Task<SessionCancelResult> CancelByGuestAsync(string token, Guid sessionId, StayActor actor, CancellationToken ct)
    {
        var id = await db.StayBookings.AsNoTracking().Where(b => b.PublicToken == token).Select(b => (Guid?)b.Id).FirstOrDefaultAsync(ct);
        if (id is null) return new SessionCancelResult(SessionCancelOutcome.NotFound, null);
        return await CancelCoreAsync(id.Value, sessionId, byStaff: false, expectedVersion: null, reason: null, actor, ct);
    }

    public async Task<SessionCancelResult> CancelByStaffAsync(Guid companyId, Guid bookingId, Guid sessionId, int expectedVersion, string reason, StayActor actor, CancellationToken ct)
    {
        if (!await db.StayBookings.AsNoTracking().AnyAsync(b => b.Id == bookingId && b.CompanyId == companyId, ct)) return new SessionCancelResult(SessionCancelOutcome.NotFound, null);
        return await CancelCoreAsync(bookingId, sessionId, byStaff: true, expectedVersion, reason, actor, ct);
    }

    private async Task<SessionCancelResult> CancelCoreAsync(
        Guid bookingId, Guid sessionId, bool byStaff, int? expectedVersion, string? reason, StayActor actor, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Cancelling frees time: it takes neither the house nor the service lock (a reader under the lock of the service sees the time as taken at worst).
        await db.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "StayBookings" WHERE "Id" = {bookingId} FOR UPDATE""", ct);
        var booking = await db.StayBookings.FirstAsync(b => b.Id == bookingId, ct);
        var session = await db.StayServiceSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.StayBookingId == bookingId, ct);
        if (session is null) return new SessionCancelResult(SessionCancelOutcome.NotFound, null);

        var company = await db.Companies.AsNoTracking().Where(c => c.Id == booking.CompanyId).Select(c => c.Phone).FirstAsync(ct);
        if (byStaff && session.Version != expectedVersion) return new SessionCancelResult(SessionCancelOutcome.VersionMismatch, Detach(booking), ServiceTexts.VersionMismatch);
        if (session.State != StayServiceSessionState.Active)
            return new SessionCancelResult(byStaff ? SessionCancelOutcome.InvalidTransition : SessionCancelOutcome.NotAllowed, Detach(booking), ServiceTexts.SessionAlreadyCancelled);
        if (StayStateMachine.IsTerminal(booking.Status))
            return new SessionCancelResult(byStaff ? SessionCancelOutcome.InvalidTransition : SessionCancelOutcome.NotAllowed, Detach(booking), ServiceTexts.BookingNotActive);
        if (!byStaff && now >= session.StartUtc)
            return new SessionCancelResult(SessionCancelOutcome.NotAllowed, Detach(booking), ServiceTexts.AlreadyStarted(company));

        sessionWriter.Cancel(session, byStaff ? StayServiceSessionState.CancelledByOwner : StayServiceSessionState.CancelledByGuest, reason, now);
        await db.StayBookingCharges.Where(c => c.ServiceSessionId == session.Id).ExecuteDeleteAsync(ct);
        booking.TotalRub -= session.TotalRub;
        booking.DueAtCheckInRub -= session.TotalRub;
        booking.Version++;
        booking.UpdatedAtUtc = now;
        await eventLog.AppendAsync(booking, byStaff ? StayBookingEventKind.ServiceSessionCancelledByOwner : StayBookingEventKind.ServiceSessionCancelledByGuest, actor,
            booking.Status, booking.Status, reason, System.Text.Json.JsonSerializer.Serialize(new { sessionId = session.Id }), session.Id);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return new SessionCancelResult(byStaff ? SessionCancelOutcome.VersionMismatch : SessionCancelOutcome.NotAllowed,
                await db.StayBookings.AsNoTracking().FirstAsync(b => b.Id == bookingId, ct), ServiceTexts.VersionMismatch);
        }
        await tx.CommitAsync(ct);
        return new SessionCancelResult(SessionCancelOutcome.Ok, booking);
    }

    // ── helpers ──

    private async Task<ServiceScope?> ServiceOfBookingAsync(StayBooking booking, Guid serviceId, CancellationToken ct) =>
        await slots.FindOfCompanyAsync(booking.CompanyId, serviceId, ct);

    private async Task<StayBooking?> IdempotentAsync(StayBooking booking, Guid key, CancellationToken ct)
    {
        var found = await db.StayServiceSessions.AsNoTracking().AnyAsync(s => s.StayBookingId == booking.Id && s.IdempotencyKey == key, ct);
        return found ? await db.StayBookings.AsNoTracking().FirstAsync(b => b.Id == booking.Id, ct) : null;
    }

    private static string? FormError(
        Guid? serviceId, DateOnly? date, int? startMinute, int? hours, IReadOnlyList<ItemSelectionInput>? items, Guid? key, out ServiceSelection selection)
    {
        selection = new ServiceSelection(default, 0, 0, []);
        if (serviceId is null || serviceId == Guid.Empty) return "Выберите услугу";
        var error = ServiceOrderCreationService.ValidateSelection(date, startMinute, hours, items, out selection);
        if (error is not null) return error;
        return key is null || key == Guid.Empty ? "Нужен ключ запроса — обновите страницу" : null;
    }

    private StayBooking Detach(StayBooking booking)
    {
        db.Entry(booking).State = EntityState.Detached;
        return booking;
    }

    private static SessionAddResult Bad(string message) => new(new BadRequestObjectResult(message));

    private static SessionAddResult Refuse(ServiceRefusalCode code, string message, NotAcceptingReason? reason = null) =>
        new(new ConflictObjectResult(new ServiceRefusalDto(code, message, reason?.ToString())));
}
