using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.API.Services.Stays;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Showcase;
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
    TokenService tokenService, OrdersPlanResolver ordersPlans, IOptions<StaysOptions> staysOptions)
{
    public const string SlugTakenSalonText = "Slug already taken";

    public async Task<CompanyCreationOutcome> CreateAsync(
        CompanyKind kind, CompanyCreationRequest request, ClaimsPrincipal user, string? remoteIp, string? userAgent)
    {
        var isShop = kind == CompanyKind.Orders;
        var isStays = kind == CompanyKind.Stays;
        var isSalon = kind == CompanyKind.Services;
        var slug = isSalon ? request.Slug ?? string.Empty : SlugPolicy.Normalize(request.Slug);

        // Salons: the slug is checked first, exactly as before (ezbook behaviour unchanged).
        if (isSalon && await db.Companies.AnyAsync(c => c.Slug == slug))
            return Refuse(new ConflictObjectResult(SlugTakenSalonText));
        // ARCHITECTURE_CYCLE28.md §577.3: "primer-" is the showcase prefix nginx marks as noindex; a real company may not take it.
        if (isSalon && ShowcaseMixingGuard.IsReservedSlug(slug))
            return Refuse(new ConflictObjectResult(ShowcaseMixingGuard.ReservedSlugText));

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

        // Cycle 37 (API_CONTRACT_CYCLE37.md §37.27.1): name → phone → address (StaysSlugPolicy, JSON 409) → the city of the vertical.
        string? staysPhone = null;
        if (isStays)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
                return Refuse(new BadRequestObjectResult("Укажите название"));
            if (request.Description is { Length: > 2000 }) return Refuse(new BadRequestObjectResult("Описание — не длиннее 2000 символов"));
            if (string.IsNullOrWhiteSpace(request.Phone) || !PhoneNormalizer.TryNormalizeRussian(request.Phone, out var canonicalStaysPhone))
                return Refuse(new BadRequestObjectResult("Введите номер телефона в формате +7 (900) 000-00-00"));
            staysPhone = canonicalStaysPhone;
            if (string.IsNullOrEmpty(slug)) slug = await SuggestStaysSlugAsync(request.Name!);
            var staysRefusal = await StaysSlugRefusalAsync(slug, exceptCompanyId: null);
            if (staysRefusal is not null) return Refuse(new ConflictObjectResult(staysRefusal));
        }

        // Cycle 4, API_CONTRACT_CYCLE4.md §31.2 (breaking change): every new company needs a city, so
        // a derived time zone exists for reminder timing. Validated before touching the advisory lock
        // below — no point serializing on the owner-companies lock for a request that's going to 400.
        // A "Дома" company is always in the city of the vertical (Stays:CatalogCity) — cityId is not accepted (§37.11.1).
        City? city;
        if (isStays)
        {
            var cityOptions = staysOptions.Value.CatalogCity;
            city = await db.Cities.FirstOrDefaultAsync(c => c.Name == cityOptions.Name && c.Region == cityOptions.Region && c.IsActive);
            if (city is null)
                return Refuse(new ObjectResult("Справочник городов не готов") { StatusCode = StatusCodes.Status503ServiceUnavailable });
        }
        else
        {
            if (request.CityId is null)
                return Refuse(new BadRequestObjectResult(isShop ? "Укажите город магазина" : "Укажите город салона"));

            city = await db.Cities.FindAsync(request.CityId.Value);
            if (city is null || !city.IsActive)
                return Refuse(new BadRequestObjectResult("Город не найден"));
        }

        if (!string.IsNullOrWhiteSpace(request.TimeZoneId) &&
            !TimeZoneOffset.TryGetUtcOffsetMinutes(request.TimeZoneId, DateTime.UtcNow, out _))
            return Refuse(new BadRequestObjectResult("Неизвестный часовой пояс"));

        var (timeZoneId, timeZoneIsManual) = CompanyTimeZoneResolver.ForNewCompany(city.TimeZoneId, request.TimeZoneId);

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // Branch limit: the account plan caps how many companies this owner may create. Without a plan
        // (Free) that's 1 — so a brand-new owner can open their first company, but a second branch needs
        // a paid plan with MaxCompanies >= 2. Existing companies over a since-lowered limit are untouched.
        // Since cycle 24 shops and salons are counted apart, each in its own line (Q-24-7).
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
        if (isStays)
        {
            await AdvisoryLock.AcquireAsync(db, "company-slug");
            var staysRefusal = await StaysSlugRefusalAsync(slug, exceptCompanyId: null);
            if (staysRefusal is not null) return Refuse(new ConflictObjectResult(staysRefusal));
        }

        // ARCHITECTURE_CYCLE24.md §459.3 (Q-24-7, changes Q5 of cycle 23): the limits are counted WITHIN the line. A shop is checked against the tariff
        // of the "Заказы" line and the shops of the account; a salon against the "Записи" tariff and the salons — a shop no longer takes a salon's place.
        var plan = await subscriptionResolver.GetEffectivePlanForAccountAsync(accountId);
        if (isShop)
        {
            var ordersPlan = await ordersPlans.GetForAccountAsync(accountId);
            if (ordersPlan.MaxShops is { } maxShops)
            {
                var shopCount = await db.Companies.CountAsync(c => c.BillingAccountId == accountId && c.Kind == CompanyKind.Orders);
                if (shopCount >= maxShops)
                    return Refuse(new ObjectResult(BillingTexts.ShopLimitReached(ordersPlan.PlanName, maxShops)) { StatusCode = 402 });
            }
        }
        else if (isSalon)
        {
            if (plan.AccountMaxCompanies.HasValue)
            {
                var ownedCount = await db.Companies.CountAsync(c => c.BillingAccountId == accountId && c.Kind == CompanyKind.Services);
                if (ownedCount >= plan.AccountMaxCompanies.Value)
                    return Refuse(new ObjectResult(BillingTexts.CompanyLimitReached(ownedCount, plan.AccountMaxCompanies.Value)) { StatusCode = 402 });
            }
        }

        // ARCHITECTURE_CYCLE28.md §574.2: a company inherits the showcase mark of its billing account (on the demo stand an owner may
        // create one more company; on production the mark is always false).
        var accountIsShowcase = await db.BillingAccounts.Where(a => a.Id == accountId).Select(a => a.IsShowcase).FirstAsync();

        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = isSalon ? request.Name ?? string.Empty : request.Name!.Trim(),
            Slug = slug,
            Description = request.Description,
            Address = request.Address,
            Phone = isStays ? staysPhone : request.Phone,
            Email = request.Email,
            // A shop takes no bookings and is not in the salon directory (§388.4).
            AllowSelfBooking = isSalon && request.AllowSelfBooking,
            // Cycle 25 (§505.2, Q-25-7): a shop is created visible in the goods catalog (it still needs hours, a product and a plan that allows it);
            // a salon keeps the owner's own choice.
            ShowInPublicListing = !isSalon || request.ShowInPublicListing,
            OwnerUserId = userId,
            BillingAccountId = accountId,
            CityId = city.Id,
            TimeZoneId = timeZoneId,
            TimeZoneIsManual = timeZoneIsManual,
            Kind = kind,
            IsShowcase = accountIsShowcase,
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
        if (isStays)
            db.StaysSettings.Add(new StaysSettings { CompanyId = company.Id, UpdatedByUserId = userId, UpdatedAtUtc = DateTime.UtcNow });

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
        // The showcase prefix is reserved for shops too: a slug is unique across ALL companies, so a shop holding "primer-…" would make
        // `ops showcase create/recreate` (and the weekly re-seed) fail on the unique index.
        if (ShowcaseMixingGuard.IsReservedSlug(normalizedSlug))
            return new CatalogConflictDto(CatalogConflictCode.SlugReserved, ShopTexts.SlugReserved);
        var taken = await db.Companies.AsNoTracking()
            .AnyAsync(c => c.Slug.ToLower() == normalizedSlug && (exceptCompanyId == null || c.Id != exceptCompanyId));
        return taken ? new CatalogConflictDto(CatalogConflictCode.SlugTaken, ShopTexts.SlugTaken) : null;
    }

    /// <summary>
    /// API_CONTRACT_CYCLE37.md §37.27.1 — the "Дома" address checks: format and length, reserved words (dom-routes.json), then whether ANY company holds it
    /// (the slug space is shared by the whole platform, case-insensitively). Null = free.
    /// </summary>
    public async Task<StaysConflictDto?> StaysSlugRefusalAsync(string normalizedSlug, Guid? exceptCompanyId)
    {
        switch (StaysSlugPolicy.Validate(normalizedSlug))
        {
            case SlugCheck.Invalid: return new StaysConflictDto("SlugInvalid", ShopTexts.SlugInvalid);
            case SlugCheck.Reserved: return new StaysConflictDto("SlugReserved", ShopTexts.SlugReserved);
        }
        var taken = await db.Companies.AsNoTracking()
            .AnyAsync(c => c.Slug.ToLower() == normalizedSlug && (exceptCompanyId == null || c.Id != exceptCompanyId));
        return taken ? new StaysConflictDto("SlugTaken", ShopTexts.SlugTaken) : null;
    }

    /// <summary>A free address made from the name (transliteration, then -2, -3 … suffixes).</summary>
    public async Task<string> SuggestStaysSlugAsync(string name)
    {
        var baseSlug = SlugTransliterator.ToBase(name, 50, 3, StaysSlugPolicy.ReservedSlugs);
        var candidates = SlugTransliterator.Candidates(baseSlug, () => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(2)).ToLowerInvariant()).ToList();
        var taken = (await db.Companies.AsNoTracking().Where(c => candidates.Contains(c.Slug.ToLower())).Select(c => c.Slug.ToLower()).ToListAsync()).ToHashSet();
        return candidates.FirstOrDefault(c => !taken.Contains(c)) ?? candidates[^1];
    }

    private static CompanyCreationOutcome Refuse(ActionResult error) => new(error);
}
