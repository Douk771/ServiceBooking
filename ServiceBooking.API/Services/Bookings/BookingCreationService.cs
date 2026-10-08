using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using static ServiceBooking.API.Controllers.BookingEndpointHelpers;

namespace ServiceBooking.API.Services.Bookings;

/// <summary>Outcome of <see cref="BookingCreationService.CreateAsync"/>: either the exact error result the
/// action always returned (<see cref="Error"/>, returned by the controller as is), or the created booking's
/// id and DTO, which the controller wraps in the same <c>CreatedAtAction(GetById)</c>.</summary>
public sealed record BookingCreationResult(ActionResult? Error, Guid BookingId = default, BookingDto? Dto = null);

/// <summary>
/// Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): the body of <c>POST /api/bookings</c>, moved verbatim out of
/// <c>BookingsController.Create</c> — same gates in the same order, same transaction and advisory lock
/// (<c>booking-slot:{master}:{date}</c>), same journal/notification calls and log lines (the logger keeps the
/// <c>BookingsController</c> category). Every early return is the same MVC result the controller used to
/// build (<c>BadRequest(x)</c> ≡ <c>new BadRequestObjectResult(x)</c>, <c>StatusCode(n, x)</c> ≡
/// <c>new ObjectResult(x) { StatusCode = n }</c>, …). Also home of the shared service-selection validation
/// (cycle 22 D3) that <see cref="BookingAvailabilityController"/>'s slot endpoints use.
/// </summary>
public sealed class BookingCreationService(
    AppDbContext db, CaptchaService captchaService,
    SubscriptionResolver subscriptionResolver, LegalDocumentProvider legalProvider,
    NotificationScheduler notificationScheduler, StaffPushScheduler staffPushScheduler,
    BookingEventLog eventLog, BookingActorResolver actorResolver,
    ILogger<BookingsController> logger)
{
    private static BookingCreationResult Fail(ActionResult error) => new(error);

    /// <param name="dto">The request body, as bound by the action.</param>
    /// <param name="user">The caller (<c>ControllerBase.User</c>).</param>
    /// <param name="remoteIp">The caller's address as the captcha check always read it
    /// (<c>HttpContext.Connection.RemoteIpAddress?.ToString()</c>).</param>
    /// <param name="requestAborted"><c>HttpContext.RequestAborted</c>.</param>
    public async Task<BookingCreationResult> CreateAsync(
        CreateBookingDto dto, ClaimsPrincipal user, string? remoteIp, CancellationToken requestAborted)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAuthenticated = userId is not null;

        // Company must exist before anything else — including for an authenticated caller. Previously
        // this check only ran on the guest branch, so a logged-in caller with a bad CompanyId fell
        // through to a 500 from a broken FK instead of a clean 404 (US-05).
        var company = await db.Companies.FindAsync(dto.CompanyId);
        if (company is null) return Fail(new NotFoundObjectResult("Company not found"));
        // ARCHITECTURE_CYCLE23.md §389.2: a shop takes no bookings — refused right after the company is loaded.
        if (Companies.CompanyKindGuard.RejectNonSalon(company.Kind) is { } shopRefusal) return Fail(shopRefusal);

        // A booking is a "staff manual booking" when an authenticated caller who actually works in THIS
        // company supplies guest details to record a walk-in for a third party. Everything else is an
        // online self-booking: a guest booking for themselves, or an authenticated client booking for
        // themselves.
        var isManualBooking = !string.IsNullOrEmpty(dto.GuestName);
        var isStaff = isAuthenticated &&
            (user.IsInRole("SuperAdmin") || await CompanyMembership.IsStaffAsync(db, dto.CompanyId, userId!));
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
        if (!isStaff && !company.AllowSelfBooking) return Fail(new ForbidResult());

        // ARCHITECTURE_CYCLE28.md §577.4, API_CONTRACT_CYCLE28.md §592: a showcase company that is not open for booking refuses the confirmation for
        // everyone but its staff (SuperAdmin included) — one place for guest, client and /embed. JSON, unlike the other 409s of this route.
        if (company.IsShowcase && !company.ShowcaseBookingOpen && !isStaff)
            return Fail(new ObjectResult(ShowcaseTexts.BookingClosedRefusal()) { StatusCode = StatusCodes.Status409Conflict });

        if (isGuestPath)
        {
            // Guest booking is bot-protected by Yandex SmartCaptcha. When enforced (server key set, or
            // Production) a valid token is mandatory and validation fails closed; in Development without
            // a key it's skipped. See CaptchaService.
            if (captchaService.IsEnforced)
            {
                if (string.IsNullOrEmpty(dto.CaptchaToken))
                    return Fail(new BadRequestObjectResult("Captcha required for guest booking"));

                var ip = remoteIp;
                if (!await captchaService.ValidateAsync(dto.CaptchaToken, ip))
                    return Fail(new BadRequestObjectResult("Invalid captcha"));
            }

            if (string.IsNullOrEmpty(dto.GuestName) || string.IsNullOrEmpty(dto.GuestPhone))
                return Fail(new BadRequestObjectResult("Name and phone are required for guest booking"));
        }

        // US-26: canonical form is what gets stored, for guest bookings same as everywhere else —
        // otherwise the same walk-in phoned in as "8 999..." and booked online as "+7 999..." would
        // show up as two different people in MastersController.GetClients.
        var guestPhone = dto.GuestPhone;
        if (!string.IsNullOrEmpty(guestPhone))
        {
            // US-61/Q4 (§48.2 p.4): a new guest phone (self-booking or staff recording a walk-in) only
            // accepts the Russian format.
            if (!PhoneNormalizer.TryNormalizeRussian(guestPhone, out var canonicalGuestPhone))
                return Fail(new BadRequestObjectResult("Введите номер телефона в формате +7 (900) 000-00-00"));
            guestPhone = canonicalGuestPhone;
        }

        // Plan: Free permits ONLY staff manual bookings. Any online self-booking — a guest booking for
        // themselves, or an authenticated client booking for themselves — requires the company's owner
        // account to be on a plan whose tariff config allows online booking (the resolver already
        // treats an inactive/expired subscription as Free).
        var effectivePlan = await subscriptionResolver.GetEffectivePlanAsync(dto.CompanyId);
        if (!effectivePlan.AllowOnlineBooking && !isStaffManualBooking)
            return Fail(new ObjectResult("Online booking requires a paid subscription.") { StatusCode = 402 });

        // US-67 (ARCHITECTURE_CYCLE6.md §47.1): serviceIds is the multi-service form of serviceId —
        // 1..5, no duplicates, serviceId must equal serviceIds[0] when both are sent. Plus existence,
        // company, active, the master's membership and capability — see ResolveServicesAsync.
        var (orderedServiceIds, services, servicesError) = await ResolveServicesAsync(
            dto.CompanyId, dto.MasterId, dto.ServiceId, dto.ServiceIds, checkMasterIsStaff: true);
        if (servicesError is not null) return Fail(servicesError);

        var servicesById = services.ToDictionary(s => s.Id);
        var (totalDurationMinutes, totalPrice, orderedServices) =
            BookingServiceSelection.Aggregate(orderedServiceIds, servicesById);
        // service.Price/service.DurationMinutes below now mean "the visit total" — kept as one variable
        // so the rest of this method (mostly written before US-67) reads exactly like it always did.
        var service = orderedServices[0];

        var slotEnd = dto.StartTime.AddMinutes(totalDurationMinutes);

        // Not in the past, and doesn't wrap past midnight (TimeOnly can't represent 24:00, so an
        // overflowing slot would otherwise silently give EndTime < StartTime). Applies to every path,
        // including staff manual bookings — Q7's relaxation is about working hours, not about the past.
        if (!IsBookableMoment(dto.Date, dto.StartTime, totalDurationMinutes))
            return Fail(new ConflictObjectResult("Time slot is no longer available"));

        // US-65/Q5 (ARCHITECTURE_CYCLE6.md §45.7 p.2/p.3): the client-only path is bounded by the
        // company's booking horizon; staff (manual bookings) are never subject to this — a salon must
        // always be able to book a regular ahead of the public window. 400, not 409: this is unrelated
        // to slot occupancy (§45.7 p.3).
        if (!isStaffManualBooking)
        {
            var horizonDays = BookingHorizon.Normalize(company!.BookingHorizonDays);
            var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
            if (!BookingHorizon.IsWithin(dto.Date, todayUtc, horizonDays))
                return Fail(new BadRequestObjectResult($"Записаться можно не дальше чем на {horizonDays} дней вперёд"));
        }

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

        // US-37 p.3, ARCHITECTURE.md §5.2/§6.2: the consent snapshot is filled by the SERVER, from the
        // legal documents in effect right now, and ONLY on the guest path — never from the request body
        // (CreateBookingDto gets no new fields for this), and never on a staff manual booking or an
        // authenticated client's own booking (their consent already lives in the ConsentRecord journal,
        // ARCHITECTURE_CYCLE5.md §44.2). If the
        // manifest happens to be unavailable (only possible outside Production), the booking still goes
        // through — a guest's ability to book must not depend on the legal text provider being up —
        // just without a consent snapshot on this one booking.
        string? consentPrivacyVersion = null, consentTermsVersion = null;
        DateTime? consentAcceptedAtUtc = null;
        if (isGuestPath)
        {
            var legalSnapshot = legalProvider.Current;
            var privacyDoc = legalSnapshot?.Get(LegalDocumentType.Privacy);
            var termsDoc = legalSnapshot?.Get(LegalDocumentType.TermsClient);
            if (privacyDoc is not null && termsDoc is not null)
            {
                consentPrivacyVersion = privacyDoc.Version;
                consentTermsVersion = termsDoc.Version;
                consentAcceptedAtUtc = DateTime.UtcNow;
            }
        }

        // ARCHITECTURE_CYCLE5.md §44.5, API_CONTRACT_CYCLE5.md §46.1 (BREAKING № 5). BookingNoticeVersion
        // is filled unconditionally — the ст. 18 notice (D5) is shown on the booking form regardless of
        // who's filling it out, unlike the guest-only consent snapshot above. A manifest that isn't
        // loaded (only possible outside Production) simply leaves it null, same "booking must not depend
        // on the legal text provider being up" rule as the consent snapshot.
        var bookingNoticeVersion = legalProvider.Current?.GetText(LegalTextKey.BookingNotice)?.Version;

        // US-78 п. 1: applies only to the self-booking paths (client, guest, /embed — all the same
        // endpoint) — a staff manual booking is the staff member's own tool, recording who's actually in
        // front of them; there is no "someone else" to confirm authority over.
        DateTime? guardianConfirmedAtUtc = null;
        string? guardianConfirmationVersion = null;
        if (!isStaffManualBooking && dto.BookedForOther)
        {
            if (dto.GuardianConfirmation is not { Confirmed: true })
                return Fail(new BadRequestObjectResult("Для записи другого человека нужно подтвердить полномочия."));

            var guardianText = legalProvider.Current?.GetText(LegalTextKey.GuardianConfirmation);
            if (guardianText is null)
                return Fail(new ObjectResult("Правовые документы временно недоступны.") { StatusCode = StatusCodes.Status503ServiceUnavailable });
            if (dto.GuardianConfirmation.TextVersion != guardianText.Version)
                return Fail(new ConflictObjectResult("Текст подтверждения был обновлён — перечитайте и подтвердите заново."));

            guardianConfirmedAtUtc = DateTime.UtcNow;
            guardianConfirmationVersion = guardianText.Version;
        }

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
            ConsentPrivacyVersion = consentPrivacyVersion,
            ConsentTermsVersion = consentTermsVersion,
            ConsentAcceptedAtUtc = consentAcceptedAtUtc,
            BookingNoticeVersion = bookingNoticeVersion,
            BookedForOther = !isStaffManualBooking && dto.BookedForOther,
            GuardianConfirmedAtUtc = guardianConfirmedAtUtc,
            GuardianConfirmationVersion = guardianConfirmationVersion,
            Status = BookingStatus.Confirmed,
            PaymentStatus = requiresPrepayment ? PaymentStatus.Pending : PaymentStatus.NotRequired,
            Price = totalPrice,
            CommissionPercent = masterCommissionPercent,
            // §574.1 invariant 3: a booking in a showcase company is marked; it was made through the API, so by a visitor (§577.4).
            ShowcaseKind = company.IsShowcase ? ShowcaseBookingKind.Visitor : ShowcaseBookingKind.None
        };

        // US-67 (ARCHITECTURE_CYCLE6.md §44.2 p.2/p.3): one row per selected service, in request order,
        // snapshot at booking time — Σ prices/durations here must equal Price/EndTime-StartTime above.
        var bookingServices = orderedServices.Select((s, position) => new BookingService
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            ServiceId = s.Id,
            Position = position,
            NameSnapshot = s.Name,
            DurationMinutes = s.DurationMinutes,
            Price = s.Price,
        }).ToList();

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

            slotOk = SlotCalculator.IsSlotAllowed(dto.StartTime, totalDurationMinutes,
                workingHours?.StartTime, workingHours?.EndTime, breaks, existingBookings, ScheduleFallback.None);
        }

        if (!slotOk) return Fail(new ConflictObjectResult("Time slot is no longer available"));

        // Invariant check (ARCHITECTURE_CYCLE6.md §44.2 p.2): Σ BookingService rows must equal the
        // booking's own totals before anything is persisted — a mismatch here is a bug in the
        // aggregation above, not a user-facing 400.
        if (bookingServices.Sum(bs => bs.Price) != booking.Price ||
            bookingServices.Sum(bs => bs.DurationMinutes) != (booking.EndTime - booking.StartTime).TotalMinutes)
            throw new InvalidOperationException("BookingService totals do not match the booking's Price/duration.");

        db.Bookings.Add(booking);
        db.BookingServices.AddRange(bookingServices);

        // ARCHITECTURE_CYCLE10.md §105: one of six BookingEventLog.Append call sites, inside this same
        // transaction and BEFORE SaveChangesAsync — deliberately not wrapped in try/catch (unlike the
        // notification scheduler below): a failure to record the journal row must fail the booking too.
        var createActor = await actorResolver.ResolveAsync(user, booking);
        eventLog.Append(booking, BookingEventKind.Created,
            createActor.Kind, createActor.UserId, createActor.NameSnapshot, createActor.RoleSnapshot);

        // ARCHITECTURE_CYCLE4.md §25.3: queued in the SAME transaction as the booking itself, after all
        // eight existing gates above (none of which are touched) and before SaveChangesAsync — the
        // scheduler only tracks changes on this same AppDbContext, it never calls SaveChangesAsync
        // itself. A failure here must not fail the booking (US-28 p.4: the booking is more important than
        // the notification), so it's caught and logged, never rethrown.
        try
        {
            // §375 F17: the plan resolved above is handed over — the scheduler doesn't resolve it again.
            await notificationScheduler.OnBookingCreatedAsync(
                booking, orderedServices.Select(s => s.Name).ToList(), requestAborted, effectivePlan);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to queue notifications for booking {BookingId}", booking.Id);
        }

        // ARCHITECTURE_CYCLE9.md §105.6 (US-116) — same transaction, same "must never fail the booking"
        // isolation as the WhatsApp/MAX scheduler call directly above. A separate try/catch (not folded
        // into the one above) so a Web Push queueing bug can never suppress the WhatsApp/MAX event, or
        // vice versa — each subsystem's own failure stays its own.
        try
        {
            await staffPushScheduler.OnBookingCreatedAsync(
                booking, orderedServices.Select(s => s.Name).ToList(),
                user.FindFirstValue(ClaimTypes.NameIdentifier), requestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to queue staff push notifications for booking {BookingId}", booking.Id);
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        var master = await db.Users.FindAsync(dto.MasterId);
        booking.Company = company;
        booking.BookingServices = bookingServices;
        var clientName = client is not null
            ? $"{client.FirstName} {client.LastName}"
            : dto.GuestName ?? "Guest";

        return new BookingCreationResult(null, booking.Id,
            MapToDto(booking, service, master!, clientName, reminderStatus: null, historyEventCount: null));
    }

    /// <summary>
    /// Cycle 22 D3 — the one validation of a visit's service selection, shared by Create and
    /// GetSlots/GetAvailability (ARCHITECTURE_CYCLE6.md §47.1), in this order: the selection's shape
    /// (1..5, no duplicates, serviceId == serviceIds[0]) → every service exists (404) → belongs to the
    /// company → is active → [Create only: the master is staff of the company — its own message] → the
    /// master performs every selected service. The slot endpoints check the master's membership
    /// themselves, before this, with their own message — hence <paramref name="checkMasterIsStaff"/>.
    /// </summary>
    public async Task<(List<Guid> OrderedIds, List<Service> Services, ActionResult? Error)> ResolveServicesAsync(
        Guid companyId, string masterId, Guid? serviceId, List<Guid>? serviceIds, bool checkMasterIsStaff)
    {
        var validation = BookingServiceSelection.Validate(serviceId, serviceIds);
        if (!validation.IsValid) return ([], [], new BadRequestObjectResult(validation.Message));

        var orderedIds = BookingServiceSelection.Resolve(serviceId, serviceIds);
        var services = await db.Services.Where(s => orderedIds.Contains(s.Id)).ToListAsync();
        if (services.Count != orderedIds.Distinct().Count()) return ([], [], new NotFoundObjectResult("Service not found"));
        // Objects exist but their combination doesn't make sense — 400, not 404 (ARCHITECTURE.md §14.4).
        if (services.Any(s => s.CompanyId != companyId))
            return ([], [], new BadRequestObjectResult("Услуга не относится к выбранной компании"));
        if (services.Any(s => !s.IsActive))
            return ([], [], new BadRequestObjectResult("Услуга сейчас недоступна"));
        if (checkMasterIsStaff && !await CompanyMembership.IsStaffAsync(db, companyId, masterId))
            return ([], [], new BadRequestObjectResult("Master is not a staff member of this company"));

        // The master must be able to perform EVERY selected service, not just the first one
        // (ARCHITECTURE_CYCLE6.md §47.1 p.2) — a client adding a service the chosen master doesn't do
        // gets told so directly, rather than a silently empty slot list.
        var unsupported = await MasterCapability.FindUnsupportedServicesAsync(db, masterId, orderedIds);
        if (unsupported.Count > 0)
        {
            var names = services.Where(s => unsupported.Contains(s.Id)).Select(s => s.Name);
            return ([], [], new BadRequestObjectResult($"Мастер не оказывает услугу: {string.Join(", ", names)}"));
        }

        return (orderedIds, services, null);
    }
}
