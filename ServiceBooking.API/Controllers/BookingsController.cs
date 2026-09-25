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

[ApiController]
[Route("api/[controller]")]
public class BookingsController(
    AppDbContext db, SlotService slotService, AvailabilityService availabilityService, CaptchaService captchaService,
    SubscriptionResolver subscriptionResolver, LegalDocumentProvider legalProvider,
    NotificationScheduler notificationScheduler, ServiceBooking.API.Services.Notifications.StaffPushScheduler staffPushScheduler,
    BookingEventLog eventLog, BookingActorResolver actorResolver,
    ILogger<BookingsController> logger) : ControllerBase
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
        [FromQuery] Guid? serviceId,
        [FromQuery] DateOnly date,
        [FromQuery] bool manual = false,
        [FromQuery] bool extendedHours = false,
        [FromQuery] List<Guid>? serviceIds = null,
        [FromQuery] Guid? excludeBookingId = null)
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
            var booking = await db.Bookings.Include(b => b.BookingServices).Include(b => b.Service)
                .FirstOrDefaultAsync(b => b.Id == excludeBookingId);
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
            totalDuration = booking.BookingServices is { Count: > 0 }
                ? booking.BookingServices.Sum(bs => bs.DurationMinutes)
                : booking.Service.DurationMinutes;
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
        var fallback = manual && isStaff
            ? (extendedHours ? ScheduleFallback.WholeDay : ScheduleFallback.DefaultWindow)
            : ScheduleFallback.None;
        var slots = await slotService.GetAvailableSlotsAsync(companyId, masterId, totalDuration, date, fallback, excludeBookingId);
        return Ok(slots);
    }

    /// <summary>
    /// Shared by GetSlots/GetAvailability: resolves the effective service list (single serviceId, or
    /// serviceIds when supplied), validates the same three things POST /api/bookings validates
    /// (existence/company/active, master capability — ARCHITECTURE_CYCLE6.md §47.1), and returns the
    /// summed duration. Returns a non-null ActionResult when validation fails, which callers must
    /// return directly.
    /// </summary>
    private async Task<(int? TotalDuration, ActionResult? Error)> ResolveTotalDurationAsync(
        Guid companyId, string masterId, Guid? serviceId, List<Guid>? serviceIds)
    {
        var validation = BookingServiceSelection.Validate(serviceId, serviceIds);
        if (!validation.IsValid) return (null, BadRequest(validation.Message));

        var orderedIds = BookingServiceSelection.Resolve(serviceId, serviceIds);
        var services = await db.Services.Where(s => orderedIds.Contains(s.Id)).ToListAsync();
        if (services.Count != orderedIds.Distinct().Count()) return (null, NotFound("Service not found"));
        if (services.Any(s => s.CompanyId != companyId))
            return (null, BadRequest("Услуга не относится к выбранной компании"));
        if (services.Any(s => !s.IsActive))
            return (null, BadRequest("Услуга сейчас недоступна"));

        var unsupported = await MasterCapability.FindUnsupportedServicesAsync(db, masterId, orderedIds);
        if (unsupported.Count > 0)
        {
            var names = services.Where(s => unsupported.Contains(s.Id)).Select(s => s.Name);
            return (null, BadRequest($"Мастер не оказывает услугу: {string.Join(", ", names)}"));
        }

        var servicesById = services.ToDictionary(s => s.Id);
        var (totalDuration, _, _) = BookingServiceSelection.Aggregate(orderedIds, servicesById);
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
        [FromQuery] List<Guid>? serviceIds = null)
    {
        if (to < from) return BadRequest("to must not be before from");
        if (to.DayNumber - from.DayNumber > 30) return BadRequest("Диапазон не может превышать 31 день");

        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        if (from < todayUtc.AddDays(-1)) return BadRequest("from is too far in the past");

        var company = await db.Companies.FindAsync(companyId);
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
        var fallback = honorManual
            ? (extendedHours ? ScheduleFallback.WholeDay : ScheduleFallback.DefaultWindow)
            : ScheduleFallback.None;
        var (defaultStart, defaultEnd) = slotService.GetDefaultWindow();
        var days = await availabilityService.GetAvailabilityAsync(
            companyId, masterId, totalDuration!.Value, from, to, fallback, defaultStart, defaultEnd, honorManual);

        return Ok(new AvailabilityDto(from, to, totalDuration.Value, SlotCalculator.StepMinutes,
            horizonDays, horizonLastDate, days, honorManual));
    }

    [HttpPost]
    [EnableRateLimiting("booking-create")]
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
            // US-61/Q4 (§48.2 p.4): a new guest phone (self-booking or staff recording a walk-in) only
            // accepts the Russian format.
            if (!PhoneNormalizer.TryNormalizeRussian(guestPhone, out var canonicalGuestPhone))
                return BadRequest("Введите номер телефона в формате +7 (900) 000-00-00");
            guestPhone = canonicalGuestPhone;
        }

        // Plan: Free permits ONLY staff manual bookings. Any online self-booking — a guest booking for
        // themselves, or an authenticated client booking for themselves — requires the company's owner
        // account to be on a plan whose tariff config allows online booking (the resolver already
        // treats an inactive/expired subscription as Free).
        var effectivePlan = await subscriptionResolver.GetEffectivePlanAsync(dto.CompanyId);
        if (!effectivePlan.AllowOnlineBooking && !isStaffManualBooking)
            return StatusCode(402, "Online booking requires a paid subscription.");

        // US-67 (ARCHITECTURE_CYCLE6.md §47.1): serviceIds is the multi-service form of serviceId —
        // 1..5, no duplicates, serviceId must equal serviceIds[0] when both are sent.
        var selectionValidation = BookingServiceSelection.Validate(dto.ServiceId, dto.ServiceIds);
        if (!selectionValidation.IsValid) return BadRequest(selectionValidation.Message);

        var orderedServiceIds = BookingServiceSelection.Resolve(dto.ServiceId, dto.ServiceIds);
        var services = await db.Services.Where(s => orderedServiceIds.Contains(s.Id)).ToListAsync();
        if (services.Count != orderedServiceIds.Distinct().Count()) return NotFound("Service not found");
        // Objects exist but their combination doesn't make sense — 400, not 404 (ARCHITECTURE.md §14.4).
        if (services.Any(s => s.CompanyId != dto.CompanyId))
            return BadRequest("Услуга не относится к выбранной компании");
        if (services.Any(s => !s.IsActive)) return BadRequest("Услуга сейчас недоступна");
        if (!await CompanyMembership.IsStaffAsync(db, dto.CompanyId, dto.MasterId))
            return BadRequest("Master is not a staff member of this company");

        // The master must be able to perform EVERY selected service, not just the first one
        // (ARCHITECTURE_CYCLE6.md §47.1 p.2) — a client adding a service the chosen master doesn't do
        // gets told so directly, rather than a silently empty slot list.
        var unsupportedServices = await MasterCapability.FindUnsupportedServicesAsync(db, dto.MasterId, orderedServiceIds);
        if (unsupportedServices.Count > 0)
        {
            var unsupportedNames = services.Where(s => unsupportedServices.Contains(s.Id)).Select(s => s.Name);
            return BadRequest($"Мастер не оказывает услугу: {string.Join(", ", unsupportedNames)}");
        }

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
            return Conflict("Time slot is no longer available");

        // US-65/Q5 (ARCHITECTURE_CYCLE6.md §45.7 p.2/p.3): the client-only path is bounded by the
        // company's booking horizon; staff (manual bookings) are never subject to this — a salon must
        // always be able to book a regular ahead of the public window. 400, not 409: this is unrelated
        // to slot occupancy (§45.7 p.3).
        if (!isStaffManualBooking)
        {
            var horizonDays = BookingHorizon.Normalize(company!.BookingHorizonDays);
            var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
            if (!BookingHorizon.IsWithin(dto.Date, todayUtc, horizonDays))
                return BadRequest($"Записаться можно не дальше чем на {horizonDays} дней вперёд");
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
                return BadRequest("Для записи другого человека нужно подтвердить полномочия.");

            var guardianText = legalProvider.Current?.GetText(LegalTextKey.GuardianConfirmation);
            if (guardianText is null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Правовые документы временно недоступны.");
            if (dto.GuardianConfirmation.TextVersion != guardianText.Version)
                return Conflict("Текст подтверждения был обновлён — перечитайте и подтвердите заново.");

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
            CommissionPercent = masterCommissionPercent
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

        if (!slotOk) return Conflict("Time slot is no longer available");

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
        var createActor = await actorResolver.ResolveAsync(User, booking);
        eventLog.Append(booking, BookingEventKind.Created,
            createActor.Kind, createActor.UserId, createActor.NameSnapshot, createActor.RoleSnapshot);

        // ARCHITECTURE_CYCLE4.md §25.3: queued in the SAME transaction as the booking itself, after all
        // eight existing gates above (none of which are touched) and before SaveChangesAsync — the
        // scheduler only tracks changes on this same AppDbContext, it never calls SaveChangesAsync
        // itself. A failure here must not fail the booking (US-28 p.4: the booking is more important than
        // the notification), so it's caught and logged, never rethrown.
        try
        {
            await notificationScheduler.OnBookingCreatedAsync(
                booking, orderedServices.Select(s => s.Name).ToList(), HttpContext.RequestAborted);
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
                User.FindFirstValue(ClaimTypes.NameIdentifier), HttpContext.RequestAborted);
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

        return CreatedAtAction(nameof(GetById), new { id = booking.Id },
            MapToDto(booking, service, master!, clientName, reminderStatus: null, historyEventCount: null));
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
            .Include(b => b.BookingServices)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (booking is null) return NotFound();

        var canView = booking.ClientId == userId || await CanManageBookingAsync(booking, userId);
        if (!canView) return Forbid();

        var clientName = booking.Client is not null
            ? $"{booking.Client.FirstName} {booking.Client.LastName}"
            : booking.GuestName ?? "Guest";

        var reminderStatus = await ReminderStatusForAsync(booking.Id);

        // API_CONTRACT_CYCLE10.md §123: historyEventCount is filled here only for staff of this booking's
        // company (or SuperAdmin) — the same "personnel of the company" bar §122's history endpoint uses,
        // not the narrower CanManageBookingAsync (assigned master + owner) that gated `canView` above.
        var isStaffOfCompany = User.IsInRole("SuperAdmin") ||
            (userId is not null && await CompanyMembership.IsStaffAsync(db, booking.CompanyId, userId));
        int? historyEventCount = null;
        if (isStaffOfCompany)
            historyEventCount = await db.BookingEvents.CountAsync(e => e.BookingId == booking.Id);

        return Ok(MapToDto(booking, booking.Service, booking.Master, clientName, reminderStatus, historyEventCount));
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

        var bookings = await query.OrderByDescending(b => b.Date).ThenByDescending(b => b.StartTime).ToListAsync();

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
    public async Task<ActionResult<List<BookingDto>>> GetMasterBookings([FromQuery] DateOnly? date, [FromQuery] DateOnly? to)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var query = db.Bookings
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
            .ToListAsync();

        // API_CONTRACT_CYCLE4.md §30.3: one batched query for the whole page's reminder status, not one
        // per booking — same "batch, don't loop" convention as everything else added this cycle.
        var reminderStatusByBooking = await ReminderStatusesForAsync(bookings.Select(b => b.Id));
        // API_CONTRACT_CYCLE10.md §123: same batching convention for historyEventCount — one grouping
        // query for the whole page, not N+1.
        var historyCountByBooking = await HistoryEventCountsForAsync(bookings.Select(b => b.Id));

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
        // The fallback mirrors MapToDto below: if BookingServices is somehow empty, fall back to the
        // single legacy service rather than summing to zero. Without it a row-less visit reschedules
        // to EndTime == StartTime and silently collapses to nothing — the two other places that read
        // this sum already guard it, and the asymmetry was a review finding of this cycle.
        var totalDurationMinutes = booking.BookingServices is { Count: > 0 }
            ? booking.BookingServices.Sum(bs => bs.DurationMinutes)
            : booking.Service.DurationMinutes;
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
                var serviceNames = booking.BookingServices is { Count: > 0 }
                    ? booking.BookingServices.OrderBy(bs => bs.Position).Select(bs => bs.NameSnapshot).ToList()
                    : [booking.Service.Name];
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

    private static BookingDto MapToDto(Booking b, Service s, AppUser master, string clientName,
        ReminderStatusDto? reminderStatus = null, int? historyEventCount = null,
        bool? clientRescheduleAllowed = null, int? clientRescheduleMinHours = null, int? companyBookingHorizonDays = null,
        bool? clientCancelAllowed = null)
    {
        // US-67 (API_CONTRACT_CYCLE6.md §43.2): `services` is built from BookingServices when loaded
        // (every path except the in-memory object returned by Create, which sets it explicitly before
        // calling here); falls back to the single legacy service `s` only if BookingServices wasn't
        // populated at all, which should never happen after the AddBookingServices backfill.
        var items = b.BookingServices is { Count: > 0 }
            ? b.BookingServices.OrderBy(bs => bs.Position)
                .Select(bs => new BookingServiceItemDto(bs.ServiceId, bs.NameSnapshot, bs.DurationMinutes, bs.Price))
                .ToList()
            : [new BookingServiceItemDto(b.ServiceId, s.Name, s.DurationMinutes, b.Price)];
        var totalDurationMinutes = items.Sum(i => i.DurationMinutes);

        return new(b.Id, b.CompanyId, b.Company?.Name ?? "", b.Company?.Slug ?? "", b.ServiceId, items[0].Name, b.MasterId,
            $"{master.FirstName} {master.LastName}", b.ClientId, clientName,
            b.GuestPhone ?? b.Client?.PhoneNumber, b.GuestEmail ?? b.Client?.Email,
            b.Date, b.StartTime, b.EndTime, b.Status, b.PaymentStatus, b.Price, b.CancellationReason,
            b.Notes, b.CreatedAt,
            b.ConsentPrivacyVersion, b.ConsentTermsVersion, b.ConsentAcceptedAtUtc, b.ClientDeleted, reminderStatus,
            totalDurationMinutes, items,
            b.BookingNoticeVersion, b.BookedForOther, b.GuardianConfirmedAtUtc, historyEventCount,
            clientRescheduleAllowed, clientRescheduleMinHours, companyBookingHorizonDays, clientCancelAllowed);
    }

    // API_CONTRACT_CYCLE4.md §30.3. Picks the highest-Generation Reminder row for a booking (§23.5: a
    // reschedule supersedes the previous generation's row rather than mutating it), so a rescheduled
    // booking's card reflects the CURRENT reminder, not one already Cancelled by NotificationScheduler.
    private async Task<ReminderStatusDto?> ReminderStatusForAsync(Guid bookingId)
    {
        var map = await ReminderStatusesForAsync([bookingId]);
        return map.GetValueOrDefault(bookingId);
    }

    private async Task<Dictionary<Guid, ReminderStatusDto>> ReminderStatusesForAsync(IEnumerable<Guid> bookingIds)
    {
        var ids = bookingIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        var rows = await db.OutboundNotifications.AsNoTracking()
            .Where(n => n.BookingId != null && ids.Contains(n.BookingId!.Value) && n.Type == NotificationType.Reminder)
            .ToListAsync();

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
    private async Task<Dictionary<Guid, int>> HistoryEventCountsForAsync(IEnumerable<Guid> bookingIds)
    {
        var ids = bookingIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        return await db.BookingEvents.AsNoTracking()
            .Where(e => ids.Contains(e.BookingId))
            .GroupBy(e => e.BookingId)
            .Select(g => new { BookingId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BookingId, x => x.Count);
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
    public async Task<ActionResult<BookingHistoryDto>> GetHistory(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var booking = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null) return NotFound();

        var isStaffOfCompany = User.IsInRole("SuperAdmin") ||
            await CompanyMembership.IsStaffAsync(db, booking.CompanyId, userId);
        if (!isStaffOfCompany) return NotFound();

        var events = await db.BookingEvents.AsNoTracking()
            .Where(e => e.BookingId == id)
            .OrderBy(e => e.OccurredAtUtc)
            .ToListAsync();

        // §122.2: a booking predates the journal exactly when it has no Created event — no backfill
        // (decision П3), so this is computed, not stored.
        var precedesJournal = events.All(e => e.Kind != BookingEventKind.Created);

        var eventDtos = events.Select(e => new BookingEventDto(
            e.Id, e.Kind, e.OccurredAtUtc,
            BookingEventTexts.Title(e.Kind),
            new BookingEventActorDto(
                e.ActorKind, e.ActorNameSnapshot, e.ActorRoleSnapshot,
                BookingEventTexts.ActorLabel(e.Kind, e.ActorKind, e.ActorNameSnapshot, e.ActorRoleSnapshot)),
            e.Kind == BookingEventKind.Rescheduled && e.PreviousDate is not null && e.PreviousStartTime is not null
                && e.NewDate is not null && e.NewStartTime is not null
                ? new BookingRescheduleDto(e.PreviousDate.Value, e.PreviousStartTime.Value, e.NewDate.Value, e.NewStartTime.Value)
                : null,
            e.Kind == BookingEventKind.Cancelled ? e.CancellationReason : null
        )).ToList();

        return Ok(new BookingHistoryDto(id, precedesJournal, eventDtos));
    }
}
