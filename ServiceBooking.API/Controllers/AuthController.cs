using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.PhoneVerification;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    TokenService tokenService,
    LegalDocumentProvider legalProvider,
    ConsentLedger ledger,
    AppDbContext db,
    IPhoneVerificationMethodRegistry phoneVerificationRegistry,
    PhoneVerificationWriter phoneVerificationWriter,
    IOptions<PhoneVerificationOptions> phoneVerificationOptions,
    ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("register")]
    [EnableRateLimiting("auth-register")]
    public async Task<ActionResult<AuthResponseDto>> Register(RegisterDto dto)
    {
        // ARCHITECTURE_CYCLE5.md §46.1/§46.2: registration blocks on acknowledging Privacy and accepting
        // TermsClient — the only two elements the law makes a precondition of using the product at all
        // (a one-sided document, and a contract offer). Checked before anything else touches the
        // database — an unaccepted registration must never create a row to begin with. PdnConsent is
        // deliberately NOT checked here: §59.2 records that this departs from SPEC's literal wording in
        // favour of the legal opinion it was written against, which is the authoritative source on this
        // one point. A caller who never calls POST /api/profile/consents afterwards is registered and
        // fully functional — that is the intended, not a degraded, outcome.
        //
        // Checked by hand, not via [Required] (API_CONTRACT_CYCLE5.md §40.1, RegisterDto's own note):
        // a missing/incomplete `legal` gets this exact domain string, not a generic ProblemDetails blob —
        // frontend's utils/authError.ts substring-matches this text.
        if (string.IsNullOrWhiteSpace(dto.Legal?.PrivacyAcknowledgedVersion) || string.IsNullOrWhiteSpace(dto.Legal?.TermsAcceptedVersion))
            return BadRequest("Consent to the Terms of Service and the Privacy Policy is required.");

        var snapshot = legalProvider.Current;
        var privacyDoc = snapshot?.Get(LegalDocumentType.Privacy);
        var termsDoc = snapshot?.Get(LegalDocumentType.TermsClient);
        if (privacyDoc is null || termsDoc is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Правовые документы временно недоступны.");

        // The caller's form may have been open while an operator replaced a document — reject stale
        // versions with 409 rather than silently recording acceptance of a document the person never
        // actually saw (API_CONTRACT_CYCLE5.md §40.2 p.1).
        if (dto.Legal.PrivacyAcknowledgedVersion != privacyDoc.Version || dto.Legal.TermsAcceptedVersion != termsDoc.Version)
            return Conflict("Документы были обновлены ещё раз — перечитайте и примите новую редакцию.");

        // US-26: the account is identified by the CANONICAL phone — otherwise "8 999..." and
        // "+7 999..." would register as two different accounts (the exact problem this history fixes).
        // US-61/Q4 (ARCHITECTURE_CYCLE6.md §48.2 p.1): new accounts only accept the Russian format —
        // temporary, one predicate (PhoneNormalizer.IsRussian) away from being lifted.
        if (!PhoneNormalizer.TryNormalizeRussian(dto.Phone, out var canonicalPhone))
            return BadRequest("Введите номер телефона в формате +7 (900) 000-00-00");

        // ARCHITECTURE_CYCLE12.md §148.1 step 3, API_CONTRACT_CYCLE12.md §168: NEW, only when the
        // optional field is present at all — an omitted field behaves exactly as before this cycle
        // (US-12-07). Checked AFTER legal/phone (§148.1's own ordering: "порядок — часть контракта").
        PhoneVerificationSession? verificationSession = null;
        if (dto.PhoneVerification is not null)
        {
            var adapter = phoneVerificationRegistry.Get(PhoneVerificationMethod.MaxBot);
            if (!adapter.Enabled)
                return Conflict(PhoneVerificationTexts.SubsystemUnavailable);

            var now = DateTime.UtcNow;
            var candidate = await db.PhoneVerificationSessions
                .FirstOrDefaultAsync(s => s.Id == dto.PhoneVerification.SessionId);

            var statusTokenMatches = candidate is not null &&
                StatusTokenGenerator.Hash(dto.PhoneVerification.StatusToken) == candidate.StatusTokenHash;

            var valid = candidate is not null && statusTokenMatches
                && candidate.Status == PhoneVerificationStatus.Verified
                && candidate.Purpose == PhoneVerificationPurpose.Registration
                && candidate.UserId == null
                && candidate.ConsumableUntilUtc is { } consumableUntil && consumableUntil > now
                && candidate.CanonicalPhone == canonicalPhone;

            if (!valid)
                return Conflict(PhoneVerificationTexts.RegisterSessionInvalid);

            verificationSession = candidate;
        }

        // Phone is the account identifier: it goes into UserName (which has Identity's unique index),
        // giving phone uniqueness for free. Email is optional.
        var user = new AppUser
        {
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Email = dto.Email,
            UserName = canonicalPhone,
            PhoneNumber = canonicalPhone
        };

        var result = await userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
            return BadRequest(result.Errors);

        await userManager.AddToRoleAsync(user, "Client");

        // Two consent journal rows, one moment, in the same request as account creation (ARCHITECTURE_
        // CYCLE5.md §40.2 p.2) — not the same DB transaction as CreateAsync (Identity commits that on its
        // own), but nothing else can observe the account before this write completes, since Register
        // hasn't returned yet. Each grant carries its own transaction/advisory-lock pair (ConsentLedger).
        var subject = ConsentSubject.ForUser(user.Id);
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        await ledger.GrantAsync(new ConsentGrant(
            subject, LegalDocumentType.Privacy.ToString(), privacyDoc.Version, privacyDoc.ContentHash,
            Purpose: null, ConsentAct.Acknowledged, ConsentSource.Registration, ipAddress, userAgent));
        await ledger.GrantAsync(new ConsentGrant(
            subject, LegalDocumentType.TermsClient.ToString(), termsDoc.Version, termsDoc.ContentHash,
            Purpose: null, ConsentAct.Accepted, ConsentSource.Registration, ipAddress, userAgent));

        // ARCHITECTURE_CYCLE12.md §148.1 step 5: in the SAME transaction as the VerifiedPhone write, the
        // ceiling is re-checked (it could have been exhausted by the same MAX account between the
        // session being verified and this request arriving) and the mirror is set. Р1: a ceiling hit
        // here does NOT fail registration — the account exists either way, just with phoneVerified:false.
        var phoneVerified = false;
        if (verificationSession is not null)
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await AdvisoryLock.AcquireAsync(db, $"phone-verification:{verificationSession.ExternalAccountKey}");

            var writeOutcome = await phoneVerificationWriter.WriteAsync(
                verificationSession, user, phoneVerificationOptions.Value.MaxPhonesPerExternalAccount, HttpContext.RequestAborted);
            verificationSession.Status = PhoneVerificationStatus.Consumed;
            phoneVerified = writeOutcome == PhoneVerificationWriter.WriteOutcome.Written;

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        var roles = await userManager.GetRolesAsync(user);
        var token = tokenService.GenerateToken(user, roles, privacyDoc.Version, termsDoc.Version);

        return Ok(new AuthResponseDto(token, user.Id, user.PhoneNumber!, user.Email, user.FirstName, user.LastName, roles, phoneVerified));
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth-login")]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginDto dto)
    {
        // Same canonical form the account was created/normalized to — this is what lets someone who
        // registered as "8 999..." log in typing "+7 999..." (US-26, AUTH-0xx). An invalid-looking
        // number simply won't match anything and falls through to the same 401 as any other bad login —
        // no separate 400 here, to avoid leaking whether a number "looks right" to an attacker probing
        // logins.
        var canonicalPhone = PhoneNormalizer.Normalize(dto.Phone);
        var user = await userManager.FindByNameAsync(canonicalPhone);
        if (user is null)
        {
            logger.LogInformation("Login outcome {Outcome} for {Phone}", LoginOutcome.UserNotFound, LogMasking.Phone(canonicalPhone));
            return Unauthorized("Invalid credentials");
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, dto.Password, lockoutOnFailure: true);
        var outcome = LoginOutcomeMapper.FromSignInResult(result);
        if (outcome != LoginOutcome.Success)
        {
            logger.LogInformation("Login outcome {Outcome} for {Phone}", outcome, LogMasking.Phone(canonicalPhone));
            return LoginOutcomeMapper.ToErrorResponse(outcome);
        }

        logger.LogInformation("Login outcome {Outcome} for {Phone}", LoginOutcome.Success, LogMasking.Phone(canonicalPhone));

        var roles = await userManager.GetRolesAsync(user);

        // The claims carry what THIS user actually accepted, from the ConsentRecord journal — not the
        // document's current version (ARCHITECTURE.md §6.3 p.1). An account that predates cycle C, or was
        // created while the legal manifest was unavailable in Dev/Testing, simply has no rows here; the
        // claim is then omitted (TokenService), and LegalConsentFilter treats that the same as any other
        // mismatch. Three cold reads (login is not the per-request hot path §45.1 protects) — TermsOwner
        // comes back null for anyone who never accepted it, which is the correct "not an owner" claim state.
        var subject = ConsentSubject.ForUser(user.Id);
        var privacyState = await ledger.CurrentAsync(subject, LegalDocumentType.Privacy.ToString(), purpose: null);
        var termsState = await ledger.CurrentAsync(subject, LegalDocumentType.TermsClient.ToString(), purpose: null);
        var ownerTermsState = await ledger.CurrentAsync(subject, LegalDocumentType.TermsOwner.ToString(), purpose: null);
        var token = tokenService.GenerateToken(user, roles, privacyState?.DocumentVersion, termsState?.DocumentVersion, ownerTermsState?.DocumentVersion);

        // AuthResponseDto's own note: PhoneVerified here is simply the account's current mirror, not
        // something login itself computes — same field, same meaning as everywhere else it appears.
        return Ok(new AuthResponseDto(token, user.Id, user.PhoneNumber!, user.Email, user.FirstName, user.LastName, roles, user.PhoneNumberConfirmed));
    }
}
