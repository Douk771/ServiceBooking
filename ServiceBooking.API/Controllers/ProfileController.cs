using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Bookings;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.PhoneVerification;
using ServiceBooking.API.Services.Subjects;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using PhoneVerificationRefDto = ServiceBooking.API.DTOs.PhoneVerification.PhoneVerificationRefDto;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileController(
    UserManager<AppUser> userManager, AppDbContext db, ImageUploadService imageUploadService, FileStorage storage,
    SubscriptionResolver subscriptionResolver, AccountUsageReader usageReader,
    LegalDocumentProvider legalProvider, ConsentLedger ledger, HealthNoteProtector healthNoteProtector,
    GuestBookingLookup guestBookingLookup, IPhoneVerificationMethodRegistry phoneVerificationRegistry,
    PhoneVerificationWriter phoneVerificationWriter, IOptions<PhoneVerificationOptions> phoneVerificationOptions,
    SubjectScopeResolver subjectScopeResolver, ILogger<ProfileController> logger)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ProfileDto>> Get()
    {
        var user = await userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user is null) return NotFound();
        var roles = await userManager.GetRolesAsync(user);
        return Ok(await MapToDtoAsync(user, roles));
    }

    // US-38, ARCHITECTURE.md §7.1/§7.2. Synchronous — one client's data is tens of rows/KB, not worth
    // the four new moving parts a background job would need (task, artifact storage, TTL, download
    // endpoint). Rate-limited to 3/day (policy "data-export") instead, and allow-listed in
    // LegalConsentFilter: the right to a copy of one's own data must not depend on having accepted a
    // new policy revision (ARCHITECTURE.md §19.4).
    //
    // Deliberately excludes: note TEXT (a staff member's judgment about the client, not the client's own
    // data in a clean sense — API_CONTRACT.md §8) and photo CONTENT (the private storage class's access
    // model is staff-of-company-only, SuperAdmin included gets 403 on the raw endpoint — an export that
    // handed the bytes to the subject directly would go around that model sideways). Only counts/sizes
    // are included for both. No foreign ids, no hashes (PasswordHash, SecurityStamp, ContentHash) appear
    // anywhere in the response — human-readable names stand in for every reference to another entity.
    [HttpGet("export")]
    [EnableRateLimiting("data-export")]
    public async Task<IActionResult> Export()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();

        // ARCHITECTURE_CYCLE5.md §44.2/§50.2: reads the journal, not a "current state" row — the FULL
        // history for this subject, exactly what GET /api/profile/consents' `history` field shows,
        // because an export is a legal artifact and a revoked/superseded grant is still something the
        // subject did.
        // knownPhone (code review В3): pulls in salon-recorded PhotoConsent/HealthDataConsent rows too —
        // see ConsentLedger.HistoryAsync's doc comment.
        var consentHistory = await ledger.HistoryAsync(ConsentSubject.ForUser(userId), user.PhoneNumber);
        var consent = consentHistory.Select(ToExportConsentDto).ToList();

        var memberships = await db.CompanyMembers
            .Include(cm => cm.Company)
            .Where(cm => cm.UserId == userId)
            .Select(cm => new ExportMembershipDto(cm.Company.Name, cm.Role.ToString(), cm.JoinedAt))
            .ToListAsync();

        // Matches DeleteAccount's step 3 and MastersController.GetClients: a visit made as a guest
        // BEFORE this person registered, on the same canonical phone, is data about this subject just
        // as much as a visit made while logged in — DeleteAccount already anonymizes both branches, so
        // the export must show both too, or a deletion could erase data the export never revealed
        // (API_CONTRACT.md §8).
        // TD-03 (ARCHITECTURE_CYCLE16.md §245): the single gate. `ownPhone` is the account's own
        // contact — always shown back to the account (ExportProfileDto below). `guestMatchPhone` is
        // null unless this account has PROVEN it owns that number (VerifiedPhones) — every predicate
        // below that used to read a single ambiguous `canonicalPhone` now reads exactly one of the two,
        // per §245.4's distribution table.
        var scope = await subjectScopeResolver.ForAccountAsync(user, HttpContext.RequestAborted);
        var ownPhone = scope.OwnPhone;
        var guestMatchPhone = scope.GuestMatchPhone; // SUBJECT-PHONE-GATE: gated — canonical guard for the whole export
        var bookings = await db.Bookings
            .Include(b => b.Service).Include(b => b.Master).Include(b => b.Company)
            .Where(b => b.ClientId == userId || (guestMatchPhone != null && b.GuestPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
            .OrderByDescending(b => b.Date).ThenByDescending(b => b.StartTime)
            .Select(b => new ExportBookingDto(
                b.Id, b.Date, b.StartTime, b.EndTime, b.Company.Name, b.Service.Name,
                b.Master.FirstName + " " + b.Master.LastName, b.Status.ToString(), b.PaymentStatus.ToString(),
                b.Price, b.CancellationReason))
            .ToListAsync();

        var reviews = await db.Reviews
            .Include(r => r.Company)
            .Where(r => r.ClientId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ExportReviewDto(r.Company.Name, r.Rating, r.Comment, r.CreatedAt))
            .ToListAsync();

        // Notes/photos "about me" are found the same way DeleteAccount and MastersController.GetClients
        // do: by ClientId for a registered client, or by canonical GuestPhone for visits made before
        // registering (a client can be both, if they booked as a guest before signing up).
        var notesAboutMe = await db.ClientNotes
            .Include(n => n.Company).Include(n => n.Photos)
            .Where(n => n.ClientId == userId || (guestMatchPhone != null && n.GuestPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new ExportNoteMetaDto(n.Company.Name, n.CreatedAt, n.Photos.Count))
            .ToListAsync();

        var photosOfMe = await db.ClientNotePhotos
            .Include(p => p.ClientNote).ThenInclude(n => n.Company)
            .Where(p => p.ClientNote.ClientId == userId || (guestMatchPhone != null && p.ClientNote.GuestPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ExportPhotoMetaDto(p.ClientNote.Company.Name, p.CreatedAt, p.SizeBytes))
            .ToListAsync();

        // T5-B11 (ARCHITECTURE_CYCLE5.md §50.2, API_CONTRACT_CYCLE5.md §49). "Без N+1": three cheap
        // id-only queries (one per source — bookings/notes/photos already scoped to this subject exactly
        // like the sections above) build BOTH the company-id union and the "what is stored" tags in one
        // pass, then ONE second query fetches the company cards themselves — never one query per company.
        var bookingCompanyIds = await db.Bookings
            .Where(b => b.ClientId == userId || (guestMatchPhone != null && b.GuestPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
            .Select(b => b.CompanyId).Distinct().ToListAsync();
        var noteCompanyIds = await db.ClientNotes
            .Where(n => n.ClientId == userId || (guestMatchPhone != null && n.GuestPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
            .Select(n => n.CompanyId).Distinct().ToListAsync();
        var photoCompanyIds = await db.ClientNotePhotos
            .Where(p => p.ClientNote.ClientId == userId || (guestMatchPhone != null && p.ClientNote.GuestPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
            .Select(p => p.CompanyId).Distinct().ToListAsync();
        // Code review В4: was ClientId-only, unlike every neighboring section above — a health note filed
        // while this person was still a guest (booked, then registered later) is stored by GuestPhone,
        // exactly like ClientNote/ClientNotePhoto, and the export silently omitted it.
        var healthNoteRows = await db.ClientHealthNotes
            .Where(n => n.ClientId == userId || (guestMatchPhone != null && n.GuestPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
            .ToListAsync();

        var whatIsStoredByCompany = new Dictionary<Guid, List<string>>();
        void Tag(IEnumerable<Guid> companyIds, string kind)
        {
            foreach (var id in companyIds)
            {
                if (!whatIsStoredByCompany.TryGetValue(id, out var list)) whatIsStoredByCompany[id] = list = [];
                if (!list.Contains(kind)) list.Add(kind);
            }
        }
        Tag(bookingCompanyIds, "bookings");
        Tag(noteCompanyIds, "notes");
        Tag(photoCompanyIds, "photos");
        Tag(healthNoteRows.Select(n => n.CompanyId), "healthNotes");

        var operatorCompanyIds = whatIsStoredByCompany.Keys.ToList();
        var operators = operatorCompanyIds.Count == 0 ? []
            : await db.Companies.AsNoTracking().Where(c => operatorCompanyIds.Contains(c.Id))
                .Select(c => new ExportOperatorDto(c.Id, c.Name, c.Address, c.Phone, c.Email, whatIsStoredByCompany[c.Id]))
                .ToListAsync();

        // US-75 — sent notifications and opt-out status. bodyAvailable reflects §51's затирание: a row
        // past its retention window has ContentRedactedAtUtc set, and the export must say so plainly
        // rather than showing an empty string that looks like "nothing was ever sent".
        var notifications = await db.OutboundNotifications.AsNoTracking()
            .Include(n => n.Company)
            .Where(n => n.RecipientUserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new ExportNotificationDto(
                n.SentAtUtc, n.Type.ToString(), n.Status.ToString(), n.Company.Name, n.ContentRedactedAtUtc == null))
            .ToListAsync();
        var optOutRow = guestMatchPhone is null ? null
            : await db.NotificationOptOuts.AsNoTracking().FirstOrDefaultAsync(o => o.Phone == guestMatchPhone);  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
        var optOut = new ExportOptOutDto(optOutRow is not null, optOutRow?.OptedOutAtUtc);

        // US-77 — decrypted explicitly (HealthNoteProtector's own doc comment: never via a transparent
        // converter), same subject-key convention ClientConsentsController uses (userId as SubjectKey for
        // a registered client — this export only ever runs for the account holder themselves).
        var companyNameById = operators.ToDictionary(o => o.CompanyId, o => o.Name);
        var healthNotesExport = new List<ExportHealthNoteDto>();
        foreach (var note in healthNoteRows)
        {
            var value = healthNoteProtector.Unprotect(note.Ciphertext, note.CompanyId, userId);
            healthNotesExport.Add(new ExportHealthNoteDto(companyNameById.GetValueOrDefault(note.CompanyId, ""), value, note.UpdatedAt));
        }

        // ARCHITECTURE_CYCLE14.md §151.3, API_CONTRACT_CYCLE14.md §170.3: only the fact/method/date of
        // verification for the CURRENT number — read straight from VerifiedPhone (source of truth) rather
        // than trusting the mirror alone, since the mirror could in principle drift. The MAX-account
        // identifier (even hashed) and any open session are deliberately excluded — neither is data the
        // subject can meaningfully read back, and an open session lives minutes anyway.
        var verifiedPhoneRow = guestMatchPhone is null ? null
            : await db.VerifiedPhones.AsNoTracking().FirstOrDefaultAsync(v => v.Phone == guestMatchPhone);  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
        var phoneVerification = new ExportPhoneVerificationDto(
            verifiedPhoneRow is not null, verifiedPhoneRow?.Method.ToString(), verifiedPhoneRow?.VerifiedAtUtc);

        // TD-03 (ARCHITECTURE_CYCLE16.md §245.6, API_CONTRACT_CYCLE16.md §273.1). §272/A2: this section
        // MUST NOT become an oracle for "does this phone have guest data" — it reports only that the
        // rule was applied, never a count or a yes/no about hidden rows. `applied` is derived from
        // `scope.GateApplied` alone, never from whether any of the sections above turned out empty.
        var guestDataGate = new ExportGuestDataGateDto(
            scope.GateApplied,
            scope.GateApplied ? "PhoneNotVerified" : null,
            scope.GateApplied ? SubjectGateTexts.Resolve(legalProvider.Current) : null,
            // BACKEND DEVIATION FROM ARCHITECTURE_CYCLE16.md §245.6/§273.1/§277: those sections write
            // "/subject-request" throughout, but the route that has actually existed in the app since
            // cycle 5 is "/data-request" (frontend/src/App.tsx, SubjectRequestPage.tsx) — "/subject-request"
            // does not exist and would 404. A prior partial run of this cycle already found and fixed
            // this on the frontend side (commit f94a052, useExportData.test.tsx) and flagged the
            // contract/architecture docs as still needing the same fix from architect. Backend here
            // follows the real, working route rather than reproducing the doc's typo. See report.
            scope.GateApplied ? "/data-request" : null);

        if (scope.GateApplied)
        {
            // §245.7: name and endpoint only, no phone, no counts (NFT §7.4).
            logger.LogInformation("guest-data gate applied: userId={UserId} endpoint={Endpoint}", userId, "profile/export");
        }

        // ARCHITECTURE_CYCLE5.md §50.2, API_CONTRACT_CYCLE5.md §49: "признаны результатом работы салона"
        // is removed — it is not a valid ground for refusal (ч. 8 ст. 14 152-ФЗ is exhaustive). Replaced
        // with routing to the actual operator of that data — see `operators` above.
        var export = new ProfileExportDto(
            DateTime.UtcNow,
            // ownPhone (§245.4 table): the account's own contact — shown regardless of verification.
            new ExportProfileDto(user.FirstName, user.LastName, ownPhone, user.Email, user.AvatarUrl, user.CreatedAt),
            consent, memberships, bookings, reviews, notesAboutMe, photosOfMe,
            // Code review, "заодно": names the section by key, not just by description — the reader
            // must not have to guess which of several sections in this same file "compan(ies) below" refers to.
            "Оператором заметок, фотографий и сведений, внесённых сотрудниками компании, является сама " +
            "компания — контакты и адрес каждой такой компании перечислены в разделе «operators» этой " +
            "выгрузки. Запрос об их предоставлении, уточнении или удалении направляйте ей напрямую. По " +
            "вопросам обработки ваших данных платформой обращайтесь в поддержку сервиса.",
            operators, notifications, optOut, healthNotesExport, phoneVerification, guestDataGate);

        Response.Headers.ContentDisposition =
            $"attachment; filename=\"servicebooking-export-{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}.json\"";
        return Ok(export);
    }

    [HttpPut]
    public async Task<ActionResult<ProfileDto>> Update([FromBody] UpdateProfileDto dto)
    {
        var user = await userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user is null) return NotFound();

        user.FirstName = dto.FirstName;
        user.LastName = dto.LastName;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded) return BadRequest(result.Errors.FirstOrDefault()?.Description);

        var roles = await userManager.GetRolesAsync(user);
        return Ok(await MapToDtoAsync(user, roles));
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        var user = await userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user is null) return NotFound();

        var result = await userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);
        if (!result.Succeeded) return BadRequest(result.Errors.FirstOrDefault()?.Description ?? "Неверный текущий пароль");
        return NoContent();
    }

    // Phone doubles as the Identity UserName (see AuthController), so changing it goes through
    // SetUserNameAsync — that's what actually enforces the uniqueness check and persists both
    // PhoneNumber and UserName/NormalizedUserName in one write. Requires the current password,
    // same as changing the password, since it's effectively changing the login identifier.
    //
    // ⚠️ ARCHITECTURE_CYCLE14.md §148.5, API_CONTRACT_CYCLE14.md §169 (US-14-17) — LOMAYUSHCHEE
    // (breaking) change of behavior, the ONE HTTP-visible breaking change of cycle 14: changing to a
    // number that already has GUEST bookings on it now requires a verified MAX session for that number
    // first. Every other change (no guest bookings on the new number, or the number not changing at
    // all) works exactly as before — Р1. Formulation that MUST accompany any description of this
    // result: закрыт путь через смену номера; путь через регистрацию нового аккаунта на чужой номер
    // остаётся открытым осознанно (SPEC.md §6.4, блок V1 в CURRENT_STATE.md §9) — see §153.4/§140.3.
    [HttpPost("change-phone")]
    [EnableRateLimiting("phone-change")]
    public async Task<ActionResult<ProfileDto>> ChangePhone([FromBody] ChangePhoneDto dto)
    {
        var user = await userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user is null) return NotFound();

        if (!await userManager.CheckPasswordAsync(user, dto.CurrentPassword))
            return BadRequest("Неверный текущий пароль");

        // US-26: the canonical form is what's stored, never what the user typed — see PhoneNormalizer.
        // US-61/Q4 (§48.2 p.2): changing to a NEW number only accepts the Russian format; an existing
        // foreign number already on the account is untouched by this check (§48.4).
        if (!PhoneNormalizer.TryNormalizeRussian(dto.NewPhone, out var canonicalPhone))
            return BadRequest("Введите номер телефона в формате +7 (900) 000-00-00");

        var oldPhone = user.PhoneNumber;
        var phoneIsChanging = oldPhone != canonicalPhone;

        // §148.5 steps 3-4: the gate participates ONLY when the number is actually changing AND the new
        // number has guest bookings on it (GuestBookingLookup — §142.5's one indexed EXISTS).
        PhoneVerificationSession? verificationSession = null;
        if (phoneIsChanging && await guestBookingLookup.HasGuestBookingsAsync(canonicalPhone, HttpContext.RequestAborted))
        {
            var adapter = phoneVerificationRegistry.Get(PhoneVerificationMethod.MaxBot);
            var now = DateTime.UtcNow;

            if (dto.Verification is not null)
            {
                var candidate = await db.PhoneVerificationSessions.FirstOrDefaultAsync(s => s.Id == dto.Verification.SessionId);
                var tokenMatches = candidate is not null && !string.IsNullOrEmpty(dto.Verification.StatusToken) &&
                    StatusTokenGenerator.Hash(dto.Verification.StatusToken) == candidate.StatusTokenHash;
                if (PhoneVerificationSessionAcceptance.IsUsableForChangePhone(candidate, tokenMatches, user.Id, canonicalPhone, now))
                {
                    verificationSession = candidate;
                }
            }

            var gateOutcome = GuestBookingGateDecision.Evaluate(
                phoneIsChanging: true, newNumberHasGuestBookings: true,
                validSessionPresented: verificationSession is not null, subsystemEnabled: adapter.Enabled);

            if (gateOutcome != ChangePhoneGateOutcome.Allow)
            {
                // §148.5, R14: every attempt that hits the gate is logged — this is the postfactum
                // detection for the residual enumeration-oracle risk Р3 accepts, not the gate itself.
                logger.LogInformation(
                    "phone-change gate blocked: userId={UserId} phone={MaskedPhone} reason={Reason}",
                    user.Id, LogMasking.Phone(canonicalPhone), "GuestBookingsExist");

                return Conflict(gateOutcome == ChangePhoneGateOutcome.SubsystemDisabled
                    ? PhoneVerificationTexts.ChangePhoneSubsystemDisabled
                    : PhoneVerificationTexts.ChangePhoneNeedsVerification);
            }
        }

        if (phoneIsChanging)
        {
            // §148.5 step 6 (US-14-11): the whole change — UserName/PhoneNumber, dropping the OLD
            // number's verification, and writing whatever THIS call proved about the NEW one — happens
            // in ONE transaction (review finding: SetUserNameAsync used to save on its own, before the
            // transaction below even opened, so a later failure in this block would leave the account's
            // login identifier changed while the verification rows stayed on the old number).
            await using var transaction = await db.Database.BeginTransactionAsync();

            user.PhoneNumber = canonicalPhone;
            var result = await userManager.SetUserNameAsync(user, canonicalPhone);
            if (!result.Succeeded)
            {
                var isDuplicate = result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.DuplicateUserName));
                return BadRequest(isDuplicate
                    ? "Этот номер телефона уже используется другим аккаунтом"
                    : result.Errors.FirstOrDefault()?.Description ?? "Не удалось изменить номер телефона");
            }

            if (oldPhone is not null)
                await phoneVerificationWriter.RemoveForOldNumberAsync(oldPhone, user.Id, HttpContext.RequestAborted);

            user.PhoneNumberConfirmed = false;
            if (verificationSession is not null)
            {
                await AdvisoryLock.AcquireAsync(db, $"phone-verification:{verificationSession.ExternalAccountKey}");
                await phoneVerificationWriter.WriteAsync(
                    verificationSession, user, phoneVerificationOptions.Value.MaxPhonesPerExternalAccount, HttpContext.RequestAborted);
                verificationSession.Status = PhoneVerificationStatus.Consumed;
                // WriteAsync only flips PhoneNumberConfirmed back to true on an actual write — a race
                // that exhausts the ceiling between the gate check above and here leaves it correctly
                // false, exactly like §148.1 step 5's registration-side equivalent (Р1).
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        var roles = await userManager.GetRolesAsync(user);
        return Ok(await MapToDtoAsync(user, roles));
    }

    // US-39, ARCHITECTURE.md §7.3/§7.4/§19.2. POST, not DELETE: needs a body with the current password,
    // the same jest as change-phone above — DELETE with a body is poorly supported by proxies/clients,
    // and this product already has the convention.
    //
    // The row survives as a tombstone (DeletedAtUtc) rather than being physically removed — the
    // decision that departs from SPEC's literal wording (US-39 p.2), recorded in ARCHITECTURE.md §19.2:
    // Booking.Master/Review.Master/Company.Owner/MailLog.SentBy are Restrict (a physical delete would
    // simply fail), and ClientNote.Master is Cascade, so it would silently destroy every note this
    // person wrote about OTHER clients — company data, explicitly protected by US-39 p.3 / risk R4.
    // Every personal-data field on the row is scrubbed instead; DeletedAtUtc is what makes that
    // "scrubbed on purpose" rather than indistinguishable from corruption.
    [HttpPost("delete-account")]
    public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();

        if (!await userManager.CheckPasswordAsync(user, dto.CurrentPassword))
            return BadRequest("Неверный текущий пароль");

        // Gate #2: a company owner must transfer or close their company first — deleting the account
        // out from under an active business is not this endpoint's job (API_CONTRACT.md §9).
        if (await db.Companies.AnyAsync(c => c.OwnerUserId == userId))
            return Conflict("За вами числится компания. Передайте её другому владельцу или обратитесь " +
                             "в поддержку — тогда аккаунт можно будет удалить.");

        // TD-03 (ARCHITECTURE_CYCLE16.md §245): resolved before any field on `user` is scrubbed below.
        // `guestMatchPhone` is null unless this account proved it owns its own number — an unverified
        // account can no longer physically DESTROY someone else's guest-recorded data just by deleting
        // itself. `ownPhone` is captured only for the avatar/file cleanup below, which is this
        // account's own data regardless of verification.
        var scope = await subjectScopeResolver.ForAccountAsync(user, HttpContext.RequestAborted);
        var guestMatchPhone = scope.GuestMatchPhone; // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
        var oldAvatarUrl = user.AvatarUrl;

        if (scope.GateApplied)
        {
            // §245.7: name and endpoint only, no phone, no counts (NFT §7.4).
            logger.LogInformation("guest-data gate applied: userId={UserId} endpoint={Endpoint}", userId, "profile/delete-account");
        }

        await using var transaction = await db.Database.BeginTransactionAsync();

        // Step 1 used to delete UserConsent rows outright here. CYCLE5-BREAKING (ARCHITECTURE_CYCLE5.md
        // §44.2 p.5, §55.1 R6): the consent journal now OUTLIVES account deletion — a ConsentRecord is
        // evidence the OPERATOR needs to prove what was consented to (ч. 1 ст. 9: "доказывает оператор"),
        // not personal convenience data the subject can erase on demand. All three of ConsentRecord's FKs
        // are NO ACTION specifically so this can't silently regress even if this comment goes stale — the
        // rows physically cannot be removed by cascading `user` below. Retention (§49.1, not built by
        // this task) is what eventually ages them out, at a minimum of three years, never on request.

        // Step 2: notes ABOUT this person (by ClientId, or by guest phone for pre-registration visits) —
        // gather photo storage keys before the cascade delete removes the ClientNotePhoto rows, since
        // the files themselves live outside the database (ARCHITECTURE.md §1.4: DB row goes first, file
        // cleanup happens only after a successful commit).
        var notesAboutMe = await db.ClientNotes
            .Include(n => n.Photos)
            .Where(n => n.ClientId == userId || (guestMatchPhone != null && n.GuestPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
            .ToListAsync();
        var photoKeysToDelete = notesAboutMe.SelectMany(n => n.Photos)
            .Select(p => (p.StoragePath, p.ThumbnailPath)).ToList();
        db.ClientNotes.RemoveRange(notesAboutMe); // cascades ClientNotePhotos (AppDbContext)

        // Step 2b (code review, Б1): ClientHealthNote is a SEPARATE table from ClientNote (deliberately,
        // §44.3 — no navigation property between them at all), with a NO ACTION FK, so it does not
        // cascade with the ClientNotes.RemoveRange above and was missing here entirely until this fix.
        // Left alone, the row was unreachable by ANY product path after this method ran: ResolveClientAsync
        // (ClientConsentsController) looks the subject up by ClientId OR canonical GuestPhone, and this
        // method has just cleared user.PhoneNumber — so DELETE health-note would 404 forever, and the
        // row would sit until the retention sweep aged it out three years later. Same double condition as
        // notesAboutMe above.
        var healthNotesAboutMe = await db.ClientHealthNotes
            .Where(h => h.ClientId == userId || (guestMatchPhone != null && h.GuestPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
            .ToListAsync();
        db.ClientHealthNotes.RemoveRange(healthNotesAboutMe);

        // Step 2c (ARCHITECTURE_CYCLE9.md §105.5, US-123): this person's OWN Web Push subscriptions —
        // ⚠️ the Cascade FK on PushSubscription.UserId (AppDbContext) does NOT fire here: this method
        // leaves a TOMBSTONE (AppUser.DeletedAtUtc is set below; the row itself is never removed), so
        // nothing about this deletion is actually a cascadable "AppUser row went away". Deleted
        // explicitly instead, in the same transaction, same reasoning as I10's owned-channel cleanup
        // further below (a channel this person OWNS is decommissioned explicitly for the identical
        // reason). A person who deletes their account must stop receiving push about a platform they no
        // longer have credentials to see.
        var ownPushSubscriptions = await db.PushSubscriptions.Where(s => s.UserId == userId).ToListAsync();
        db.PushSubscriptions.RemoveRange(ownPushSubscriptions);

        // Step 2d (ARCHITECTURE_CYCLE14.md §151.2, US-14-12): same reasoning as step 2c — the Cascade FK
        // on VerifiedPhone.UserId/PhoneVerificationSession.UserId never actually fires (this account is
        // tombstoned, not removed), so both are cleaned up explicitly, in the same transaction. This is
        // what makes the MAX-account identifier (as HMAC) genuinely unrecoverable after deletion (Р4),
        // frees this person's slot in the per-MAX-account ceiling (Q10), and guarantees a future
        // registration on this same number starts unverified (Q9) — by removing the row, not a flag flip.
        db.VerifiedPhones.RemoveRange(
            await db.VerifiedPhones.Where(v => v.UserId == userId
                || (guestMatchPhone != null && v.Phone == guestMatchPhone)).ToListAsync());  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
        // Review finding: matching purely by CanonicalPhone (with no UserId check) used to also delete a
        // COMPLETE STRANGER's still-live session on the same number — e.g. someone else mid-registration
        // on the exact phone this account is being deleted from under, whose next poll would 404 without
        // warning. Own sessions (any status) are always this account's to remove; a session that merely
        // SHARES the phone is only swept up once it's already terminal — cleanup, not a race with a live
        // caller.
        db.PhoneVerificationSessions.RemoveRange(
            await db.PhoneVerificationSessions.Where(s => s.UserId == userId
                || (guestMatchPhone != null && s.CanonicalPhone == guestMatchPhone  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
                    && s.Status != PhoneVerificationStatus.Pending && s.Status != PhoneVerificationStatus.Linked))
                .ToListAsync());

        // Step 3: bookings are anonymized, never deleted — the salon's revenue/commission history for a
        // completed visit must stay intact (US-39 p.3). Matches both the client path and the guest path
        // (a booking made before this person registered, found the same way as step 2's notes).
        var bookingsToAnonymize = await db.Bookings
            .Where(b => b.ClientId == userId || (guestMatchPhone != null && b.GuestPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
            .ToListAsync();
        foreach (var booking in bookingsToAnonymize)
        {
            booking.ClientId = null;
            booking.GuestName = null;
            booking.GuestPhone = null;
            booking.GuestEmail = null;
            booking.Notes = null;
            booking.ClientDeleted = true;
        }

        // TD-05 (ARCHITECTURE_CYCLE16.md §247.2, no migration — §240.3/§247.1). Two rules, both scoped
        // to exactly the set of bookings the gate above already allowed touching (never a separate
        // phone-matching lookup here — that would be a sixth place, §245.2/§247.2):
        //   - Client events for THIS account (ActorUserId == userId) → tombstone.
        //   - Guest events on a booking that was just anonymized above → tombstone (same person, no
        //     account link existed at the time).
        // Staff/SuperAdmin/System events are untouched — different subject, different retention (D1/TD-18).
        const string deletedActorTombstone = "Удалённый пользователь"; // same form as Reviews.ReviewerName above
        var anonymizedBookingIds = bookingsToAnonymize.Select(b => b.Id).ToHashSet();
        var eventsToTombstone = await db.BookingEvents
            .Where(e =>
                (e.ActorKind == BookingActorKind.Client && e.ActorUserId == userId) ||
                (e.ActorKind == BookingActorKind.Guest && anonymizedBookingIds.Contains(e.BookingId)))
            .ToListAsync();
        foreach (var bookingEvent in eventsToTombstone)
            bookingEvent.ActorNameSnapshot = deletedActorTombstone;

        // I9/N9: EVERY notification row for this person carries its own snapshot of the recipient's
        // phone/name/rendered text (§23.4), independent of the booking row anonymized above — cancelling
        // the booking does NOT touch these, and neither does anonymizing only the Pending ones: a
        // terminal row (Sent/Delivered/Failed/...) keeps exactly the same personal data, forever, right
        // next to a booking that's already been scrubbed in the SAME method. Split by whether the row is
        // still actionable:
        //   - Pending: cancelled AND scrubbed (was already sent nowhere — nothing to preserve).
        //   - Everything else (terminal): scrubbed ONLY — Status/Reason/timestamps/ProviderMessageId are
        //     left untouched, since they're the delivery record itself (§23.4: "a permanent journal
        //     entry"), not personal data about the recipient the way the phone/name/body are.
        // NotificationOptOut rows are deliberately NOT touched here — they are what stops the platform
        // from ever messaging this phone again, which is the opposite of what this endpoint should undo.
        var allNotifications = await db.OutboundNotifications
            .Where(n => n.RecipientUserId == userId || (guestMatchPhone != null && n.RecipientPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
            .ToListAsync();
        foreach (var notification in allNotifications)
        {
            if (notification.Status == NotificationStatus.Pending)
            {
                notification.Status = NotificationStatus.Cancelled;
                notification.Reason = NotificationReason.BookingOrAssignmentCancelled;
            }

            notification.RecipientName = null;
            // N8: empty, not a fake sentinel like the previous "deleted" — PhoneDisplayMask.Mask would
            // otherwise run it through the generic fallback and produce something that LOOKS like a real
            // masked phone ("+de***ed"). Empty never happens on an ordinary row, so
            // CompanyNotificationsController.GetLog's mapping keys off exactly this to show "получатель
            // удалён" instead of masking it.
            notification.RecipientPhone = string.Empty;
            notification.Body = string.Empty;
        }

        // Step 4: reviews are depersonalized, not deleted — the review is about the salon, and the
        // rating/text remain meaningful without the author's identity attached.
        var reviewsToDeperson = await db.Reviews.Where(r => r.ClientId == userId).ToListAsync();
        foreach (var review in reviewsToDeperson)
        {
            review.ClientId = null;
            review.ReviewerName = "Удалённый пользователь";
        }

        // I10 / SPEC US-56 п. 5: this person's own WhatsApp channels (they may own one even without
        // owning a company — gate #2 above only blocks deletion while a COMPANY is still theirs) must be
        // decommissioned too, not left running and billed forever with nobody left who can ever log in
        // to disconnect them. §30.4 database-first only, same as the webhook's ban handling above in this
        // cycle's report (I2/B8): orphan the instance id, blank the channel's own credentials, cancel its
        // Pending rows — the actual provider delete is picked up by ChannelHealthTask's orphan-retry
        // sweep rather than a synchronous provider call inside this already-long transaction.
        var ownedChannels = await db.NotificationChannels
            .Where(c => c.OwnerUserId == userId && c.State != ChannelState.Replaced)
            .ToListAsync();
        foreach (var ownedChannel in ownedChannels)
        {
            var instanceId = ownedChannel.ProviderInstanceId;
            if (instanceId is not null)
            {
                ownedChannel.OrphanedInstanceId = instanceId;
                ownedChannel.ProviderInstanceId = null;
                ownedChannel.ProviderSecretCiphertext = null;
                ownedChannel.ProviderSecretKeyId = null;
            }

            if (ownedChannel.State != ChannelState.DisabledByOwner)
            {
                db.ChannelStateEvents.Add(new ChannelStateEvent
                {
                    Id = Guid.NewGuid(), ChannelId = ownedChannel.Id, FromState = ownedChannel.State,
                    ToState = ChannelState.DisabledByOwner, Reason = ChannelStateReason.DisconnectedByOwner,
                });
                ownedChannel.State = ChannelState.DisabledByOwner;
                ownedChannel.LastStateReason = ChannelStateReason.DisconnectedByOwner;
            }

            var channelPending = await db.OutboundNotifications
                .Where(n => n.ChannelId == ownedChannel.Id && n.Status == NotificationStatus.Pending)
                .ToListAsync();
            foreach (var row in channelPending)
            {
                row.Status = NotificationStatus.Cancelled;
                row.Reason = NotificationReason.BookingOrAssignmentCancelled;
            }

            // N7: previously left standing — a company (someone ELSE's company, this person only bought
            // the channel) stayed assigned to a channel that will never send again. Its settings screen
            // would keep showing "салон привязан к каналу" for a channel that's now dead, and any booking
            // event there would keep queuing Pending rows that just sit until they expire, instead of the
            // company being told up front there is no usable channel (§23.2's NoUsableChannel gate).
            var channelAssignments = await db.ChannelCompanyAssignments
                .Where(a => a.ChannelId == ownedChannel.Id).ToListAsync();
            db.ChannelCompanyAssignments.RemoveRange(channelAssignments);
        }

        // Step 5: company memberships are removed and Identity roles resynced — same lock this person's
        // OWN AddMember/RemoveMember calls would have taken, extended here to every company they belong
        // to (US-46, ARCHITECTURE.md §7.4 step 5).
        var membershipCompanyIds = await db.CompanyMembers
            .Where(cm => cm.UserId == userId).Select(cm => cm.CompanyId).Distinct().ToListAsync();
        foreach (var companyId in membershipCompanyIds)
            await AdvisoryLock.AcquireAsync(db, $"company-members:{companyId}");

        var memberships = await db.CompanyMembers.Where(cm => cm.UserId == userId).ToListAsync();
        db.CompanyMembers.RemoveRange(memberships);
        await db.SaveChangesAsync();
        await IdentityRoleSync.SyncAsync(db, userManager, userId);

        // Step 6: the account itself becomes a tombstone. Every personal-data field is scrubbed; login
        // is made impossible two ways at once (PasswordHash cleared AND a permanent lockout), and the
        // phone number is freed for reuse by clearing UserName/PhoneNumber (Identity's unique index is
        // on NormalizedUserName, so "deleted-{id}" being unique is what makes this safe to repeat).
        user.FirstName = "Удалённый";
        user.LastName = "пользователь";
        user.AvatarUrl = null;
        user.PasswordHash = null;
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        user.DeletedAtUtc = DateTime.UtcNow;
        await userManager.SetEmailAsync(user, null);
        await userManager.SetPhoneNumberAsync(user, null);
        await userManager.SetUserNameAsync(user, $"deleted-{user.Id}");
        // Rotates SecurityStamp, which is what actually invalidates every token issued before this
        // moment — Program.cs's OnTokenValidated compares a hash of it on every request (US-39 p.7).
        await userManager.UpdateSecurityStampAsync(user);

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        // Files only after the commit succeeds (ARCHITECTURE.md §1.4) — the worst outcome of a crash
        // between the two is an orphaned file, which photo-retention-cleanup sweeps up later; a DB row
        // pointing at a missing file is the state that must never happen.
        foreach (var (full, thumb) in photoKeysToDelete)
        {
            storage.DeletePrivate(full);
            storage.DeletePrivate(thumb);
        }
        storage.DeletePublic(oldAvatarUrl);

        return NoContent();
    }

    // US-25: any authenticated user replaces their OWN avatar — the id comes from the token, there is
    // no route parameter, so a caller can't reach anyone else's avatar through this endpoint (403 is
    // unreachable here, per API_CONTRACT.md §7). Public storage class: the avatar is meant to be seen by
    // anonymous visitors browsing the storefront, so it's served by UseStaticFiles like the logo, not
    // through the private client-photo pipeline (ARCHITECTURE.md §3.1). Does NOT count against the
    // client-photo quota (US-24) — that quota is about privacy-sensitive customer photos, not about a
    // one-file-per-user profile picture (US-25 p.8).
    [HttpPost("avatar")]
    [EnableRateLimiting("uploads")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ActionResult<ProfileDto>> UploadAvatar(IFormFile? file)
    {
        var user = await userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user is null) return NotFound();

        var validation = await imageUploadService.ReadAndProcessAsync(file, ImageProfile.Avatar);
        if (!validation.Success) return BadRequest(validation.ErrorMessage);

        // Order matters (ARCHITECTURE.md §1.4/§21.2, code review finding): write the new file, commit the
        // new URL, THEN delete the old file — never the other way round. Deleting the old file first
        // and having UpdateAsync fail afterwards would leave a live row pointing at a file that no
        // longer exists, which is the one state this pipeline must never produce; a leftover old file
        // is just an orphan the retention sweep would pick up, the acceptable failure mode.
        var oldUrl = user.AvatarUrl;
        var newUrl = await storage.SavePublicAsync(PublicArea.Avatars, validation.Image!.Bytes, validation.Image.Extension);
        user.AvatarUrl = newUrl;

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            storage.DeletePublic(newUrl); // the update didn't persist — don't leave the new file orphaned either
            return BadRequest(updateResult.Errors.FirstOrDefault()?.Description);
        }

        storage.DeletePublic(oldUrl); // old file removed on replace (US-25 p.5), only after the swap committed

        var roles = await userManager.GetRolesAsync(user);
        return Ok(await MapToDtoAsync(user, roles));
    }

    // Plan info is only meaningful for company owners. Money reads go through the owner's
    // BillingAccount (ARCHITECTURE_CYCLE7.md §45.1) — Company.OwnerUserId/AccountSubscriptions.OwnerUserId
    // stay a rights/visibility question, not a money one; the account is the single source for what's
    // actually owed and what's actually included (plan + grandfathered bonus + purchased options).
    // Shows the actual subscription row (including an expired/inactive one) rather than the normalized
    // "Free" fallback, so the owner can see WHY they're on the Free baseline.
    private async Task<ProfilePlanDto?> GetPlanInfoAsync(string userId, IList<string> roles)
    {
        if (!roles.Contains("CompanyOwner")) return null;

        var account = await db.BillingAccounts.FirstOrDefaultAsync(a => a.OwnerUserId == userId);
        if (account is null)
            return new ProfilePlanDto(
                "Free", 0, true, null, false, false, false, false, MaxEmployees: 1, MaxCompanies: 1,
                TotalMonthlyPrice: 0, Currency: "RUB", CompaniesUsed: 0, EmployeesUsed: 0,
                ExpiresInDays: null, IsExpiringSoon: false, OptionCount: 0);

        var now = DateTime.UtcNow;
        var sub = await db.AccountSubscriptions.Include(s => s.PlanConfig)
            .FirstOrDefaultAsync(s => s.BillingAccountId == account.Id);
        var plan = await subscriptionResolver.GetEffectivePlanForAccountAsync(account.Id);
        var usage = (await usageReader.GetAsync([account.Id])).GetValueOrDefault(account.Id) ?? new AccountUsage(account.Id, 0, 0);

        var subscribedOptions = await db.AccountSubscriptionOptions.Include(o => o.Option)
            .Where(o => o.BillingAccountId == account.Id)
            .Where(o => o.EndsAtUtc == null || o.EndsAtUtc > now)
            .ToListAsync();
        var planRules = sub?.PlanConfigId is { } planConfigId
            ? await db.PlanOptionRules.Where(r => r.PlanConfigId == planConfigId).ToListAsync()
            : [];

        decimal OptionMonthly(AccountSubscriptionOption row)
        {
            var rule = planRules.FirstOrDefault(r => r.OptionId == row.OptionId);
            // N14 — a missing rule reads as Unavailable everywhere else (SubscriptionResolver,
            // OwnerSubscriptionService, AdminBillingController); fail-open to Extra here would price a
            // gated-off option in the profile total while every other screen shows it as free/absent.
            var availability = rule?.Availability ?? OptionAvailability.Unavailable;
            var pricePerUnit = row.Option.PricePerMonth ?? 0m;
            return BillingCalculator.MonthlyPriceFor(availability, row.Quantity, pricePerUnit, rule?.IncludedQuantity);
        }

        var planPrice = sub?.PlanConfig?.PricePerMonth ?? 0m;
        var totalMonthlyPrice = BillingCalculator.TotalMonthlyPrice(planPrice, subscribedOptions.Select(OptionMonthly));

        var isExpired = sub is not null && sub.PaidUntil.HasValue && sub.PaidUntil.Value < now;
        var expiresInDays = BillingCalculator.ExpiresInDays(sub?.PaidUntil, now);
        var isExpiringSoon = sub?.PlanConfig is not null &&
            BillingCalculator.IsExpiringSoon(sub.PaidUntil, sub.PlanConfig.NotifyDaysBefore, now);

        if (sub is null || sub.PlanConfig is null)
            return new ProfilePlanDto(
                "Free", 0, true, null, false, false, false, false,
                MaxEmployees: plan.AccountMaxEmployees, MaxCompanies: plan.AccountMaxCompanies,
                TotalMonthlyPrice: totalMonthlyPrice, Currency: "RUB",
                CompaniesUsed: usage.CompaniesUsed, EmployeesUsed: usage.SeatsUsed,
                ExpiresInDays: expiresInDays, IsExpiringSoon: isExpiringSoon, OptionCount: subscribedOptions.Count);

        return new ProfilePlanDto(
            sub.PlanConfig.Name, sub.PlanConfig.PricePerMonth, sub.IsActive, sub.PaidUntil, isExpired,
            sub.PlanConfig.AllowOnlineBooking, sub.PlanConfig.AllowMailing, sub.PlanConfig.AllowAnalytics,
            // Deprecated but kept literal to its old meaning is not possible any more once options/
            // grandfathering exist (§53.2) — same widening the owner's own /billing/subscription screen
            // already uses; the UI's real limit comes from there, this field is display-only history.
            plan.AccountMaxEmployees, plan.AccountMaxCompanies,
            TotalMonthlyPrice: totalMonthlyPrice, Currency: "RUB",
            CompaniesUsed: usage.CompaniesUsed, EmployeesUsed: usage.SeatsUsed,
            ExpiresInDays: expiresInDays, IsExpiringSoon: isExpiringSoon, OptionCount: subscribedOptions.Count);
    }

    private async Task<ProfileDto> MapToDtoAsync(AppUser u, IList<string> roles)
    {
        // ARCHITECTURE_CYCLE14.md §149.1: the boolean comes from the mirror (cheap, already loaded with
        // the account — one indexed read either way, per profile request, not per row of a list); the
        // date comes from VerifiedPhone itself, since the mirror carries no timestamp of its own.
        DateTime? phoneVerifiedAtUtc = null;
        if (u.PhoneNumberConfirmed && u.PhoneNumber is not null)
            phoneVerifiedAtUtc = await db.VerifiedPhones.AsNoTracking()
                .Where(v => v.Phone == u.PhoneNumber)  // SUBJECT-PHONE-GATE: not-account-scoped — only ever reached when u.PhoneNumberConfirmed is already true (own mirror), displays only this account's own verification timestamp, never a guest-matched row (ARCHITECTURE_CYCLE16.md §245.3)
                .Select(v => (DateTime?)v.VerifiedAtUtc)
                .FirstOrDefaultAsync();

        return new(u.Id, u.PhoneNumber ?? "", u.Email, u.FirstName, u.LastName, u.AvatarUrl, [.. roles],
            await GetPlanInfoAsync(u.Id, roles), u.PhoneNumberConfirmed, phoneVerifiedAtUtc);
    }

    private static ExportConsentDto ToExportConsentDto(ConsentState s) =>
        new(s.Id, s.DocumentKey, s.DocumentVersion, s.Purpose?.ToString(), s.Act.ToString(), s.Source.ToString(),
            s.CompanyId, s.GrantedAtUtc, s.RevokedAtUtc, s.RevokeReason);

    // ── Consents (ARCHITECTURE_CYCLE5.md §41, API_CONTRACT_CYCLE5.md §41) ──────────────────────────
    //
    // All three are [Authorize] but allow-listed in LegalConsentFilter (a caller blocked by a pending
    // Privacy/TermsClient redaction must still be able to manage the ONE consent that is never itself a
    // reason to block, US-68 p.5). Every read/write here goes through ConsentLedger — this controller
    // never touches ConsentRecords directly, so "what counts as current" is answered in exactly one place.

    [HttpGet("consents")]
    public async Task<ActionResult<ConsentsDto>> GetConsents()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var doc = legalProvider.Current?.Get(LegalDocumentType.PdnConsent);
        if (doc is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Правовые документы временно недоступны.");

        return Ok(await BuildConsentsDtoAsync(userId, doc));
    }

    [HttpPost("consents")]
    public async Task<ActionResult<ConsentsDto>> PostConsents([FromBody] SubmitConsentDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        // §41.2: this endpoint accepts ONLY PdnConsent — Privacy/TermsClient/TermsOwner go through
        // POST /api/legal/accept, which is what keeps "recorded separately from other documents" a
        // protocol-level guarantee (ARCHITECTURE_CYCLE5.md §46.2) rather than a convention two different
        // request shapes could quietly drift away from.
        if (dto.DocumentKey != LegalDocumentType.PdnConsent.ToString())
            return BadRequest($"Через этот вызов принимается только '{LegalDocumentType.PdnConsent}'.");

        var doc = legalProvider.Current?.Get(LegalDocumentType.PdnConsent);
        if (doc is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Правовые документы временно недоступны.");

        if (dto.Version != doc.Version)
            return Conflict("Документ был обновлён ещё раз — перечитайте и подтвердите свой выбор заново.");

        var purposes = dto.Purposes ?? [];
        var parsedPurposes = new List<ConsentPurpose>();
        foreach (var key in purposes)
        {
            if (!Enum.TryParse<ConsentPurpose>(key, ignoreCase: true, out var purpose) || doc.Purposes.All(p => p.Key != purpose))
                return BadRequest($"Неизвестная цель '{key}'.");
            parsedPurposes.Add(purpose);
        }

        var subject = ConsentSubject.ForUser(userId);
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        if (parsedPurposes.Count == 0)
        {
            // A valid, deliberate answer: "shown the form, granted nothing" — one row with Purpose: null
            // records that the form was actually presented, distinct from never having called this
            // endpoint at all (API_CONTRACT_CYCLE5.md §41.2).
            await ledger.GrantAsync(new ConsentGrant(
                subject, doc.Type.ToString(), doc.Version, doc.ContentHash, Purpose: null,
                ConsentAct.Consented, ConsentSource.Profile, ipAddress, userAgent));
        }
        else
        {
            foreach (var purpose in parsedPurposes)
            {
                await ledger.GrantAsync(new ConsentGrant(
                    subject, doc.Type.ToString(), doc.Version, doc.ContentHash, purpose,
                    ConsentAct.Consented, ConsentSource.Profile, ipAddress, userAgent));
            }
        }

        return Ok(await BuildConsentsDtoAsync(userId, doc));
    }

    // T5-B5 (ARCHITECTURE_CYCLE5.md §47.1, API_CONTRACT_CYCLE5.md §41.3). Both endpoints share the SAME
    // selection queries (§49.2's "sухой прогон тем же запросом выборки" principle, applied here too,
    // even though this cascade isn't the retention sweep itself) — revoke-preview differs from revoke
    // only in whether SaveChanges/file deletion actually happen.

    [HttpPost("consents/revoke")]
    public async Task<ActionResult<RevokeConsentResponseDto>> RevokeConsent([FromBody] RevokeConsentDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // Code review В3: §44.4 describes a revoke mechanism for the SALON-recorded PhotoConsent/
        // HealthDataConsent rows too (ClientConsentsController's PostPhotoConsent/PostHealthConsent) —
        // until now this endpoint only ever accepted PdnConsent, so a person had no way to withdraw a
        // consent a company's staff recorded on their behalf. These two are phone+company scoped
        // (ConsentSubject.ForPhoneInCompany), not user scoped, so a companyId is required to say WHICH
        // salon's record is being withdrawn — deliberately a separate branch from PdnConsent below
        // rather than one that silently reinterprets `purpose`, which has no meaning for these two keys.
        if (dto.DocumentKey is LegalTextKey.PhotoConsent or LegalTextKey.HealthDataConsent)
            return await RevokeSalonConsentAsync(userId, dto);

        if (dto.DocumentKey != LegalDocumentType.PdnConsent.ToString())
            return BadRequest($"Через этот вызов отзывается только '{LegalDocumentType.PdnConsent}', " +
                               $"'{LegalTextKey.PhotoConsent}' или '{LegalTextKey.HealthDataConsent}'.");

        ConsentPurpose? purpose = null;
        if (dto.Purpose is not null)
        {
            if (!Enum.TryParse<ConsentPurpose>(dto.Purpose, ignoreCase: true, out var parsed))
                return BadRequest($"Неизвестная цель '{dto.Purpose}'.");
            purpose = parsed;
        }

        var subject = ConsentSubject.ForUser(userId);

        // §47.3: idempotent — "0 revoked" is a legitimate, non-error outcome (revoking an already-revoked
        // or never-granted purpose), and the cascade below still runs against whatever it finds (which,
        // for an already-clean subject, is nothing — also legitimate, not an error).
        var revoked = await ledger.RevokeAsync(subject, dto.DocumentKey, purpose, dto.Reason);
        var effects = await ApplyOrPreviewRevokeEffectsAsync(userId, purpose, apply: true);

        return Ok(new RevokeConsentResponseDto(revoked, effects));
    }

    /// <summary>The salon-scoped half of RevokeConsent (code review В3) — withdraws a PhotoConsent or
    /// HealthDataConsent recorded by ONE company's staff, and (§44.4: "удаляет фото необратимо" / mirrors
    /// PutHealthNote's own consent-gated write) deletes whatever that consent was covering AT THAT
    /// COMPANY specifically, never account-wide (unlike the PdnConsent WorkPhotos/HealthData purposes,
    /// which are account-wide by construction).</summary>
    private async Task<ActionResult<RevokeConsentResponseDto>> RevokeSalonConsentAsync(string userId, RevokeConsentDto dto)
    {
        if (dto.CompanyId is null || dto.CompanyId == Guid.Empty)
            return BadRequest("Для отзыва согласия, записанного сотрудником компании, укажите companyId.");

        var phone = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.PhoneNumber).FirstOrDefaultAsync();
        // Н2 (LEGAL_REVIEW_CYCLE16.md §3.4): прежняя строка обещала проверку подтверждения там, где
        // проверялось только НАЛИЧИЕ номера. Теперь два случая вместо одного; формулировки — юриста,
        // команда их не сочиняет. Ни одна не сообщает, существуют ли данные (§245.6 п. 1, правило
        // «ответ не оракул»).
        if (string.IsNullOrEmpty(phone))
            return BadRequest("У учётной записи не указан номер телефона, поэтому отзывать нечего.");

        // TD-03 (ARCHITECTURE_CYCLE16.md §245.2 row 3 — a place the spec itself did not name, found
        // while reading the code). Before this gate, an unverified account could destroy a COMPLETE
        // STRANGER's photos/health notes at any company just by knowing that company's id and typing
        // its own (unproven) phone number. The ledger consent revoke itself stays phone-scoped as
        // before (it is not a destructive read/write of someone else's ClientNote/ClientHealthNote
        // rows); only the two GuestPhone-matched deletions below are gated.
        var user = await userManager.FindByIdAsync(userId);
        var scope = user is null
            ? new SubjectScope(userId, phone, null)
            : await subjectScopeResolver.ForAccountAsync(user, HttpContext.RequestAborted);
        var guestMatchPhone = scope.GuestMatchPhone; // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4

        if (scope.GateApplied)
        {
            // §245.7: name and endpoint only, no phone, no counts (NFT §7.4).
            logger.LogInformation("guest-data gate applied: userId={UserId} endpoint={Endpoint}", userId, "profile/consents/revoke");
        }

        // 🔴 TD-03-ter (LEGAL_REVIEW_CYCLE16.md находка Н1). Запись в ConsentLedger гейтится ТОЖЕ, а не
        // только удаления ниже. Отзыв чужого согласия — нарушение сам по себе (ч. 3 ст. 9 152-ФЗ), даже
        // когда ни одна строка данных при этом не удалена: согласие субъекта оказывается помечено
        // отозванным по воле постороннего. Ранняя версия цикла 16 оставила этот вызов открытым,
        // рассудив, что он «не разрушительный», — это неверная посылка, и её ловит
        // GuestDataGateCycle16Tests.RevokeSalonConsent_UnconfirmedPhone_CannotRevokeAStranger_SConsent.
        // Свойство обязано держаться ЭТОЙ проверкой, а не порядком строк выше (ранний BadRequest на
        // отсутствующем телефоне спасал лишь случайно и сломался бы при первой перестановке).
        if (guestMatchPhone is null)  // SUBJECT-PHONE-GATE: gated — TD-03-ter, ARCHITECTURE_CYCLE16.md §245.4
            return BadRequest(
                "Это согласие записано на номер телефона, а не на учётную запись. Отозвать его отсюда " +
                "можно после подтверждения номера; если подтвердить номер невозможно, направьте отзыв " +
                "через форму обращения.");

        var subject = ConsentSubject.ForPhoneInCompany(guestMatchPhone, dto.CompanyId.Value);
        var revoked = await ledger.RevokeAsync(subject, dto.DocumentKey, purpose: null, dto.Reason);

        var photosDeleted = 0;
        var healthNotesDeleted = 0;
        if (dto.DocumentKey == LegalTextKey.PhotoConsent)
        {
            var photos = await db.ClientNotePhotos.Include(p => p.ClientNote)
                .Where(p => p.CompanyId == dto.CompanyId
                            && (p.ClientNote.ClientId == userId || (guestMatchPhone != null && p.ClientNote.GuestPhone == guestMatchPhone)))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
                .ToListAsync();
            photosDeleted = photos.Count;
            if (photos.Count > 0)
            {
                var paths = photos.Select(p => (p.StoragePath, p.ThumbnailPath)).ToList();
                db.ClientNotePhotos.RemoveRange(photos);
                await db.SaveChangesAsync();
                foreach (var (full, thumb) in paths)
                {
                    storage.DeletePrivate(full);
                    storage.DeletePrivate(thumb);
                }
            }
        }
        else // HealthDataConsent
        {
            var healthNotes = await db.ClientHealthNotes
                .Where(n => n.CompanyId == dto.CompanyId && (n.ClientId == userId || (guestMatchPhone != null && n.GuestPhone == guestMatchPhone)))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
                .ToListAsync();
            healthNotesDeleted = healthNotes.Count;
            if (healthNotes.Count > 0)
            {
                db.ClientHealthNotes.RemoveRange(healthNotes);
                await db.SaveChangesAsync();
            }
        }

        var effects = new RevokeEffectsDto(photosDeleted, healthNotesDeleted, [], 0);
        return Ok(new RevokeConsentResponseDto(revoked, effects));
    }

    [HttpGet("consents/revoke-preview")]
    public async Task<ActionResult<RevokeEffectsDto>> RevokeConsentPreview([FromQuery] string? purpose)
    {
        ConsentPurpose? parsedPurpose = null;
        if (purpose is not null)
        {
            if (!Enum.TryParse<ConsentPurpose>(purpose, ignoreCase: true, out var parsed))
                return BadRequest($"Неизвестная цель '{purpose}'.");
            parsedPurpose = parsed;
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var effects = await ApplyOrPreviewRevokeEffectsAsync(userId, parsedPurpose, apply: false);
        return Ok(effects);
    }

    /// <summary>
    /// The cascade table from ARCHITECTURE_CYCLE5.md §47.1, computed once for both the real revoke and
    /// its preview. `purpose: null` means "the whole PdnConsent document" — every cascade below applies,
    /// plus the optional profile fields (§47.1's fourth row). A specific purpose applies only its own row.
    /// </summary>
    private async Task<RevokeEffectsDto> ApplyOrPreviewRevokeEffectsAsync(string userId, ConsentPurpose? purpose, bool apply)
    {
        var wholeDocument = purpose is null;
        var photosDeleted = 0;
        var healthNotesDeleted = 0;
        var queuedNotificationsCancelled = 0;
        var profileFieldsCleared = new List<string>();
        // TD-03 (ARCHITECTURE_CYCLE16.md §245.2 row 4 — a place the spec itself did not name). Before
        // this gate an unverified account could both DESTROY a stranger's health notes (apply: true)
        // AND, via the preview endpoint, learn their COUNT without deleting anything — a double
        // violation of §272/A2 ("not an oracle"). Only the health-notes branch below reads a phone at
        // all; the WorkPhotos branch above is ClientId-only by construction and needs no gate.
        var accountForScope = await userManager.FindByIdAsync(userId);
        var scope = accountForScope is null
            ? new SubjectScope(userId, null, null)
            : await subjectScopeResolver.ForAccountAsync(accountForScope, HttpContext.RequestAborted);
        var guestMatchPhone = scope.GuestMatchPhone; // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4

        if (scope.GateApplied)
        {
            // §245.7: name and endpoint only, no phone, no counts (NFT §7.4).
            logger.LogInformation(
                "guest-data gate applied: userId={UserId} endpoint={Endpoint}", userId,
                apply ? "profile/consents/revoke" : "profile/consents/revoke-preview");
        }

        // Code review, "заодно": the four sections below used to run as four independent SaveChangesAsync
        // calls with no shared transaction — a crash between any two of them left a partially-applied
        // revoke (e.g. photos gone but the pending-notification cancellation never happened). One
        // transaction for the whole apply pass, same pattern DeleteAccount already uses to mix
        // db.SaveChangesAsync and userManager calls (both go through this same AppDbContext/connection).
        // Preview (apply == false) writes nothing at all, so it opens no transaction.
        await using var transaction = apply ? await db.Database.BeginTransactionAsync() : null;
        // Files (photo pair + avatar) are collected here and only actually deleted AFTER the transaction
        // below commits (ARCHITECTURE.md §1.4: "row goes first") — now that all four sections share one
        // transaction, deleting a file mid-pass (as an earlier version did, right after that section's
        // own SaveChanges) would leave an orphaned-on-disk file with no row if a LATER section made the
        // whole transaction roll back.
        var photoPathsToDelete = new List<(string Full, string Thumb)>();
        string? avatarUrlToDelete = null;

        if (wholeDocument || purpose == ConsentPurpose.WorkPhotos)
        {
            // "About the user" — every photo on a note filed against THEIR account, in any company
            // (§47.1's "во всех компаниях"), never a guest-path note (that has no ClientId to match).
            var photos = await db.ClientNotePhotos.Include(p => p.ClientNote)
                .Where(p => p.ClientNote.ClientId == userId).ToListAsync();
            photosDeleted = photos.Count;
            if (apply && photos.Count > 0)
            {
                photoPathsToDelete.AddRange(photos.Select(p => (p.StoragePath, p.ThumbnailPath)));
                db.ClientNotePhotos.RemoveRange(photos);
                await db.SaveChangesAsync();
            }
        }

        if (wholeDocument || purpose == ConsentPurpose.HealthData)
        {
            var healthNotes = await db.ClientHealthNotes
                .Where(n => n.ClientId == userId || (guestMatchPhone != null && n.GuestPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — TD-03, ARCHITECTURE_CYCLE16.md §245.4
                .ToListAsync();
            healthNotesDeleted = healthNotes.Count;
            if (apply && healthNotes.Count > 0)
            {
                db.ClientHealthNotes.RemoveRange(healthNotes);
                await db.SaveChangesAsync();
            }
        }

        if (wholeDocument || purpose == ConsentPurpose.ProviderDelivery)
        {
            // §47.1: not skipped silently at send time — CANCELLED now, with a reason, so the delivery
            // log shows why (§55.1 R11's "новая ветка гейта" is the mirror of this: NEW rows stop being
            // queued at all via NotificationGate/T-24, ARCHITECTURE_CYCLE5.md §52.3).
            var pending = await db.OutboundNotifications
                .Where(n => n.RecipientUserId == userId && n.Status == NotificationStatus.Pending).ToListAsync();
            queuedNotificationsCancelled = pending.Count;
            if (apply && pending.Count > 0)
            {
                foreach (var row in pending)
                {
                    row.Status = NotificationStatus.Cancelled;
                    row.Reason = NotificationReason.NoProviderDeliveryConsent;
                }
                await db.SaveChangesAsync();
            }
        }

        if (wholeDocument)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user is not null)
            {
                if (!string.IsNullOrEmpty(user.Email)) profileFieldsCleared.Add("email");
                if (!string.IsNullOrEmpty(user.AvatarUrl)) profileFieldsCleared.Add("avatarUrl");
                if (apply && profileFieldsCleared.Count > 0)
                {
                    avatarUrlToDelete = user.AvatarUrl;
                    user.Email = null;
                    user.AvatarUrl = null;
                    await userManager.UpdateAsync(user);
                }
            }
        }

        if (transaction is not null) await transaction.CommitAsync();

        // Only after the commit succeeds (ARCHITECTURE.md §1.4) — see the comment above the transaction.
        foreach (var (full, thumb) in photoPathsToDelete)
        {
            storage.DeletePrivate(full);
            storage.DeletePrivate(thumb);
        }
        if (avatarUrlToDelete is not null) storage.DeletePublic(avatarUrlToDelete);

        return new RevokeEffectsDto(photosDeleted, healthNotesDeleted, profileFieldsCleared, queuedNotificationsCancelled);
    }

    private async Task<ConsentsDto> BuildConsentsDtoAsync(string userId, LegalDocument pdnDoc)
    {
        var subject = ConsentSubject.ForUser(userId);
        // knownPhone (code review В3): "Мои согласия" must also show salon-recorded PhotoConsent/
        // HealthDataConsent rows — see ConsentLedger.HistoryAsync's doc comment.
        var knownPhone = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.PhoneNumber).FirstOrDefaultAsync();
        var history = await ledger.HistoryAsync(subject, knownPhone);

        // "granted" is the latest row per purpose UNDER PdnConsent specifically, revoked or not — a
        // revoked purpose still needs to show up (with revokedAt set) so the profile screen can render
        // it as "revoked" rather than making it look like it was never granted (API_CONTRACT_CYCLE5.md
        // §41.1). Deliberately NOT ConsentLedger.CurrentAllAsync, which only ever returns non-revoked
        // rows — that method answers a different question ("what currently applies"), used for gating
        // decisions elsewhere, not for this audit-style view.
        var granted = history
            .Where(s => s.DocumentKey == LegalDocumentType.PdnConsent.ToString() && s.Purpose is not null)
            .GroupBy(s => s.Purpose)
            .Select(g => g.First()) // history is already newest-first
            .OrderBy(s => s.GrantedAtUtc)
            .Select(s => new ConsentGrantedDto(s.Purpose!.Value.ToString(), s.DocumentVersion, s.GrantedAtUtc, s.RevokedAtUtc))
            .ToList();

        var versionOutdated = granted.Any(g => g.RevokedAt is null && g.Version != pdnDoc.Version);

        var documentDto = new ConsentDocumentDto(
            pdnDoc.Type.ToString(), pdnDoc.Version, pdnDoc.IsDraft,
            pdnDoc.Purposes.Select(p => new LegalPurposeDto(p.Key.ToString(), p.Title)).ToList());

        return new ConsentsDto(documentDto, granted, versionOutdated, history.Select(ToExportConsentDto).ToList());
    }
}

// CommissionPercent removed (US-22): commission became per-company (CompanyMember.CommissionPercent)
// in cycle A, so the account-level value here no longer means anything and the UI never showed it.
// ARCHITECTURE_CYCLE14.md §149.1, API_CONTRACT_CYCLE14.md §170.1: PhoneVerified/PhoneVerifiedAtUtc are
// ADDITIVE (US-14-14). PhoneVerifiedAtUtc is null whenever PhoneVerified is false.
public record ProfileDto(string Id, string Phone, string? Email, string FirstName, string LastName, string? AvatarUrl,
    List<string> Roles, ProfilePlanDto? Plan, bool PhoneVerified, DateTime? PhoneVerifiedAtUtc);

// §53.2 — only additive over the cycle-4 shape: every existing field keeps its old meaning (subscription
// is bound to the account, not to a person, but that is invisible here), the six new fields below are
// appended. MaxEmployees/MaxCompanies stay deprecated (same note as /billing/subscription) — a UI
// should read limits from there, not from here.
public record ProfilePlanDto(
    string PlanName, decimal PricePerMonth, bool IsActive, DateTime? PaidUntil, bool IsExpired,
    bool AllowOnlineBooking, bool AllowMailing, bool AllowAnalytics, int? MaxEmployees, int? MaxCompanies,
    decimal TotalMonthlyPrice, string Currency, int CompaniesUsed, int EmployeesUsed,
    int? ExpiresInDays, bool IsExpiringSoon, int OptionCount);

// API_CONTRACT_CYCLE5.md §41 — GET/POST /api/profile/consents. LegalPurposeDto is the same record
// LegalController's GET /api/legal/documents uses (same namespace) — one shape for "what a purpose is",
// so the frontend never has to reconcile two slightly different purpose objects from two endpoints.
public record ConsentDocumentDto(string Type, string Version, bool IsDraft, List<LegalPurposeDto> Purposes);
public record ConsentGrantedDto(string Purpose, string Version, DateTime GrantedAt, DateTime? RevokedAt);
public record ConsentsDto(ConsentDocumentDto Document, List<ConsentGrantedDto> Granted, bool VersionOutdated, List<ExportConsentDto> History);
public record SubmitConsentDto(string DocumentKey, string Version, List<string>? Purposes);
// CompanyId appended (code review В3): required only for the salon-scoped DocumentKeys
// (LegalTextKey.PhotoConsent/HealthDataConsent) — default null keeps every existing PdnConsent caller
// compiling and behaving exactly as before.
public record RevokeConsentDto(string DocumentKey, string? Purpose, string? Reason, Guid? CompanyId = null);
public record RevokeEffectsDto(int PhotosDeleted, int HealthNotesDeleted, List<string> ProfileFieldsCleared, int QueuedNotificationsCancelled);
public record RevokeConsentResponseDto(int Revoked, RevokeEffectsDto Effects);

public record UpdateProfileDto(string FirstName, string LastName);
public record ChangePasswordDto(string CurrentPassword, string NewPassword);
// ARCHITECTURE_CYCLE14.md §148.5, API_CONTRACT_CYCLE14.md §169: Verification is ADDITIVE and OPTIONAL —
// required ONLY when the new number already has guest bookings on it (checked server-side, never
// assumed from the presence of this field).
public record ChangePhoneDto(string CurrentPassword, string NewPhone, PhoneVerificationRefDto? Verification = null);
public record DeleteAccountDto(string CurrentPassword);

// US-38 export DTOs (API_CONTRACT.md §8) — deliberately their own shape, not a reuse of ProfileDto/
// BookingDto/etc.: the export is a legal artifact with its own contract (no ids of other people's
// entities, no hashes), and coupling it to DTOs used elsewhere would mean a change made for an
// unrelated screen could silently change what leaves the product in a data export.
// CYCLE5-BREAKING: `Consent`→`Consents` and `Notice`→`Explanation` to match API_CONTRACT_CYCLE5.md §49's
// field names exactly (cycle 3 used different names for the same two fields); four sections added
// (T5-B11, §50.2): `Operators` (which companies hold data about this subject — the routing §50.2
// replaces "результат работы салона" with), `Notifications`/`OptOut` (US-75), `HealthNotes` (US-77).
// ARCHITECTURE_CYCLE14.md §151.3: PhoneVerification is the one section this cycle adds — additive,
// appended last, same convention as T5-B11's own additions above it.
// ARCHITECTURE_CYCLE16.md §245.6, API_CONTRACT_CYCLE16.md §273.1: GuestDataGate is cycle 16's one
// additive section — appended last again, same convention.
public record ProfileExportDto(
    DateTime GeneratedAt, ExportProfileDto Profile, List<ExportConsentDto> Consents,
    List<ExportMembershipDto> Memberships, List<ExportBookingDto> Bookings, List<ExportReviewDto> Reviews,
    List<ExportNoteMetaDto> NotesAboutMe, List<ExportPhotoMetaDto> PhotosOfMe, string Explanation,
    List<ExportOperatorDto> Operators, List<ExportNotificationDto> Notifications, ExportOptOutDto OptOut,
    List<ExportHealthNoteDto> HealthNotes, ExportPhoneVerificationDto PhoneVerification,
    ExportGuestDataGateDto GuestDataGate);

// API_CONTRACT_CYCLE16.md §273.1. §272/A2: deliberately carries no count or "hasHiddenData" flag — only
// whether the rule applied, so the response can never be used to infer whether hidden data exists.
public record ExportGuestDataGateDto(bool Applied, string? Reason, string? Explanation, string? SubjectRequestPath);

public record ExportPhoneVerificationDto(bool PhoneVerified, string? Method, DateTime? VerifiedAtUtc);

public record ExportOperatorDto(Guid CompanyId, string Name, string? Address, string? Phone, string? Email, List<string> WhatIsStored);
public record ExportNotificationDto(DateTime? SentAt, string Type, string Status, string CompanyName, bool BodyAvailable);
public record ExportOptOutDto(bool OptedOut, DateTime? OptedOutAt);
public record ExportHealthNoteDto(string CompanyName, string? Value, DateTime UpdatedAt);

public record ExportProfileDto(string FirstName, string LastName, string? Phone, string? Email, string? AvatarUrl, DateTime CreatedAt);

// CYCLE5-BREAKING: was (Document, Version, AcceptedAt) — a single "current state" triple, matching
// cycle 3's UserConsent. Now the full §38.4 journal-row shape: the export shows EVERY consent event,
// including superseded/revoked ones, not just a snapshot of the latest (ARCHITECTURE_CYCLE5.md §44.2).
public record ExportConsentDto(
    Guid Id, string DocumentKey, string DocumentVersion, string? Purpose, string Act, string Source,
    Guid? CompanyId, DateTime GrantedAt, DateTime? RevokedAt, string? RevokeReason);

public record ExportMembershipDto(string Company, string Role, DateTime JoinedAt);

public record ExportBookingDto(
    Guid Id, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, string Company, string Service, string Master,
    string Status, string PaymentStatus, decimal Price, string? CancellationReason);

public record ExportReviewDto(string Company, int Rating, string? Comment, DateTime CreatedAt);
public record ExportNoteMetaDto(string Company, DateTime CreatedAt, int PhotoCount);
public record ExportPhotoMetaDto(string Company, DateTime CreatedAt, long SizeBytes);
