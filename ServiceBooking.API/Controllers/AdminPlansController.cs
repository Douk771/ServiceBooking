using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): the subscription-plan half of the former
/// <c>AdminController</c> — same <c>api/admin</c> prefix, same SuperAdmin gate, same per-action routes.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "SuperAdmin")]
public class AdminPlansController(AppDbContext db, PricingCatalogCache pricingCatalogCache) : ControllerBase
{
    // ── Subscription Plan Configs ──────────────────────────────────────────────

    [HttpGet("plans")]
    public async Task<IActionResult> GetPlans(CancellationToken ct)
    {
        var plans = await db.SubscriptionPlanConfigs.OrderBy(p => p.PricePerMonth).ToListAsync(ct);
        var subscriberCounts = await GetActiveSubscriberCountsAsync(plans.Select(p => p.Id));
        var rules = await db.PlanOptionRules.WhereNotRetired().Where(r => plans.Select(p => p.Id).Contains(r.PlanConfigId)).ToListAsync(ct);
        var totalOptionsInCatalog = await db.SubscriptionOptions.WhereNotRetired().CountAsync(ct);
        return Ok(new AdminPlansListDto(plans.Select(p =>
            MapAdminPlanDto(p, subscriberCounts.GetValueOrDefault(p.Id), rules.Where(r => r.PlanConfigId == p.Id).ToList(), totalOptionsInCatalog)).ToList()));
    }

    // contracts/cycle7/openapi.yaml AdminPlanInput (BREAKING fix, cycle-07 backend report): the previous shape
    // bound straight into SubscriptionPlanConfig (Highlights as a raw newline-separated string) and had
    // no `options` field at all — every plan created/updated through this endpoint left the whole
    // option-availability matrix untouched, silently leaving every option Unavailable. Both endpoints
    // below now accept the contract's array-of-strings Highlights and a full options matrix.
    [HttpPost("plans")]
    public async Task<IActionResult> CreatePlan([FromBody] AdminPlanInput dto)
    {
        var validationError = ValidatePlanInput(dto);
        if (validationError is not null) return validationError;

        var systemFreeError = await ValidateSystemFreeAsync(false, dto.PricePerMonth, existingPlanId: null, dto.Line ?? CompanyKind.Services);
        if (systemFreeError is not null) return systemFreeError;

        var plan = new SubscriptionPlanConfig
        {
            Line = dto.Line ?? CompanyKind.Services,
            MaxProductsPerShop = dto.MaxProductsPerShop,
            MaxOrdersPerMonth = dto.MaxOrdersPerMonth,
            AllowOrders = dto.AllowOrders ?? true,
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Description = dto.Description,
            Highlights = JoinHighlights(dto.Highlights),
            PricePerMonth = dto.PricePerMonth,
            MaxEmployees = dto.MaxEmployees,
            MaxCompanies = dto.MaxCompanies,
            AllowOnlineBooking = dto.AllowOnlineBooking,
            AllowMailing = dto.AllowMailing,
            AllowAnalytics = dto.AllowAnalytics,
            AllowPublicListing = dto.AllowPublicListing,
            AllowOnlinePayment = dto.AllowOnlinePayment,
            PhotoQuotaMb = dto.PhotoQuotaMb,
            PhotoRetention = dto.PhotoRetention,
            NotifyDaysBefore = dto.NotifyDaysBefore,
            IsPublic = dto.IsPublic ?? false,
            IsActive = dto.IsActive,
            SortOrder = dto.SortOrder ?? 0,
            IsSystemFree = false,
            CreatedAt = DateTime.UtcNow,
        };
        var optionRulesError = dto.Options is not null ? await ApplyOptionRulesAsync(plan.Id, dto.Options) : null;
        if (optionRulesError is not null) return optionRulesError;
        // NB-8 (cycle-07 backend report): no try/catch DbUpdateException-when-IsSystemFree here, unlike
        // UpdatePlan/SetSystemFree — IsSystemFree is hardcoded false a few lines up, so that guard could
        // never fire; keeping it would have been dead code masking a real conflict as an unhandled 500.
        // A brand-new plan can never race the "one system-free plan" unique index because it never asks
        // to be the system-free plan in the first place (see PUT .../system-free for that transition).
        db.SubscriptionPlanConfigs.Add(plan);
        await db.SaveChangesAsync();
        pricingCatalogCache.Invalidate();
        // A brand-new plan has no subscribers yet — no need for the AccountSubscriptions round trip.
        // Contract (API_CONTRACT_CYCLE7.md) documents 201 Created for a successful create, not 200.
        return StatusCode(StatusCodes.Status201Created, await BuildAdminPlanDtoAsync(plan, isNew: true));
    }

