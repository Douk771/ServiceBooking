using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileController(
    UserManager<AppUser> userManager, AppDbContext db, ImageUploadService imageUploadService, FileStorage storage)
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

        var consent = await db.UserConsents
            .Where(c => c.UserId == userId)
            .Select(c => new ExportConsentDto(c.DocumentType.ToString(), c.Version, c.AcceptedAtUtc))
            .ToListAsync();

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
        var canonicalPhone = user.PhoneNumber;
        var bookings = await db.Bookings
            .Include(b => b.Service).Include(b => b.Master).Include(b => b.Company)
            .Where(b => b.ClientId == userId || (canonicalPhone != null && b.GuestPhone == canonicalPhone))
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
            .Where(n => n.ClientId == userId || (canonicalPhone != null && n.GuestPhone == canonicalPhone))
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new ExportNoteMetaDto(n.Company.Name, n.CreatedAt, n.Photos.Count))
            .ToListAsync();

        var photosOfMe = await db.ClientNotePhotos
            .Include(p => p.ClientNote).ThenInclude(n => n.Company)
            .Where(p => p.ClientNote.ClientId == userId || (canonicalPhone != null && p.ClientNote.GuestPhone == canonicalPhone))
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ExportPhotoMetaDto(p.ClientNote.Company.Name, p.CreatedAt, p.SizeBytes))
            .ToListAsync();

        var export = new ProfileExportDto(
            DateTime.UtcNow,
            new ExportProfileDto(user.FirstName, user.LastName, user.PhoneNumber, user.Email, user.AvatarUrl, user.CreatedAt),
            consent, memberships, bookings, reviews, notesAboutMe, photosOfMe,
            "В файл не входят: текст заметок сотрудников салона о вас и содержимое фотографий, " +
            "загруженных салоном. Эти материалы принадлежат салону как результат его работы; запросить " +
            "их можно у салона напрямую. По вопросам обработки ваших данных платформой обращайтесь в " +
            "поддержку сервиса.");

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
    // Deferred (code review, cycle C): the new number is NOT verified — only the current password is
    // checked, and PhoneNumberConfirmed is never consulted. Combined with Export/DeleteAccount
    // matching guest bookings by canonical phone, that lets someone move their account onto a number
    // a guest once booked with and read that guest's visit history. Accepted knowingly; the fix is
    // an SMS confirmation of the new number, which waits on the messaging channel — see
    // SPEC_DEFERRED_NOTIFICATIONS.md "Отложенное, связанное с этой темой".
    [HttpPost("change-phone")]
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

        if (user.PhoneNumber != canonicalPhone)
        {
            user.PhoneNumber = canonicalPhone;
            var result = await userManager.SetUserNameAsync(user, canonicalPhone);
            if (!result.Succeeded)
            {
                var isDuplicate = result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.DuplicateUserName));
                return BadRequest(isDuplicate
                    ? "Этот номер телефона уже используется другим аккаунтом"
                    : result.Errors.FirstOrDefault()?.Description ?? "Не удалось изменить номер телефона");
            }
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

        // Captured before any field on `user` is scrubbed below — needed to find guest-path bookings/
        // notes recorded under this phone before the account existed (same match MastersController.
        // GetClients and the export endpoint use), and to clean up the old avatar file after commit.
        var canonicalPhone = user.PhoneNumber;
        var oldAvatarUrl = user.AvatarUrl;

        await using var transaction = await db.Database.BeginTransactionAsync();

        // Step 1: consent records are this person's own data about their own acceptance — deleted
        // outright, unlike everything below which is either company data (kept) or this person's
        // participation in company data (anonymized, not erased).
        var consents = await db.UserConsents.Where(c => c.UserId == userId).ToListAsync();
        db.UserConsents.RemoveRange(consents);

        // Step 2: notes ABOUT this person (by ClientId, or by guest phone for pre-registration visits) —
        // gather photo storage keys before the cascade delete removes the ClientNotePhoto rows, since
        // the files themselves live outside the database (ARCHITECTURE.md §1.4: DB row goes first, file
        // cleanup happens only after a successful commit).
        var notesAboutMe = await db.ClientNotes
            .Include(n => n.Photos)
            .Where(n => n.ClientId == userId || (canonicalPhone != null && n.GuestPhone == canonicalPhone))
            .ToListAsync();
        var photoKeysToDelete = notesAboutMe.SelectMany(n => n.Photos)
            .Select(p => (p.StoragePath, p.ThumbnailPath)).ToList();
        db.ClientNotes.RemoveRange(notesAboutMe); // cascades ClientNotePhotos (AppDbContext)

        // Step 3: bookings are anonymized, never deleted — the salon's revenue/commission history for a
        // completed visit must stay intact (US-39 p.3). Matches both the client path and the guest path
        // (a booking made before this person registered, found the same way as step 2's notes).
        var bookingsToAnonymize = await db.Bookings
            .Where(b => b.ClientId == userId || (canonicalPhone != null && b.GuestPhone == canonicalPhone))
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
            .Where(n => n.RecipientUserId == userId || (canonicalPhone != null && n.RecipientPhone == canonicalPhone))
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

    // Plan info is only meaningful for company owners — subscriptions are bound to the owner account
    // (see SubscriptionResolver). Shows the actual subscription row (including an expired/inactive one)
    // rather than the normalized "Free" fallback, so the owner can see WHY they're on the Free baseline.
    private async Task<ProfilePlanDto?> GetPlanInfoAsync(string userId, IList<string> roles)
    {
        if (!roles.Contains("CompanyOwner")) return null;

        var sub = await db.AccountSubscriptions.Include(s => s.PlanConfig)
            .FirstOrDefaultAsync(s => s.OwnerUserId == userId);

        if (sub is null || sub.PlanConfig is null)
            return new ProfilePlanDto("Free", 0, true, null, false, false, false, false, MaxEmployees: 1, MaxCompanies: 1);

        var isExpired = sub.PaidUntil.HasValue && sub.PaidUntil.Value < DateTime.UtcNow;
        return new ProfilePlanDto(
            sub.PlanConfig.Name, sub.PlanConfig.PricePerMonth, sub.IsActive, sub.PaidUntil, isExpired,
            sub.PlanConfig.AllowOnlineBooking, sub.PlanConfig.AllowMailing, sub.PlanConfig.AllowAnalytics,
            sub.PlanConfig.MaxEmployees, sub.PlanConfig.MaxCompanies);
    }

    private async Task<ProfileDto> MapToDtoAsync(AppUser u, IList<string> roles) =>
        new(u.Id, u.PhoneNumber ?? "", u.Email, u.FirstName, u.LastName, u.AvatarUrl, [.. roles],
            await GetPlanInfoAsync(u.Id, roles));
}

