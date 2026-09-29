using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Companies;

/// <summary>The fields both <c>POST /api/companies</c> and <c>POST /api/shops</c> send.</summary>
public sealed record CompanyCreationRequest(
    string? Name, string? Slug, string? Description, string? Address, string? Phone, string? Email, int? CityId,
    string? TimeZoneId, bool AllowSelfBooking, bool ShowInPublicListing, string? OwnerTermsVersion);

/// <summary>Either the exact error result the caller returns as is, or the created company with what the response needs.</summary>
public sealed record CompanyCreationOutcome(
    ActionResult? Error, Company? Company = null, City? City = null, EffectivePlan? Plan = null,
    Guid AccountId = default, string? Token = null);

/// <summary>
/// ARCHITECTURE_CYCLE23.md §395.1 — the body of the former <c>CompaniesController.Create</c>, moved here WITHOUT changing the
/// salon path (same checks in the same order, same texts, same transaction and lock <c>billing-account:{id}</c>, same
/// TermsOwner acceptance, same fresh token) so that <c>POST /api/companies</c> (Services) and <c>POST /api/shops</c> (Orders)
/// share one implementation. The shop path adds the address policy (<see cref="SlugPolicy"/>, JSON 409 codes), the
/// ShopSettings row and the order of checks of API_CONTRACT_CYCLE23.md §409.1.
/// </summary>
public sealed class CompanyCreationService(
    AppDbContext db, UserManager<AppUser> userManager, SubscriptionResolver subscriptionResolver,
    BillingAccountProvisioner billingAccountProvisioner, LegalDocumentProvider legalProvider, ConsentLedger ledger,
    TokenService tokenService)
{
    public const string SlugTakenSalonText = "Slug already taken";

    public async Task<CompanyCreationOutcome> CreateAsync(
        CompanyKind kind, CompanyCreationRequest request, ClaimsPrincipal user, string? remoteIp, string? userAgent)
    {
        var isShop = kind == CompanyKind.Orders;
        var slug = isShop ? SlugPolicy.Normalize(request.Slug) : request.Slug ?? string.Empty;

        // Salons: the slug is checked first, exactly as before (ezbook behaviour unchanged).
        if (!isShop && await db.Companies.AnyAsync(c => c.Slug == slug))
            return Refuse(new ConflictObjectResult(SlugTakenSalonText));

        // ARCHITECTURE_CYCLE5.md §42.1, API_CONTRACT_CYCLE5.md §42.1 (BREAKING № 3). Checked by hand
        // (RegisterDto.Legal's own note explains why), before anything else touches the database — an
        // unaccepted company creation must never create a row to begin with.
        if (string.IsNullOrWhiteSpace(request.OwnerTermsVersion))
            return Refuse(new BadRequestObjectResult("Для создания компании нужно принять соглашение с владельцем."));

        var ownerTermsDoc = legalProvider.Current?.Get(LegalDocumentType.TermsOwner);
        if (ownerTermsDoc is null)
            return Refuse(new ObjectResult("Правовые документы временно недоступны.") { StatusCode = StatusCodes.Status503ServiceUnavailable });
        if (request.OwnerTermsVersion != ownerTermsDoc.Version)
            return Refuse(new ConflictObjectResult("Соглашение было обновлено ещё раз — перечитайте и примите новую редакцию."));

        if (isShop)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
                return Refuse(new BadRequestObjectResult("Укажите название магазина"));
            var slugRefusal = await CheckShopSlugAsync(slug, exceptCompanyId: null);
            if (slugRefusal is not null) return Refuse(slugRefusal);
        }

        // Cycle 4, API_CONTRACT_CYCLE4.md §31.2 (breaking change): every new company needs a city, so
        // a derived time zone exists for reminder timing. Validated before touching the advisory lock
        // below — no point serializing on the owner-companies lock for a request that's going to 400.
        if (request.CityId is null)
            return Refuse(new BadRequestObjectResult(isShop ? "Укажите город магазина" : "Укажите город салона"));

        var city = await db.Cities.FindAsync(request.CityId.Value);
        if (city is null || !city.IsActive)
            return Refuse(new BadRequestObjectResult("Город не найден"));

        if (!string.IsNullOrWhiteSpace(request.TimeZoneId) &&
            !TimeZoneOffset.TryGetUtcOffsetMinutes(request.TimeZoneId, DateTime.UtcNow, out _))
            return Refuse(new BadRequestObjectResult("Неизвестный часовой пояс"));

        var (timeZoneId, timeZoneIsManual) = CompanyTimeZoneResolver.ForNewCompany(city.TimeZoneId, request.TimeZoneId);

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // Branch limit: the account plan caps how many companies this owner may create. Without a plan
        // (Free) that's 1 — so a brand-new owner can open their first company, but a second branch needs
        // a paid plan with MaxCompanies >= 2. Existing companies over a since-lowered limit are untouched.
        // Shops and salons count together (Q5 of cycle 23).
        //
        // Serialize concurrent creates against everything else that can change how many companies fit
        // on this billing account (§52) — a company transfer moving a company IN, or another create —
        // by using the SAME lock key ("billing-account:{id}") those operations already take
        // (AdminBillingController.AssignSubscription, CompanyTransferService). Locking by userId alone
        // (the previous key) let a create race a concurrent transfer: both would read the pre-write
        // company count and both pass the limit check.
        var accountId = await billingAccountProvisioner.EnsureAccountAsync(userId);
        await using var limitTransaction = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"billing-account:{accountId}");
        // Shops: the address is claimed under its own lock (the same key as a slug change), the check repeated inside it —
        // two owners typing the same address at once cannot both pass (the unique index is only a case-sensitive net).
        if (isShop)
        {
            await AdvisoryLock.AcquireAsync(db, "company-slug");
            var slugRefusal = await CheckShopSlugAsync(slug, exceptCompanyId: null);
            if (slugRefusal is not null) return Refuse(slugRefusal);
        }

        var plan = await subscriptionResolver.GetEffectivePlanForAccountAsync(accountId);
        if (plan.AccountMaxCompanies.HasValue)
        {
            var ownedCount = await db.Companies.CountAsync(c => c.BillingAccountId == accountId);
            if (ownedCount >= plan.AccountMaxCompanies.Value)
                return Refuse(new ObjectResult(BillingTexts.CompanyLimitReached(ownedCount, plan.AccountMaxCompanies.Value)) { StatusCode = 402 });
        }

        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = isShop ? request.Name!.Trim() : request.Name ?? string.Empty,
            Slug = slug,
            Description = request.Description,
            Address = request.Address,
            Phone = request.Phone,
            Email = request.Email,
            // A shop takes no bookings and is not in the salon directory (§388.4).
            AllowSelfBooking = !isShop && request.AllowSelfBooking,
            ShowInPublicListing = !isShop && request.ShowInPublicListing,
            OwnerUserId = userId,
            BillingAccountId = accountId,
            CityId = city.Id,
            TimeZoneId = timeZoneId,
            TimeZoneIsManual = timeZoneIsManual,
            Kind = kind
        };

        var member = new CompanyMember
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            UserId = userId,
            Role = UserRole.CompanyOwner
        };

        db.Companies.Add(company);
        db.CompanyMembers.Add(member);
        if (isShop)
            db.ShopSettings.Add(new ShopSettings { CompanyId = company.Id, UpdatedByUserId = userId, UpdatedAtUtc = DateTime.UtcNow });

        await db.SaveChangesAsync();
        // US-46, ARCHITECTURE.md §8.3/§8.4: recompute Identity roles from the CompanyMember rows just
        // written, inside the same transaction and AFTER SaveChangesAsync — UserManager writes its own
        // AspNetUserRoles changes through the same AppDbContext, so this is the order that keeps both
        // writes in one commit instead of a separate round trip.
        await IdentityRoleSync.SyncAsync(db, userManager, userId);
        await limitTransaction.CommitAsync();

        // ARCHITECTURE_CYCLE5.md §42.1 — the acceptance itself, recorded AFTER the company/membership
        // commit above (nothing before this point can fail because of it, and a failure here must not
        // undo an otherwise-successful company creation — best-effort would be wrong here though: US-66
        // needs this row to exist, so it's still inside the overall request, just its own grant/lock).
        var ownerSubject = ConsentSubject.ForUser(userId);
        await ledger.GrantAsync(new ConsentGrant(
            ownerSubject, LegalDocumentType.TermsOwner.ToString(), ownerTermsDoc.Version, ownerTermsDoc.ContentHash,
            Purpose: null, ConsentAct.Accepted, ConsentSource.CompanyCreation,
            IpAddress: remoteIp, UserAgent: userAgent));

        // A fresh token, carrying the new "lco" claim — see CreateCompanyResponseDto's own doc comment
        // for why this is mandatory, not an optimization. Privacy/TermsClient claims are re-resolved the
        // same way AuthController.Login does, so this token is complete, not just augmented.
        var appUser = await userManager.FindByIdAsync(userId);
        var roles = await userManager.GetRolesAsync(appUser!);
        var privacyState = await ledger.CurrentAsync(ownerSubject, LegalDocumentType.Privacy.ToString(), purpose: null);
        var termsState = await ledger.CurrentAsync(ownerSubject, LegalDocumentType.TermsClient.ToString(), purpose: null);
        var token = tokenService.GenerateToken(appUser!, roles, privacyState?.DocumentVersion, termsState?.DocumentVersion, ownerTermsDoc.Version);

        return new CompanyCreationOutcome(null, company, city, plan, accountId, token);
    }

    /// <summary>
    /// The shop address checks of API_CONTRACT_CYCLE23.md §409.1 п. 5–7: format and length, reserved words, then whether the
    /// address is taken by ANY company (salon or shop, case-insensitively). Null = the address is free.
    /// </summary>
    public async Task<ConflictObjectResult?> CheckShopSlugAsync(string normalizedSlug, Guid? exceptCompanyId)
    {
        var refusal = await ShopSlugRefusalAsync(normalizedSlug, exceptCompanyId);
        return refusal is null ? null : new ConflictObjectResult(refusal);
    }

    /// <summary>The same check as data, for slug-check (which answers 200 with a reason instead of 409).</summary>
    public async Task<CatalogConflictDto?> ShopSlugRefusalAsync(string normalizedSlug, Guid? exceptCompanyId)
    {
        switch (SlugPolicy.Validate(normalizedSlug))
        {
            case SlugCheck.Invalid:
                return new CatalogConflictDto(CatalogConflictCode.SlugInvalid, ShopTexts.SlugInvalid);
            case SlugCheck.Reserved:
                return new CatalogConflictDto(CatalogConflictCode.SlugReserved, ShopTexts.SlugReserved);
        }
        var taken = await db.Companies.AsNoTracking()
            .AnyAsync(c => c.Slug.ToLower() == normalizedSlug && (exceptCompanyId == null || c.Id != exceptCompanyId));
        return taken ? new CatalogConflictDto(CatalogConflictCode.SlugTaken, ShopTexts.SlugTaken) : null;
    }

    private static CompanyCreationOutcome Refuse(ActionResult error) => new(error);
}