    [HttpPut("plans/{id:guid}")]
    public async Task<IActionResult> UpdatePlan(Guid id, [FromBody] AdminPlanInput dto)
    {
        var validationError = ValidatePlanInput(dto);
        if (validationError is not null) return validationError;

        var plan = await db.SubscriptionPlanConfigs.FindAsync(id);
        if (plan is null) return NotFound();

        // ARCHITECTURE_CYCLE24.md §459.5: the line is chosen at creation and never changes — accounts, limits and history are counted by it.
        if (dto.Line is { } requestedLine && requestedLine != plan.Line)
            return Conflict("Линейку тарифа менять нельзя");

        var systemFreeError = await ValidateSystemFreeAsync(plan.IsSystemFree, dto.PricePerMonth, existingPlanId: id, plan.Line);
        if (systemFreeError is not null) return systemFreeError;

        plan.MaxProductsPerShop = dto.MaxProductsPerShop;
        plan.MaxOrdersPerMonth = dto.MaxOrdersPerMonth;
        if (dto.AllowOrders.HasValue) plan.AllowOrders = dto.AllowOrders.Value;
        plan.Name = dto.Name;
        plan.PricePerMonth = dto.PricePerMonth;
        plan.MaxEmployees = dto.MaxEmployees;
        plan.MaxCompanies = dto.MaxCompanies;
        plan.AllowOnlineBooking = dto.AllowOnlineBooking;
        plan.AllowMailing = dto.AllowMailing;
        plan.AllowAnalytics = dto.AllowAnalytics;
        plan.AllowPublicListing = dto.AllowPublicListing;
        plan.AllowOnlinePayment = dto.AllowOnlinePayment;
        plan.PhotoQuotaMb = dto.PhotoQuotaMb;
        plan.PhotoRetention = dto.PhotoRetention;
        plan.Description = dto.Description;
        plan.NotifyDaysBefore = dto.NotifyDaysBefore;
        // B4: same "apply only if present" semantics as IsPublic/SortOrder below — the existing admin
        // UI never sends `highlights`/`options`, and applying them unconditionally used to wipe every
        // highlight bullet (JoinHighlights(null) => null) and every PlanOptionRule (ApplyOptionRulesAsync
        // treating a missing `options` as "remove all rules") on every ordinary field edit.
        if (dto.Highlights is not null) plan.Highlights = JoinHighlights(dto.Highlights);
        // ARCHITECTURE_CYCLE15.md §255.4/API_CONTRACT_CYCLE15.md §288 — checked ONLY on the transition
        // (plan.IsPublic true -> dto.IsPublic false), never unconditionally: the shipped system free
        // plan actually ships with IsPublic == false (20260922154148_FixSeedBillingCatalogCapabilityKeys),
        // so an unconditional guard would block every save of it, including ones that don't touch
        // isPublic at all.
        if (plan.IsSystemFree && plan.IsPublic && dto.IsPublic == false)
            return Conflict("Системный бесплатный тариф нельзя убрать с витрины.");
        if (dto.IsPublic.HasValue) plan.IsPublic = dto.IsPublic.Value;
        if (dto.SortOrder.HasValue) plan.SortOrder = dto.SortOrder.Value;
        if (dto.Options is not null)
        {
            var optionRulesError = await ApplyOptionRulesAsync(id, dto.Options);
            if (optionRulesError is not null) return optionRulesError;
        }

        // Deactivating through this endpoint has exactly the effect DeletePlan refuses below: the
        // resolver treats PlanConfig.IsActive == false as Free, so every subscriber silently loses
        // online booking, analytics and their employee limit on the next request. Same guard, same
        // status, or the 409 there is just a speed bump around a differently-named door. The system
        // free plan (ARCHITECTURE_CYCLE7.md §43.4) additionally can never be deactivated at all — the
        // public price list has no "free" row otherwise and every account resolves to the hardcoded
        // EffectivePlan.Free fallback instead of the configured system row.
        if (plan.IsActive && !dto.IsActive)
        {
            if (plan.IsSystemFree)
                return Conflict("The system free plan cannot be deactivated.");

            var activeSubscribers = await ActiveSubscribersAsync(id);
            if (activeSubscribers > 0)
                return Conflict($"Cannot deactivate a plan with {activeSubscribers} active subscriber(s). Move them to another plan first.");
        }
        plan.IsActive = dto.IsActive;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) when (plan.IsSystemFree)
        {
            // Two concurrent requests can both pass the AnyAsync check above before either commits —
            // the partial unique index on IsSystemFree (AppDbContext) is the real guard; translate its
            // violation into the same 409 instead of letting a 500 leak out (code review finding).
            return Conflict("Another plan is already marked as the system free plan.");
        }
        pricingCatalogCache.Invalidate();
        return Ok(await BuildAdminPlanDtoAsync(plan));
    }

    /// <summary>
    /// Separate from PUT /plans/{id} on purpose (code review finding B "isSystemFree removal") —
    /// contracts/cycle7/openapi.yaml's AdminPlanInput has no isSystemFree property, and folding "make
    /// this THE system free plan" into an ordinary field-edit DTO made it too easy for an unrelated PUT
    /// to accidentally flip the flag guarded by the partial unique index (AppDbContext).
    /// </summary>
    [HttpPut("plans/{id:guid}/system-free")]
    public async Task<IActionResult> SetSystemFree(Guid id, [FromBody] SetSystemFreeInput dto)
    {
        var plan = await db.SubscriptionPlanConfigs.FindAsync(id);
        if (plan is null) return NotFound();

        if (plan.IsSystemFree == dto.IsSystemFree)
            return Ok(await BuildAdminPlanDtoAsync(plan));

        if (!dto.IsSystemFree && plan.IsSystemFree)
        {
            // §43.4 says exactly one system free plan must exist AT ALL TIMES. This endpoint does NOT
            // guarantee that invariant, and cannot: moving the flag from one plan to another is
            // necessarily two separate requests (turn the old one off, then turn the new one on), and
            // between those two requests the system genuinely has ZERO system-free plans for however
            // long the admin takes to make the second call — there is no transaction spanning both. The
            // check below only guards against the WORSE failure of a permanent dead end: it refuses to
            // turn this plan's flag off unless another active, zero-priced candidate already exists to
            // receive it, so the flag can always eventually be moved. It does not, and cannot, stop an
            // admin from leaving the system without a system-free plan indefinitely by simply never
            // making the second call. Before this guard existed, turning the flag off was refused
            // unconditionally, which made moving it to another plan impossible altogether (cycle-07 QA
            // finding #1) — the seeded plan could never be replaced.
            var transferCandidateExists = await db.SubscriptionPlanConfigs
                .AnyAsync(p => p.Id != id && p.Line == plan.Line && p.IsActive && p.PricePerMonth == 0);
            if (!transferCandidateExists)
                return Conflict("Ровно один тариф должен быть системным бесплатным — создайте или подготовьте тариф с ценой 0, прежде чем снимать этот флаг.");
        }

        // Code-review finding — symmetric to SetSystemTrial's own check (`plan.IsSystemFree` there):
        // a trial plan can never also become the system free plan. Checked here explicitly rather than
        // relying only on ValidateSystemFreeAsync's own-price/other-system-free checks, because on an
        // otherwise-empty database (no system free plan seeded yet) those checks pass fine even for a
        // trial plan — which would silently produce one row with BOTH flags set, exactly what the two
        // flags together are supposed to make impossible.
        if (dto.IsSystemFree && plan.IsSystemTrial)
            return Conflict("Этот тариф уже пробный период — тариф не может быть одновременно системным бесплатным.");

        var systemFreeError = await ValidateSystemFreeAsync(dto.IsSystemFree, plan.PricePerMonth, existingPlanId: id, plan.Line);
        if (systemFreeError is not null) return systemFreeError;

        plan.IsSystemFree = dto.IsSystemFree;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("Another plan is already marked as the system free plan.");
        }
        pricingCatalogCache.Invalidate();
        return Ok(await BuildAdminPlanDtoAsync(plan));
    }

    /// <summary>
    /// Cycle 18 (API_CONTRACT_CYCLE18.md §366) — the trial-plan flag, following exactly the same
    /// separate-endpoint pattern as <see cref="SetSystemFree"/> above and for the same reason: keeping
    /// it out of <see cref="AdminPlanInput"/> means an ordinary field edit can never accidentally flip
    /// it, and the partial unique index on IsSystemTrial (AppDbContext) is still the real guard against
    /// a race, this endpoint's own check is just the friendly 409.
    /// </summary>
    [HttpPut("plans/{id:guid}/system-trial")]
    public async Task<IActionResult> SetSystemTrial(Guid id, [FromBody] SetSystemTrialInput dto)
    {
        var plan = await db.SubscriptionPlanConfigs.FindAsync(id);
        if (plan is null) return NotFound();

        // Idempotent — same value is a no-op 200 (§366).
        if (plan.IsSystemTrial == dto.IsSystemTrial)
            return Ok(await BuildAdminPlanDtoAsync(plan));

        if (dto.IsSystemTrial)
        {
            // ARCHITECTURE_CYCLE24.md §459.5: the trial period exists only for the "Записи" line.
            if (plan.Line != CompanyKind.Services)
                return Conflict("Пробный период есть только у тарифов «Записи»");
            if (plan.PricePerMonth != 0)
                return BadRequest("Триал не оплачивается — цена тарифа должна быть равна 0.");
            if (plan.IsSystemFree)
                return Conflict("Этот тариф уже системный бесплатный — тариф не может быть одновременно триалом.");
            var anotherTrialExists = await db.SubscriptionPlanConfigs.AnyAsync(p => p.Id != id && p.IsSystemTrial);
            if (anotherTrialExists)
                return Conflict("Другой тариф уже помечен как пробный период.");
        }

        plan.IsSystemTrial = dto.IsSystemTrial;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("Другой тариф уже помечен как пробный период.");
        }
        pricingCatalogCache.Invalidate();
        return Ok(await BuildAdminPlanDtoAsync(plan));
    }

    [HttpDelete("plans/{id:guid}")]
    public async Task<IActionResult> DeletePlan(Guid id)
    {
        var plan = await db.SubscriptionPlanConfigs.FindAsync(id);
        if (plan is null) return NotFound();

        // ARCHITECTURE_CYCLE7.md §43.4: the system free plan can be neither deleted nor deactivated —
        // deleting it (this endpoint only soft-deletes via IsActive = false) removes the "Бесплатно" row
        // from the public price list and, like UpdatePlan's deactivation guard above, would push any
        // future free-tier account onto the hardcoded EffectivePlan.Free fallback instead of this row.
        if (plan.IsSystemFree)
            return Conflict("The system free plan cannot be deleted.");

        // Cycle 18 (API_CONTRACT_CYCLE18.md §366) — same reasoning as the system-free guard above: the
        // trial plan is the one row TrialActivationService looks up by IsSystemTrial, and removing it
        // would turn "trial is offered" into a silent TrialNotOffered for every future activation.
        if (plan.IsSystemTrial)
            return Conflict("Тариф пробного периода нельзя удалить.");

        // Deactivating a plan that still has active subscribers would silently strip their features on
        // their very next request (SubscriptionResolver.Resolve treats PlanConfig.IsActive == false as
        // Free) — the admin must move them off the plan first
        // (PUT /api/admin/billing-accounts/{accountId}/subscription, AdminBillingController).
        var subscriberCount = await ActiveSubscribersAsync(id);
        if (subscriberCount > 0)
            return Conflict($"Cannot delete a plan with {subscriberCount} active subscriber(s). Move them to another plan first.");

        plan.IsActive = false;
        await db.SaveChangesAsync();
        pricingCatalogCache.Invalidate();
        return NoContent();
    }

    /// <summary>
    /// Cycle 22 D8 — the single-plan admin DTO every plan endpoint returns (create, update, the two
    /// flag endpoints): active subscribers of this plan, its option rules, the catalog size. A plan
    /// created in this very request has no subscribers — <paramref name="isNew"/> skips that count.
    /// </summary>
    private async Task<AdminPlanDto> BuildAdminPlanDtoAsync(SubscriptionPlanConfig plan, bool isNew = false)
    {
        var subscribedAccounts = isNew ? 0 : await ActiveSubscribersAsync(plan.Id);
        var rules = await db.PlanOptionRules.WhereNotRetired().Where(r => r.PlanConfigId == plan.Id).ToListAsync();
        var totalOptionsInCatalog = await db.SubscriptionOptions.WhereNotRetired().CountAsync();
        return MapAdminPlanDto(plan, subscribedAccounts, rules, totalOptionsInCatalog);
    }

    private async Task<Dictionary<Guid, int>> GetActiveSubscriberCountsAsync(IEnumerable<Guid> planIds)
    {
        var ids = planIds.ToList();
        var services = await db.AccountSubscriptions
            .Where(s => s.IsActive && s.PlanConfigId.HasValue && ids.Contains(s.PlanConfigId.Value))
            .GroupBy(s => s.PlanConfigId!.Value)
            .Select(g => new { PlanConfigId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PlanConfigId, x => x.Count);
        // ARCHITECTURE_CYCLE24.md §459.5: a "Заказы" plan's subscribers live in OrdersSubscriptions.
        var orders = await db.OrdersSubscriptions
            .Where(s => s.IsActive && s.PlanConfigId.HasValue && ids.Contains(s.PlanConfigId.Value))
            .GroupBy(s => s.PlanConfigId!.Value)
            .Select(g => new { PlanConfigId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PlanConfigId, x => x.Count);
        foreach (var (planId, count) in orders) services[planId] = services.GetValueOrDefault(planId) + count;
        return services;
    }

    /// <summary>Active subscribers of ONE plan across both tables — the guard of deleting / deactivating a plan (§459.5).</summary>
    private async Task<int> ActiveSubscribersAsync(Guid planId) =>
        await db.AccountSubscriptions.CountAsync(s => s.PlanConfigId == planId && s.IsActive) +
        await db.OrdersSubscriptions.CountAsync(s => s.PlanConfigId == planId && s.IsActive);

    // contracts/cycle7/openapi.yaml AdminPlanDto: projects the entity onto the contract shape rather than
    // returning it directly — the entity also carries AllowNotificationChannel and CreatedAt (neither
    // in the schema, which sets additionalProperties: false) and stores Highlights as a single
    // newline-separated string rather than the array the schema requires. `options` is now the real
    // PlanOptionRule matrix for this plan (cycle-07 backend report fixes the earlier always-`[]` gap);
    // an option with no row is Unavailable by the schema's own documented default, so it's simply
    // omitted here rather than materialized as an explicit Unavailable row.
    internal static AdminPlanDto MapAdminPlanDto(SubscriptionPlanConfig plan, int subscribedAccounts, List<PlanOptionRule> rules, int? totalOptionsInCatalog = null)
    {
        var configured = rules.Count(r => r.Availability != OptionAvailability.Unavailable);
        var total = totalOptionsInCatalog ?? configured;
        return new(
            plan.Id, plan.Name, plan.Description, SplitHighlights(plan.Highlights), plan.PricePerMonth, "RUB",
            plan.MaxEmployees, plan.MaxCompanies, plan.AllowOnlineBooking, plan.AllowMailing, plan.AllowAnalytics,
            plan.AllowPublicListing, plan.AllowOnlinePayment, plan.PhotoQuotaMb, plan.PhotoRetention,
            plan.NotifyDaysBefore, plan.IsPublic, plan.IsActive, plan.IsSystemFree, plan.SortOrder,
            Options: rules.Where(r => r.Availability != OptionAvailability.Unavailable)
                .Select(r => new AdminPlanOptionRuleDto(r.OptionId, r.Availability.ToString(), r.IncludedQuantity)).ToList(),
            subscribedAccounts,
            IsSystemTrial: plan.IsSystemTrial,
            OptionCoverage: new AdminPlanOptionCoverageDto(configured, total, $"В тариф включено {configured} из {total} опций каталога"),
            Line: plan.Line, MaxProductsPerShop: plan.MaxProductsPerShop, MaxOrdersPerMonth: plan.MaxOrdersPerMonth, AllowOrders: plan.AllowOrders);
    }

    // N25 — shares its cap with PricingCatalogBuilder.MaxHighlights so the admin editor and the public
    // storefront agree on how many bullets survive.
    internal static List<string> SplitHighlights(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(Services.Billing.PricingCatalogBuilder.MaxHighlights).ToList();

    private static string? JoinHighlights(List<string>? highlights) =>
        highlights is null || highlights.Count == 0
            ? null
            : string.Join('\n', highlights.Take(Services.Billing.PricingCatalogBuilder.MaxHighlights));

    // SubscriptionPlanConfigs.PricePerMonth is an unbounded `numeric` column (AppDbContext/migrations),
    // so an extreme value here doesn't overflow the DB the way SubscriptionOption.PricePerMonth's
    // `numeric(10,2)` does. Bounded anyway (cycle-07 backend report, item 3): the pricing screen treats
    // plans and options as one catalog, a plan's price and a subscribed option's price are summed in the
    // same `decimal` arithmetic (BillingCalculator.TotalMonthlyPrice), and a denormalized plan price is
    // exactly the kind of value that turns a later addition/multiplication into an OverflowException
    // (an unhandled 500) even though nothing overflowed at write time. Same ceiling as
    // AdminBillingController.MaxOptionPricePerMonth so the two halves of the catalog agree on what
    // "too large" means.
    public const decimal MaxPlanPricePerMonth = 99_999_999.99m;

    internal static IActionResult? ValidatePlanInput(AdminPlanInput dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length > 100)
            return new BadRequestObjectResult("Название тарифа обязательно (до 100 символов).");
        if (dto.PricePerMonth < 0)
            return new BadRequestObjectResult("Цена не может быть отрицательной.");
        if (dto.PricePerMonth > MaxPlanPricePerMonth)
            return new BadRequestObjectResult($"Цена не может превышать {MaxPlanPricePerMonth}.");
        if (dto.PhotoQuotaMb is < 0)
            return new BadRequestObjectResult("Photo quota must not be negative.");
        if (dto.MaxEmployees is < 0)
            return new BadRequestObjectResult("MaxEmployees must not be negative.");
        if (dto.MaxCompanies is < 0)
            return new BadRequestObjectResult("MaxCompanies must not be negative.");
        if (dto.MaxProductsPerShop is < 1)
            return new BadRequestObjectResult("Макс. товаров в магазине — не меньше 1 (пусто — без ограничения).");
        if (dto.MaxOrdersPerMonth is < 1)
            return new BadRequestObjectResult("Заказов в месяц — не меньше 1 (пусто — без ограничения).");
        var highlightsError = Services.Billing.PricingCatalogBuilder.ValidateHighlights(dto.Highlights);
        if (highlightsError is not null)
            return new BadRequestObjectResult(highlightsError);
        return null;
    }

    // contracts/cycle7/openapi.yaml AdminPlanInput.options: the FULL desired availability matrix for the plan —
    // rows not present are removed (an option that used to be Included/Extra and is now omitted becomes
    // Unavailable), matching the contract's "полная матрица" wording for the read side.
    // B6: an unknown Availability string or a nonexistent OptionId used to throw (Enum.Parse, no
    // existence check) and surface as a 500 instead of a 400 — validate everything up front, without
    // writing anything, before touching the DbContext.
    private async Task<IActionResult?> ApplyOptionRulesAsync(Guid planId, List<AdminPlanOptionRuleDtoV2>? desired)
    {
        var desiredList = desired ?? [];

        foreach (var d in desiredList)
        {
            if (!Enum.TryParse<OptionAvailability>(d.Availability, out _))
                return new BadRequestObjectResult($"Неизвестное значение availability: «{d.Availability}».");
            // NB-7 — an unvalidated negative IncludedQuantity feeds straight into
            // BillingCalculator.MonthlyPriceFor's Math.Max(0, quantity - includedQuantity), which for a
            // negative includedQuantity charges for MORE than the account actually bought.
            if (d.IncludedQuantity is < 0)
                return new BadRequestObjectResult("IncludedQuantity must not be negative.");
        }

        var optionIds = desiredList.Select(d => d.OptionId).ToList();
        var knownOptions = await db.SubscriptionOptions.Where(o => optionIds.Contains(o.Id)).ToListAsync();
        var unknown = optionIds.Except(knownOptions.Select(o => o.Id)).ToList();
        if (unknown.Count > 0)
            return new BadRequestObjectResult($"Опция(и) не найдены: {string.Join(", ", unknown)}.");

        // ARCHITECTURE_CYCLE19.md §405/§386.1 (LIM19-005/006) — a retired limit option exists (so it
        // never trips the "not found" check above) but is silently dropped from what gets written:
        // element in the request is discarded, and a previously saved rule for it is never deleted even
        // when it's absent from `desiredList` (a stale cached "Тарифы" tab that submits the whole matrix
        // without it must not erase the row §385.4's report needs).
        var requestRetiredOptionIds = knownOptions.Where(RetiredLimitOptions.IsRetired).Select(o => o.Id).ToHashSet();
        desiredList = desiredList.Where(d => !requestRetiredOptionIds.Contains(d.OptionId)).ToList();

        var existing = await db.PlanOptionRules.Include(r => r.Option).Where(r => r.PlanConfigId == planId).ToListAsync();

        foreach (var row in existing.Where(e =>
            desiredList.All(d => d.OptionId != e.OptionId) && !RetiredLimitOptions.IsRetired(e.Option)))
            db.PlanOptionRules.Remove(row);

        foreach (var d in desiredList)
        {
            var availability = Enum.Parse<OptionAvailability>(d.Availability);
            var row = existing.FirstOrDefault(e => e.OptionId == d.OptionId);
            if (row is null)
            {
                db.PlanOptionRules.Add(new PlanOptionRule
                {
                    Id = Guid.NewGuid(), PlanConfigId = planId, OptionId = d.OptionId,
                    Availability = availability, IncludedQuantity = d.IncludedQuantity,
                });
            }
            else
            {
                row.Availability = availability;
                row.IncludedQuantity = d.IncludedQuantity;
            }
        }
        return null;
    }

    /// <summary>ARCHITECTURE_CYCLE7.md §43.4 (cycle 24, §448.1: PER LINE): exactly one row of a line may have <c>IsSystemFree == true</c>, and
    /// that row's price must be 0 — validated here so a violation surfaces as 400/409 instead of the
    /// partial unique index throwing a raw <c>DbUpdateException</c> (500) on save. The
    /// <see cref="DbUpdateException"/> catch around <c>SaveChangesAsync</c> callers still handles the
    /// race where two concurrent requests both pass this check before either commits.</summary>
    private async Task<IActionResult?> ValidateSystemFreeAsync(bool isSystemFree, decimal pricePerMonth, Guid? existingPlanId, CompanyKind line)
    {
        if (!isSystemFree) return null;

        if (pricePerMonth != 0)
            return BadRequest("The system free plan must have PricePerMonth = 0.");

        var otherSystemFreeExists = await db.SubscriptionPlanConfigs
            .AnyAsync(p => p.IsSystemFree && p.Line == line && p.Id != (existingPlanId ?? Guid.Empty));
        if (otherSystemFreeExists)
            return Conflict("Another plan is already marked as the system free plan.");

        return null;
    }
}

// ── DTOs ───────────────────────────────────────────────────────────────────────

// contracts/cycle7/openapi.yaml AdminPlanDto/PlanOptionRuleDto — `Options` is always empty (see MapAdminPlanDto)
// until the BillingAccount option catalog exists (cycle-07 backend report).
public record AdminPlanOptionRuleDto(Guid OptionId, string Availability, int? IncludedQuantity);

public record AdminPlanDto(
    Guid Id, string Name, string? Description, List<string> Highlights, decimal PricePerMonth, string Currency,
    int? MaxEmployees, int? MaxCompanies, bool AllowOnlineBooking, bool AllowMailing, bool AllowAnalytics,
    bool AllowPublicListing, bool AllowOnlinePayment, int? PhotoQuotaMb, PhotoRetention PhotoRetention,
    int NotifyDaysBefore, bool IsPublic, bool IsActive, bool IsSystemFree, int SortOrder,
    List<AdminPlanOptionRuleDto> Options, int SubscribedAccounts,
    bool IsSystemTrial = false, AdminPlanOptionCoverageDto? OptionCoverage = null,
    // Cycle 24 (API_CONTRACT_CYCLE24.md §485.3).
    CompanyKind Line = CompanyKind.Services, int? MaxProductsPerShop = null, int? MaxOrdersPerMonth = null, bool AllowOrders = true);

// Cycle 18 (API_CONTRACT_CYCLE18.md §366) — "отсутствие строки PlanOptionRule = Unavailable" is
// fail-closed behaviour, not a defect, but a superadmin must be able to SEE it on the plan's own card.
public record AdminPlanOptionCoverageDto(int Configured, int Total, string Text);

public record SetSystemTrialInput(bool IsSystemTrial);

public record AdminPlansListDto(List<AdminPlanDto> Plans);
