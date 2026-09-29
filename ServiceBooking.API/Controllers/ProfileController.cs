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
    GuestBookingLookup guestBookingLookup, IPhoneVerificationMethodRegistry phoneVerificationRegistry,
    PhoneVerificationWriter phoneVerificationWriter, IOptions<PhoneVerificationOptions> phoneVerificationOptions,
    ILogger<ProfileController> logger,
    SubjectDataExporter subjectDataExporter, AccountDeletionService accountDeletionService)
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
    // Cycle 22 P5 (§378): the body lives in SubjectDataExporter, unchanged.
    [HttpGet("export")]
    [EnableRateLimiting("data-export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var export = await subjectDataExporter.ExportAsync(userId, HttpContext.TraceIdentifier, HttpContext.RequestAborted, ct);
        if (export is null) return NotFound();

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

    // Cycle 22 P5 (§378): both bodies live in AccountDeletionService, unchanged.
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
    // Cycle 18 (API_CONTRACT_CYCLE18.md §377, О8) — shown BEFORE the destructive POST below, so a
    // subject who was ever granted a trial learns that the trial-registry entry survives account
    // deletion. Allow-listed in LegalConsentFilter alongside POST delete-account itself (§360.2):
    // the right to leave the service cannot be gated behind accepting a new legal-document revision.
    [HttpGet("delete-account/preview")]
    public async Task<ActionResult<AccountDeletionPreviewDto>> DeleteAccountPreview(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        return Ok(await accountDeletionService.PreviewAsync(userId, ct));
    }

    [HttpPost("delete-account")]
    public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await accountDeletionService.DeleteAsync(userId, dto.CurrentPassword, HttpContext.TraceIdentifier, HttpContext.RequestAborted);
        return result.Status switch
        {
            AccountDeletionStatus.UserNotFound => NotFound(),
            AccountDeletionStatus.WrongPassword => BadRequest(result.Message),
            AccountDeletionStatus.OwnsCompany => Conflict(result.Message),
            _ => NoContent(),
        };
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

        // ARCHITECTURE_CYCLE19.md §386.1/§410 — OptionCount/totalMonthlyPrice below exclude retired
        // limit options.
        var subscribedOptions = await db.AccountSubscriptionOptions.WhereNotRetired().Include(o => o.Option)
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

public record UpdateProfileDto(string FirstName, string LastName);
public record ChangePasswordDto(string CurrentPassword, string NewPassword);
// ARCHITECTURE_CYCLE14.md §148.5, API_CONTRACT_CYCLE14.md §169: Verification is ADDITIVE and OPTIONAL —
// required ONLY when the new number already has guest bookings on it (checked server-side, never
// assumed from the presence of this field).
public record ChangePhoneDto(string CurrentPassword, string NewPhone, PhoneVerificationRefDto? Verification = null);
public record DeleteAccountDto(string CurrentPassword);

/// <summary>Cycle 18 (§377) — object, not a bare string, on purpose: future cycles are expected to add
/// more pre-deletion notices of the same kind, and adding a field here is not a breaking change.</summary>
public record AccountDeletionPreviewDto(string? TrialRegistryNotice);

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
    ExportGuestDataGateDto GuestDataGate,
    // ARCHITECTURE_CYCLE23.md §398.1 — additive, appended at the end: the customer's orders (own account's, plus guest orders on the
    // same VERIFIED number under the cycle-16 SubjectScope gate).
    List<ServiceBooking.API.DTOs.Orders.ExportOrderDto> Orders);

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
