using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Legal;
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
    AppDbContext db,
    ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("register")]
    [EnableRateLimiting("auth-register")]
    public async Task<ActionResult<AuthResponseDto>> Register(RegisterDto dto)
    {
        // US-37 (BREAKING № 1, API_CONTRACT.md §5): checked before anything else touches the database —
        // an unaccepted registration must never create a row to begin with.
        if (!dto.AcceptedLegal)
            return BadRequest("Consent to the Terms of Service and the Privacy Policy is required.");

        // Registration writes a UserConsent row for BOTH documents, so both must be loaded before the
        // account is created — the alternative (create the user, then discover a document is missing)
        // would leave a real account with no recorded consent (ARCHITECTURE.md §6.1).
        var snapshot = legalProvider.Current;
        var privacyDoc = snapshot?.Get(LegalDocumentType.Privacy);
        var termsDoc = snapshot?.Get(LegalDocumentType.Terms);
        if (privacyDoc is null || termsDoc is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Правовые документы временно недоступны.");

        // US-26: the account is identified by the CANONICAL phone — otherwise "8 999..." and
        // "+7 999..." would register as two different accounts (the exact problem this history fixes).
        if (!PhoneNormalizer.TryNormalize(dto.Phone, out var canonicalPhone))
            return BadRequest("Phone number must contain 10 to 15 digits.");

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

        // US-37 p.1: two UserConsent rows, one moment, in the same request as account creation (not the
        // same DB transaction as CreateAsync — Identity commits that on its own — but nothing else can
        // observe the account before this write completes, since Register hasn't returned yet).
        var acceptedAt = DateTime.UtcNow;
        db.UserConsents.AddRange(
            new UserConsent { Id = Guid.NewGuid(), UserId = user.Id, DocumentType = LegalDocumentType.Privacy, Version = privacyDoc.Version, AcceptedAtUtc = acceptedAt },
            new UserConsent { Id = Guid.NewGuid(), UserId = user.Id, DocumentType = LegalDocumentType.Terms, Version = termsDoc.Version, AcceptedAtUtc = acceptedAt });
        await db.SaveChangesAsync();

        var roles = await userManager.GetRolesAsync(user);
        var token = tokenService.GenerateToken(user, roles, privacyDoc.Version, termsDoc.Version);

        return Ok(new AuthResponseDto(token, user.Id, user.PhoneNumber!, user.Email, user.FirstName, user.LastName, roles));
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

        // The claims carry what THIS user actually accepted, from UserConsent — not the document's
        // current version (ARCHITECTURE.md §6.3 p.1). An account that predates cycle C, or was created
        // while the legal manifest was unavailable in Dev/Testing, simply has no rows here; the claim is
        // then omitted (TokenService), and LegalConsentFilter treats that the same as any other mismatch.
        var consents = await db.UserConsents.Where(c => c.UserId == user.Id).ToListAsync();
        var privacyVersion = consents.FirstOrDefault(c => c.DocumentType == LegalDocumentType.Privacy)?.Version;
        var termsVersion = consents.FirstOrDefault(c => c.DocumentType == LegalDocumentType.Terms)?.Version;
        var token = tokenService.GenerateToken(user, roles, privacyVersion, termsVersion);

        return Ok(new AuthResponseDto(token, user.Id, user.PhoneNumber!, user.Email, user.FirstName, user.LastName, roles));
    }
}