// CommissionPercent removed (US-22): commission became per-company (CompanyMember.CommissionPercent)
// in cycle A, so the account-level value here no longer means anything and the UI never showed it.
public record ProfileDto(string Id, string Phone, string? Email, string FirstName, string LastName, string? AvatarUrl,
    List<string> Roles, ProfilePlanDto? Plan);

public record ProfilePlanDto(
    string PlanName, decimal PricePerMonth, bool IsActive, DateTime? PaidUntil, bool IsExpired,
    bool AllowOnlineBooking, bool AllowMailing, bool AllowAnalytics, int? MaxEmployees, int? MaxCompanies);

public record UpdateProfileDto(string FirstName, string LastName);
public record ChangePasswordDto(string CurrentPassword, string NewPassword);
public record ChangePhoneDto(string CurrentPassword, string NewPhone);
public record DeleteAccountDto(string CurrentPassword);

// US-38 export DTOs (API_CONTRACT.md §8) — deliberately their own shape, not a reuse of ProfileDto/
// BookingDto/etc.: the export is a legal artifact with its own contract (no ids of other people's
// entities, no hashes), and coupling it to DTOs used elsewhere would mean a change made for an
// unrelated screen could silently change what leaves the product in a data export.
public record ProfileExportDto(
    DateTime GeneratedAt, ExportProfileDto Profile, List<ExportConsentDto> Consent,
    List<ExportMembershipDto> Memberships, List<ExportBookingDto> Bookings, List<ExportReviewDto> Reviews,
    List<ExportNoteMetaDto> NotesAboutMe, List<ExportPhotoMetaDto> PhotosOfMe, string Notice);

public record ExportProfileDto(string FirstName, string LastName, string? Phone, string? Email, string? AvatarUrl, DateTime CreatedAt);
public record ExportConsentDto(string Document, string Version, DateTime AcceptedAt);
public record ExportMembershipDto(string Company, string Role, DateTime JoinedAt);

public record ExportBookingDto(
    Guid Id, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, string Company, string Service, string Master,
    string Status, string PaymentStatus, decimal Price, string? CancellationReason);

public record ExportReviewDto(string Company, int Rating, string? Comment, DateTime CreatedAt);
public record ExportNoteMetaDto(string Company, DateTime CreatedAt, int PhotoCount);
public record ExportPhotoMetaDto(string Company, DateTime CreatedAt, long SizeBytes);
