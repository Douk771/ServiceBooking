using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>contracts/cycle7/openapi.yaml tag billing-admin — options catalog, billing accounts,
/// subscription assignment and the owner request queue (US-66, US-67, US-70). Kept as a separate
/// controller from <see cref="AdminController"/> (same "api/admin" route prefix, same SuperAdmin-only
/// authorization) purely so this cycle's diff doesn't grow an already-780-line file further.
/// Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): the options catalog moved to <see cref="AdminOptionsController"/>
/// and the trial routes to <see cref="AdminTrialController"/> — same prefix, same gate, same routes.</summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "SuperAdmin")]
public class AdminBillingController(
    AppDbContext db,
    SubscriptionResolver subscriptionResolver, AccountUsageReader usageReader,
    OwnerSubscriptionService ownerSubscriptionService,
    ILogger<AdminBillingController> logger) : ControllerBase
{
    // B5: PostgreSQL "timestamp with time zone" columns require Kind == Utc; DateOnly.ToDateTime always
    // yields Kind == Unspecified, which Npgsql rejects at runtime (500) rather than silently coercing.
    private static DateTime? ToUtc(DateOnly? date) =>
        date is null ? null : DateTime.SpecifyKind(date.Value.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);

    // ── Billing accounts (US-67) ──────────────────────────────────────────────────

    [HttpGet("billing-accounts")]
    public async Task<IActionResult> GetBillingAccounts(
        [FromQuery] string? search, [FromQuery] string? status, [FromQuery] string? trial,
        [FromQuery] int? page, [FromQuery] int? pageSize,
        [FromQuery] string? showcase,
        CancellationToken ct)
    {
        if (!ShowcaseFilterParser.TryParse(showcase, out var showcaseFilter)) return BadRequest(ShowcaseFilterParser.InvalidText);

        // Cycle 18 (API_CONTRACT_CYCLE18.md §369) — unlike `status` above (which quietly matches nothing
        // on an unrecognized value, same as the pre-cycle-18 behaviour), `trial` is a NEW parameter with
        // no legacy caller to stay silently compatible with, so an unrecognized value is a 400 rather
        // than a filter that silently returns zero rows.
        string[] validTrialValues = ["never", "active", "used"];
        if (trial is not null && !validTrialValues.Contains(trial, StringComparer.OrdinalIgnoreCase))
            return BadRequest("trial должен быть одним из: never, active, used.");

        var (currentPage, currentPageSize) = Pagination.Normalize(page, pageSize);
        search = Pagination.SanitizeSearch(search);
        var now = DateTime.UtcNow;

        // N19 — status, search and pagination all happen in SQL now; only the page's own rows (plus the
        // handful of batched lookups below, keyed by just those rows' ids) are ever materialized, not
        // every billing account in the system. The inline CASE below must stay behaviourally identical
        // to OwnerSubscriptionService.SubscriptionStatusFor — see OwnerSubscriptionStatusTests for the
        // truth table both are checked against — because EF Core cannot translate a call to that shared method
        // into SQL; it can only translate an expression written out in the query itself.
        var joined =
            from a in db.BillingAccounts.Include(a => a.Owner)
            join s in db.AccountSubscriptions.Include(s => s.PlanConfig) on a.Id equals s.BillingAccountId into subGroup
            from sub in subGroup.DefaultIfEmpty()
            select new { a, sub };

        // ARCHITECTURE_CYCLE28.md §594.1 — composes with search/status/trial below.
        joined = showcaseFilter switch
        {
            ShowcaseFilter.Only => joined.Where(x => x.a.IsShowcase),
            ShowcaseFilter.Exclude => joined.Where(x => !x.a.IsShowcase),
            _ => joined,
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            // NB-6 — phones are stored canonical (digits only, US-26); a search string that LOOKS like
            // a phone number must be normalized the same way before matching PhoneNumber, same
            // convention as AdminController.GetUsers, or a formatted phone ("+7 (999) 123-45-67")
            // never matches anything.
            var phoneSearch = PhoneNormalizer.ParseSearch(search).Term;

            joined = joined.Where(x =>
                x.a.Owner.Email!.Contains(search) ||
                (phoneSearch.Length > 0 && x.a.Owner.PhoneNumber!.Contains(phoneSearch)) ||
                x.a.Owner.FirstName.Contains(search) || x.a.Owner.LastName.Contains(search) ||
                db.Companies.Any(c => c.BillingAccountId == x.a.Id && c.Name.Contains(search)));
        }

        // The status/trialState CASE below spells the "subscription in force" rule out by hand
        // (cycle 18 B4): it runs in SQL over a LEFT JOINed, possibly-null subscription, where the
        // SubscriptionUsability.UsableAt expression can't be spliced in. Keep it in step with
        // SubscriptionUsability (cycle 22 D1) — the one definition of the rule.
        var withStatus = joined.Select(x => new
        {
            x.a,
            x.sub,
            status = x.sub == null || x.sub.PlanConfigId == null ? "Free"
                : !x.sub.IsActive || (x.sub.PaidUntil.HasValue && x.sub.PaidUntil < now) ? "Expired"
                : "Active",
            // Cycle 18 (§369) — "Active" needs both a live trial GRANT and a still-usable subscription
            // (an account whose trial was granted but has since expired/switched to Free has
            // TrialStartedAtUtc set but is no longer usable — that's Expired, not Active).
            trialState = x.a.TrialStartedAtUtc == null ? "Never"
                : x.sub != null && x.sub.IsActive && (!x.sub.PaidUntil.HasValue || x.sub.PaidUntil >= now)
                    && x.a.TrialEndsAtUtc.HasValue && x.a.TrialEndsAtUtc >= now ? "Active"
                : "Expired",
        });

        if (!string.IsNullOrWhiteSpace(status))
        {
            // Normalize once in C# against the three known values (case-insensitively, as the old
            // in-memory filter did) so the SQL comparison itself can stay a simple, translatable
            // equality rather than an untranslatable StringComparison.OrdinalIgnoreCase call.
            var canonicalStatus = new[] { "Free", "Active", "Expired" }
                .FirstOrDefault(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));
            // An unrecognized status value matches nothing — same as the old in-memory
            // string.Equals(...) filter, which also never matched an unknown value.
            withStatus = withStatus.Where(x => x.status == (canonicalStatus ?? string.Empty));
        }

        if (trial is not null)
        {
            var canonicalTrial = validTrialValues.First(v => string.Equals(v, trial, StringComparison.OrdinalIgnoreCase));
            var canonicalTrialState = canonicalTrial switch { "active" => "Active", "used" => "Expired", _ => "Never" };
            withStatus = withStatus.Where(x => x.trialState == canonicalTrialState);
        }

        var total = await withStatus.CountAsync(ct);
        var page1 = await withStatus.OrderBy(x => x.a.CreatedAtUtc)
            .Skip((currentPage - 1) * currentPageSize).Take(currentPageSize)
            .ToListAsync(ct);

        var accountIds = page1.Select(x => x.a.Id).ToList();
        var plans = await subscriptionResolver.GetEffectivePlansForAccountsAsync(accountIds);
        var usages = await usageReader.GetAsync(accountIds);

        // N4 — the same two figures the account's own card already gets right (BuildAccountCardAsync):
        // numbersRegistered from an actual COUNT, and totalMonthlyPrice including every subscribed
        // option's price, not just the bare plan. Batched (§46) — one query for every account's options,
        // one for every account's registered-channel count, not one per row — and now scoped to just
        // this page's account ids rather than every account in the system.
        // ARCHITECTURE_CYCLE19.md §386.1 — retired limit options never contribute to totalMonthlyPrice
        // or the "options" surfaces shown here.
        var subscribedOptions = await db.AccountSubscriptionOptions.WhereNotRetired().Include(o => o.Option)
            .Where(o => accountIds.Contains(o.BillingAccountId))
            .Where(o => o.EndsAtUtc == null || o.EndsAtUtc > now)
            .ToListAsync(ct);
        var planConfigIds = page1.Where(x => x.sub != null && x.sub.PlanConfigId.HasValue)
            .Select(x => x.sub!.PlanConfigId!.Value).Distinct().ToList();
        var planRules = planConfigIds.Count == 0
            ? []
            : await db.PlanOptionRules.Where(r => planConfigIds.Contains(r.PlanConfigId)).ToListAsync(ct);
        var registeredCounts = await db.NotificationChannels
            .Where(c => c.BillingAccountId != null && accountIds.Contains(c.BillingAccountId!.Value) && c.State != ChannelState.Replaced)
            .GroupBy(c => c.BillingAccountId!.Value)
            .Select(g => new { AccountId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.AccountId, g => g.Count, ct);

        var result = page1.Select(x =>
        {
            var a = x.a;
            var sub = x.sub;
            var plan = plans.GetValueOrDefault(a.Id, EffectivePlan.Free);
            var usage = usages.GetValueOrDefault(a.Id) ?? new AccountUsage(a.Id, 0, 0);

            var accountOptions = subscribedOptions.Where(o => o.BillingAccountId == a.Id);
            var optionsMonthly = accountOptions.Select(o =>
            {
                var rule = sub?.PlanConfigId is { } subPlanConfigId
                    ? planRules.FirstOrDefault(r => r.OptionId == o.OptionId && r.PlanConfigId == subPlanConfigId)
                    : null;
                var availability = rule?.Availability ?? OptionAvailability.Unavailable;
                return BillingCalculator.MonthlyPriceFor(availability, o.Quantity, o.Option.PricePerMonth ?? 0m, rule?.IncludedQuantity);
            });
            var totalMonthlyPrice = BillingCalculator.TotalMonthlyPrice(sub?.PlanConfig?.PricePerMonth ?? 0m, optionsMonthly);

            return new
            {
                id = a.Id,
                name = a.Name,
                ownerUserId = a.OwnerUserId,
                ownerName = $"{a.Owner.FirstName} {a.Owner.LastName}".Trim(),
                ownerPhoneMasked = a.Owner.PhoneNumber is null ? null : PhoneDisplayMask.Mask(a.Owner.PhoneNumber),
                planName = sub?.PlanConfig?.Name,
                status = x.status,
                statusText = OwnerSubscriptionService.StatusTextFor(x.status, sub?.PaidUntil),
                paidUntil = sub?.PaidUntil,
                totalMonthlyPrice,
                currency = "RUB",
                companiesUsed = usage.CompaniesUsed,
                companiesLimit = plan.AccountMaxCompanies,
                employeesUsed = usage.SeatsUsed,
                employeesLimit = plan.AccountMaxEmployees,
                numbersPaid = plan.PaidNotificationNumbers,
                numbersRegistered = registeredCounts.GetValueOrDefault(a.Id, 0),
                hasPendingRequest = a.RequestedAtUtc is not null,
                trialState = x.trialState,
                trialEndsAt = a.TrialEndsAtUtc,
                isShowcase = a.IsShowcase,
            };
        }).ToList();

        return Ok(Pagination.CreateContract(result, currentPage, currentPageSize, total));
    }

    [HttpGet("billing-accounts/{accountId:guid}")]
    public async Task<IActionResult> GetBillingAccount(Guid accountId, CancellationToken ct)
    {
        var account = await db.BillingAccounts.Include(a => a.Owner).Include(a => a.RequestedPlan)
            .FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null) return NotFound();

        var dto = await BuildAdminAccountDtoAsync(account);
        return Ok(dto);
    }

    // Cycle 22 P5 (§385): shared with the other half of the former AdminBillingController — the body lives
    // in AdminAccountDtoBuilder, unchanged.
    private Task<object> BuildAdminAccountDtoAsync(BillingAccount account) =>
        AdminAccountDtoBuilder.BuildAsync(db, subscriptionResolver, usageReader, ownerSubscriptionService, account);

    [HttpPut("billing-accounts/{accountId:guid}/subscription")]
    public async Task<IActionResult> AssignSubscription(Guid accountId, [FromBody] AssignSubscriptionInput dto)
    {
        var account = await db.BillingAccounts.Include(a => a.Owner).FirstOrDefaultAsync(a => a.Id == accountId);
        if (account is null) return NotFound();
        if (dto.Line is { } requestedLine && !Enum.IsDefined(requestedLine)) return BadRequest("Неизвестная линейка.");
        if (dto.Line is CompanyKind.Orders or CompanyKind.Stays or CompanyKind.Baths) return await AssignOrdersSubscriptionAsync(account, dto, dto.Line.Value);
        // A request of the "Заказы" / «Дома» / «Бани» line is closed by an assignment of that line only.
        if (dto.RequestId.HasValue && account.RequestedAtUtc is not null && account.RequestedLine is CompanyKind.Orders or CompanyKind.Stays or CompanyKind.Baths)
            return Conflict("Заявка уже обработана.");

        if (dto.PaidUntil is { } paidUntilDate && paidUntilDate.ToDateTime(TimeOnly.MinValue) < DateTime.UtcNow.Date.AddDays(-1))
            return BadRequest("Дата окончания оплаты не может быть в прошлом.");

        if (SubscriptionAssignmentValidator.RequiresPaidUntil(dto.PlanId, dto.PaidUntil))
            return BadRequest(SubscriptionAssignmentValidator.MissingPaidUntilError);

        // B6: contract requires an omitted `options` field to behave as "no options", not to 500.
        var optionLines = dto.Options ?? [];

        if (dto.RequestId.HasValue && account.RequestedAtUtc is null)
            return Conflict("Заявка уже обработана.");
        if (dto.RequestId.HasValue && dto.RequestId.Value != accountId)
            return Conflict("Заявка уже обработана.");

        SubscriptionPlanConfig? plan = null;
        if (dto.PlanId.HasValue)
        {
            plan = await db.SubscriptionPlanConfigs.FindAsync(dto.PlanId.Value);
            if (plan is null) return NotFound("Тариф не найден.");
            // ARCHITECTURE_CYCLE24.md §459.5: a tariff of the "Заказы" line goes through line = Orders only.
            if (plan.Line != CompanyKind.Services) return BadRequest(BillingTexts.AdminDifferentLine);

            // Б... (code review, cycle 18 4th pass) — the trial plan is materialized ONLY through
            // TrialActivationService.GrantAsync (owner self-service and superadmin grant/regrant), never
            // through this general-purpose assignment route: those paths are the only ones carrying the
            // one-trial-per-account/one-phone-per-trial checks (§335.2), the mailing-window reset (Б3,
            // cycle 18 3rd pass), and set TrialExpiredHandledAtUtc = null on every fresh grant. Assigning
            // the trial plan here would produce an account sitting on the trial plan with a STALE
            // TrialExpiredHandledAtUtc (or none of the Trial* snapshot columns at all) — TrialLifecycleTask's
            // expiry phase (§337.1 п.4) filters on TrialExpiredHandledAtUtc == null, so such an account would
            // never expire, not this hour, not ever.
            if (plan.IsSystemTrial)
                return Conflict(new DTOs.Billing.TrialRefusalDto(
                    "TrialPlanNotAssignableHere",
                    "Пробный тариф нельзя назначить через это действие — используйте выдачу/повторную выдачу пробного периода."));

            // ARCHITECTURE_CYCLE28.md §574.2, API_CONTRACT_CYCLE28.md §595 — the hidden service tariff of the showcase never goes to a real account.
            if (ShowcaseMixingGuard.CheckServicePlan(plan.Id, account.IsShowcase) is { } servicePlanRefusal)
                return Conflict(servicePlanRefusal);
        }

        // ARCHITECTURE_CYCLE20.md §403.2 (US-20-02, Т20-01) — runs AFTER the trial refusal above (Р6:
        // a trial-plan assignment is rejected first no matter which reason was supplied) and BEFORE any
        // write. currentPlanId is read directly rather than via the (not-yet-loaded) `sub` below, so a
        // reason mistake is caught before any option/limit checks run their own queries.
        var currentPlanId = await db.AccountSubscriptions
            .Where(s => s.BillingAccountId == accountId).Select(s => (Guid?)s.PlanConfigId).FirstOrDefaultAsync();
        var reasonRequired = Services.Billing.ManualPlanAssignmentPolicy.RequiresReason(currentPlanId, dto.PlanId, plan?.IsPublic ?? true);
        var reasonError = Services.Billing.ManualPlanAssignmentPolicy.Validate(dto.ReasonCode, dto.ReasonDetails, reasonRequired);
        if (reasonError is not null)
            return BadRequest(reasonError);

        var optionIds = optionLines.Select(o => o.OptionId).ToList();
        var options = await db.SubscriptionOptions.Where(o => optionIds.Contains(o.Id)).ToListAsync();
        if (options.Count != optionIds.Distinct().Count())
            return BadRequest("Одна или несколько опций не найдены.");

        // ARCHITECTURE_CYCLE19.md §406 п.8 — a retired limit option in the request is rejected outright;
        // nothing is saved. Checked before quantity/rule validation, as the contract's ordered list requires.
        var retiredInRequest = options.FirstOrDefault(RetiredLimitOptions.IsRetired);
        if (retiredInRequest is not null)
            return BadRequest(BillingTexts.RetiredOptionRejected(retiredInRequest.Name));

        foreach (var line in optionLines)
        {
            if (line.Quantity < 1)
                return BadRequest($"Количество для опции должно быть не меньше 1.");
            var option = options.First(o => o.Id == line.OptionId);
            if (option.MaxQuantity is { } max && line.Quantity > max)
                return BadRequest($"Количество для опции «{option.Name}» не может превышать {max}.");
        }

        var planRules = plan is not null ? await db.PlanOptionRules.Where(r => r.PlanConfigId == plan.Id).ToListAsync() : [];
        foreach (var line in optionLines)
        {
            var rule = planRules.FirstOrDefault(r => r.OptionId == line.OptionId);
            if (plan is not null && (rule is null || rule.Availability == OptionAvailability.Unavailable))
                return Conflict($"Опция недоступна на выбранном тарифе.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"billing-account:{accountId}");

        var changedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var now = DateTime.UtcNow;

        // Limit-overflow guard (US-67's last acceptance criterion): if the newly assigned plan's
        // limit is lower than what's already occupied, refuse without confirmLimitOverflow.
        // ARCHITECTURE_CYCLE19.md §384.3/§406 — options in the request never contribute to the limit any
        // more (they can't be retired limit options at this point, §406 п.8), so the one formula is used.
        var basePlan = plan is not null ? EffectivePlan.FromConfig(plan) : EffectivePlan.Free;
        var (newMaxEmployees, newMaxCompanies) = AccountLimitFormula.Compute(
            basePlan.AccountMaxEmployees, basePlan.AccountMaxCompanies, account.GrandfatheredEmployeeBonus);
        var newEffectivePlan = basePlan with { AccountMaxEmployees = newMaxEmployees, AccountMaxCompanies = newMaxCompanies };
        var usage = (await usageReader.GetAsync([accountId])).GetValueOrDefault(accountId) ?? new AccountUsage(accountId, 0, 0);
        if (!dto.ConfirmLimitOverflow)
        {
            if (newEffectivePlan.AccountMaxCompanies is { } maxCompanies && usage.CompaniesUsed > maxCompanies)
                return Conflict($"На новом тарифе доступно {maxCompanies} компаний, занято {usage.CompaniesUsed}. Подтвердите превышение лимита, чтобы продолжить.");
            if (newEffectivePlan.AccountMaxEmployees is { } maxEmployees && usage.SeatsUsed > maxEmployees)
                return Conflict($"На новом тарифе доступно {maxEmployees} мест, занято {usage.SeatsUsed}. Подтвердите превышение лимита, чтобы продолжить.");
        }

        var sub = await db.AccountSubscriptions.Include(s => s.PlanConfig).FirstOrDefaultAsync(s => s.BillingAccountId == accountId);
        if (sub is null)
        {
            sub = new AccountSubscription { Id = Guid.NewGuid(), OwnerUserId = account.OwnerUserId, BillingAccountId = accountId, CreatedAt = now };
            db.AccountSubscriptions.Add(sub);
        }

        var oldPlanId = sub.PlanConfigId;
        var oldPlanName = sub.PlanConfig?.Name;
        var oldPaidUntil = sub.PaidUntil;
        var oldIsActive = sub.IsActive;
        var oldOptionsSummary = await BuildOptionsSummaryAsync(accountId);

        // §336.3 — moving an account OFF the trial plan early must not leave a stale MailingUntilUtc
        // behind: without this reset, the trial's mailing window would keep silencing PAID mailings on
        // the new plan too (SPEC §4.4). The Trial* markers on the account itself are untouched (§337.3).
        if (oldPlanId is { } previousPlanId && dto.PlanId != previousPlanId)
        {
            var wasTrialPlan = await db.SubscriptionPlanConfigs
                .AnyAsync(p => p.Id == previousPlanId && p.IsSystemTrial);
            if (wasTrialPlan) sub.MailingUntilUtc = null;
        }

        sub.PlanConfigId = dto.PlanId;
        sub.IsActive = dto.IsActive;
        sub.PaidUntil = ToUtc(dto.PaidUntil);
        sub.UpdatedAt = now;

        // ARCHITECTURE_CYCLE19.md §383.2/§386.1 — retired limit option rows are excluded here so this
        // endpoint never creates, updates or closes them, even via the "options not in the request end"
        // loop below.
        var existingOptions = await db.AccountSubscriptionOptions.WhereNotRetired()
            .Where(o => o.BillingAccountId == accountId).ToListAsync();
        foreach (var line in optionLines)
        {
            var row = existingOptions.FirstOrDefault(o => o.OptionId == line.OptionId);
            if (row is null)
            {
                db.AccountSubscriptionOptions.Add(new AccountSubscriptionOption
                {
                    Id = Guid.NewGuid(),
                    BillingAccountId = accountId,
                    OptionId = line.OptionId,
                    Quantity = line.Quantity,
                    PaidUntilUtc = ToUtc(line.PaidUntil),
                    ActivatedAtUtc = now,
                    ActivatedByUserId = changedByUserId,
                });
            }
            else
            {
                row.EndsAtUtc = null;
                row.Quantity = line.Quantity;
                row.PaidUntilUtc = ToUtc(line.PaidUntil);
                row.ActivatedAtUtc = now;
                row.ActivatedByUserId = changedByUserId;
                row.RequestedQuantity = null;
                row.RequestedAtUtc = null;
                row.RequestedByUserId = null;
                // B1 (code review, cycle 18 3rd pass) — an admin write always produces an ordinary
                // (non-trial) row, per the invariant documented on AccountSubscriptionOption.GrantedByTrial.
                // Without this, a row a trial materialized (GrantedByTrial = true) that gets bought out
                // here keeps the flag, and a later emergency re-grant (TrialActivationService.GrantAsync)
                // would mistake this PAID row for its own leftover and silently overwrite its
                // Quantity/PaidUntilUtc back down.
                row.GrantedByTrial = false;
            }
        }
        // Options present before but omitted now (or decreased — decreases are not modeled per-unit,
        // only full removal below; a partial decrease is out of scope for this endpoint's write shape
        // and is treated the same as the option staying at its previous quantity until removed outright,
        // see the cycle-07 backend report) end at the close of the current paid period rather than
        // disappearing immediately (contract: "действует до конца оплаченного периода").
        foreach (var row in existingOptions.Where(r => optionLines.All(l => l.OptionId != r.OptionId) && r.EndsAtUtc is null))
            row.EndsAtUtc = sub.PaidUntil ?? now;

        if (dto.RequestId.HasValue)
        {
            account.RequestedPlanId = null;
            account.RequestedOptionsJson = null;
            account.RequestedAtUtc = null;
            account.RequestedByUserId = null;
            account.RequestedComment = null;
            // N10 — an approved request has nothing left to explain a rejection for.
            account.LastRejectionReason = null;
            account.LastRejectedAtUtc = null;
        }

        var newOptionsSummary = string.Join(", ", optionLines.Select(o =>
        {
            var name = options.First(x => x.Id == o.OptionId).Name;
            return $"{name} ×{o.Quantity}";
        }));

        db.SubscriptionChangeLogs.Add(new SubscriptionChangeLog
        {
            Id = Guid.NewGuid(),
            OwnerUserId = account.OwnerUserId,
            BillingAccountId = accountId,
            ChangedByUserId = changedByUserId,
            ChangedAt = now,
            OldPlanConfigId = oldPlanId,
            NewPlanConfigId = dto.PlanId,
            OldPaidUntil = oldPaidUntil,
            NewPaidUntil = sub.PaidUntil,
            OldIsActive = oldIsActive,
            NewIsActive = dto.IsActive,
            ChangeKind = oldPlanId != dto.PlanId ? SubscriptionChangeKind.Plan : SubscriptionChangeKind.Options,
            OldOptionsSummary = oldOptionsSummary,
            NewOptionsSummary = newOptionsSummary,
            Comment = dto.Comment,
            // ARCHITECTURE_CYCLE20.md §403.1 — written whenever supplied, even where not required
            // (§433.1: "причина, присланная там, где она не обязательна, принимается и пишется").
            ReasonCode = dto.ReasonCode,
            ReasonDetails = dto.ReasonDetails,
        });

        account.UpdatedAtUtc = now;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        // §59: Information-level log for a subscription assignment — who, which account, which plan,
        // and the resulting monthly sum.
        logger.LogInformation(
            "Subscription assigned to billing account {AccountId} by {UserId}: plan {PlanName}, options total {OptionsSummary}",
            accountId, changedByUserId, plan?.Name ?? "Бесплатный", string.IsNullOrEmpty(newOptionsSummary) ? "—" : newOptionsSummary);

        var freshAccount = await db.BillingAccounts.Include(a => a.Owner).Include(a => a.RequestedPlan).FirstAsync(a => a.Id == accountId);
        return Ok(await BuildAdminAccountDtoAsync(freshAccount));
    }

    /// <summary>
    /// ARCHITECTURE_CYCLE24.md §459.5 — assigns the subscription of the "Заказы" line (<see cref="OrdersSubscription"/>). The checks are those of the "Записи"
    /// assignment, counted within the line: a tariff of the other line is a 400; <c>confirmLimitOverflow</c> compares the new limits (shops, seats) with what the
    /// account already has in this line; the change log row carries the line. The options are the account's (shared by both lines) — the request is their full set.
    /// A reason for a hidden tariff is not REQUIRED here (every "Заказы" tariff is hidden by design, [legal L14]) but is validated and stored when given.
    /// </summary>
    private async Task<IActionResult> AssignOrdersSubscriptionAsync(BillingAccount account, AssignSubscriptionInput dto, CompanyKind kind)
    {
        var accountId = account.Id;
        var vertical = Services.Slots.SlotVerticals.Find(kind);
        if (dto.PaidUntil is { } paidUntilDate && paidUntilDate.ToDateTime(TimeOnly.MinValue) < DateTime.UtcNow.Date.AddDays(-1))
            return BadRequest("Дата окончания оплаты не может быть в прошлом.");
        if (SubscriptionAssignmentValidator.RequiresPaidUntil(dto.PlanId, dto.PaidUntil))
            return BadRequest(SubscriptionAssignmentValidator.MissingPaidUntilError);
        var optionLines = dto.Options ?? [];

        if (dto.RequestId.HasValue && (account.RequestedAtUtc is null || account.RequestedLine != kind || dto.RequestId.Value != accountId))
            return Conflict("Заявка уже обработана.");

        SubscriptionPlanConfig? plan = null;
        if (dto.PlanId.HasValue)
        {
            plan = await db.SubscriptionPlanConfigs.FindAsync(dto.PlanId.Value);
            if (plan is null) return NotFound("Тариф не найден.");
            if (plan.Line != kind) return BadRequest(BillingTexts.AdminDifferentLine);
            // ARCHITECTURE_CYCLE37.md §37.10.2, ARCHITECTURE_CYCLE42.md §42.5.5: the trial of «Дома» / «Бани» is granted only by StaysTrialService (the once-only checks live there).
            if (vertical is not null && plan.Id == vertical.TrialPlanId)
                return Conflict(new DTOs.Billing.TrialRefusalDto("TrialPlanNotAssignableHere",
                    "Пробный тариф нельзя назначить через это действие — он выдаётся владельцем при активации пробного периода."));
            // ARCHITECTURE_CYCLE35.md §35.3.3: the hidden demo tariff of «Заказы» is for showcase accounts only, like the one of «Записи».
            if (ShowcaseMixingGuard.CheckServicePlan(plan.Id, account.IsShowcase) is { } servicePlanRefusal)
                return Conflict(servicePlanRefusal);
        }
        var reasonError = ManualPlanAssignmentPolicy.Validate(dto.ReasonCode, dto.ReasonDetails, required: false);
        if (reasonError is not null) return BadRequest(reasonError);

        var optionIds = optionLines.Select(o => o.OptionId).ToList();
        var options = await db.SubscriptionOptions.Where(o => optionIds.Contains(o.Id)).ToListAsync();
        if (options.Count != optionIds.Distinct().Count()) return BadRequest("Одна или несколько опций не найдены.");
        var retired = options.FirstOrDefault(RetiredLimitOptions.IsRetired);
        if (retired is not null) return BadRequest(BillingTexts.RetiredOptionRejected(retired.Name));
        foreach (var line in optionLines)
        {
            if (line.Quantity < 1) return BadRequest("Количество для опции должно быть не меньше 1.");
            var option = options.First(o => o.Id == line.OptionId);
            if (option.MaxQuantity is { } max && line.Quantity > max)
                return BadRequest($"Количество для опции «{option.Name}» не может превышать {max}.");
        }
        var planRules = plan is not null ? await db.PlanOptionRules.Where(r => r.PlanConfigId == plan.Id).ToListAsync() : [];
        foreach (var line in optionLines)
        {
            var rule = planRules.FirstOrDefault(r => r.OptionId == line.OptionId);
            if (plan is not null && (rule is null || rule.Availability == OptionAvailability.Unavailable))
                return Conflict("Опция недоступна на выбранном тарифе.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"billing-account:{accountId}");
        var changedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var now = DateTime.UtcNow;

        // The new limits vs what is already occupied IN THIS LINE (shops, seats incl. the owner; for «Дома» — published houses; for «Бани» — published resources).
        if (vertical is not null)
        {
            var unitsPublished = await new Services.Stays.StaysPlanResolver(db).CountPublishedUnitsAsync(vertical, accountId);
            var (maxUnits, unitWord) = vertical.Unit switch
            {
                Services.Stays.GateUnit.House => (plan?.MaxHouses, "домов"),
                Services.Stays.GateUnit.Resource => (plan?.MaxResources, "ресурсов"),
                _ => throw new System.Diagnostics.UnreachableException()
            };
            if (!dto.ConfirmLimitOverflow && maxUnits is { } limit && unitsPublished > limit)
                return Conflict($"На новом тарифе доступно {limit} {unitWord}, опубликовано {unitsPublished}. Подтвердите превышение лимита, чтобы продолжить.");
        }
        else if (kind == CompanyKind.Orders)
        {
            var systemFree = await db.SubscriptionPlanConfigs.AsNoTracking().FirstOrDefaultAsync(p => p.IsSystemFree && p.Line == CompanyKind.Orders);
            var newPlan = plan is not null
                ? OrdersPlanResolver.Resolve(new OrdersSubscription { PlanConfig = plan, PlanConfigId = plan.Id, IsActive = true }, systemFree, now)
                : OrdersPlanResolver.Resolve(null, systemFree, now);
            var usage = (await usageReader.GetAsync([accountId], CompanyKind.Orders)).GetValueOrDefault(accountId) ?? new AccountUsage(accountId, 0, 0);
            if (!dto.ConfirmLimitOverflow)
            {
                if (newPlan.MaxShops is { } maxShops && usage.CompaniesUsed > maxShops)
                    return Conflict($"На новом тарифе доступно {maxShops} магазинов, занято {usage.CompaniesUsed}. Подтвердите превышение лимита, чтобы продолжить.");
                if (newPlan.MaxSeats is { } maxSeats && usage.SeatsUsed > maxSeats)
                    return Conflict($"На новом тарифе доступно {maxSeats} мест, занято {usage.SeatsUsed}. Подтвердите превышение лимита, чтобы продолжить.");
            }
        }

        Guid? oldPlanId;
        DateTime? oldPaidUntil;
        bool oldIsActive;
        DateTime? newPaidUntil;
        if (kind == CompanyKind.Baths)
        {
            var bathsSub = await db.BathsSubscriptions.FirstOrDefaultAsync(s => s.BillingAccountId == accountId);
            if (bathsSub is null)
            {
                bathsSub = new BathsSubscription { Id = Guid.NewGuid(), BillingAccountId = accountId, CreatedAtUtc = now };
                db.BathsSubscriptions.Add(bathsSub);
            }
            (oldPlanId, oldPaidUntil, oldIsActive) = (bathsSub.PlanConfigId, bathsSub.PaidUntil, bathsSub.IsActive);
            bathsSub.PlanConfigId = dto.PlanId;
            bathsSub.IsActive = dto.IsActive;
            bathsSub.PaidUntil = ToUtc(dto.PaidUntil);
            bathsSub.UpdatedAtUtc = now;
            bathsSub.UpdatedByUserId = changedByUserId;
            newPaidUntil = bathsSub.PaidUntil;
        }
        else if (kind == CompanyKind.Stays)
        {
            var staysSub = await db.StaysSubscriptions.FirstOrDefaultAsync(s => s.BillingAccountId == accountId);
            if (staysSub is null)
            {
                staysSub = new StaysSubscription { Id = Guid.NewGuid(), BillingAccountId = accountId, CreatedAtUtc = now };
                db.StaysSubscriptions.Add(staysSub);
            }
            (oldPlanId, oldPaidUntil, oldIsActive) = (staysSub.PlanConfigId, staysSub.PaidUntil, staysSub.IsActive);
            staysSub.PlanConfigId = dto.PlanId;
            staysSub.IsActive = dto.IsActive;
            staysSub.PaidUntil = ToUtc(dto.PaidUntil);
            staysSub.UpdatedAtUtc = now;
            staysSub.UpdatedByUserId = changedByUserId;
            newPaidUntil = staysSub.PaidUntil;
        }
        else
        {
            var sub = await db.OrdersSubscriptions.Include(s => s.PlanConfig).FirstOrDefaultAsync(s => s.BillingAccountId == accountId);
            if (sub is null)
            {
                sub = new OrdersSubscription { Id = Guid.NewGuid(), BillingAccountId = accountId, CreatedAtUtc = now };
                db.OrdersSubscriptions.Add(sub);
            }
            (oldPlanId, oldPaidUntil, oldIsActive) = (sub.PlanConfigId, sub.PaidUntil, sub.IsActive);
            sub.PlanConfigId = dto.PlanId;
            sub.IsActive = dto.IsActive;
            sub.PaidUntil = ToUtc(dto.PaidUntil);
            sub.UpdatedAtUtc = now;
            sub.UpdatedByUserId = changedByUserId;
            newPaidUntil = sub.PaidUntil;
        }
        var oldOptionsSummary = await BuildOptionsSummaryAsync(accountId);

        // The account's options: the request is the FULL set (as in the "Записи" assignment); the ones left out end with the paid period.
        var existingOptions = await db.AccountSubscriptionOptions.WhereNotRetired().Where(o => o.BillingAccountId == accountId).ToListAsync();
        foreach (var line in optionLines)
        {
            var row = existingOptions.FirstOrDefault(o => o.OptionId == line.OptionId);
            if (row is null)
                db.AccountSubscriptionOptions.Add(new AccountSubscriptionOption
                {
                    Id = Guid.NewGuid(), BillingAccountId = accountId, OptionId = line.OptionId, Quantity = line.Quantity,
                    PaidUntilUtc = ToUtc(line.PaidUntil), ActivatedAtUtc = now, ActivatedByUserId = changedByUserId,
                });
            else
            {
                row.EndsAtUtc = null;
                row.Quantity = line.Quantity;
                row.PaidUntilUtc = ToUtc(line.PaidUntil);
                row.ActivatedAtUtc = now;
                row.ActivatedByUserId = changedByUserId;
                row.RequestedQuantity = null;
                row.RequestedAtUtc = null;
                row.RequestedByUserId = null;
                row.GrantedByTrial = false;
            }
        }
        foreach (var row in existingOptions.Where(r => optionLines.All(l => l.OptionId != r.OptionId) && r.EndsAtUtc is null))
            row.EndsAtUtc = newPaidUntil ?? now;

        if (dto.RequestId.HasValue)
        {
            account.RequestedLine = null;
            account.RequestedPlanId = null;
            account.RequestedOptionsJson = null;
            account.RequestedAtUtc = null;
            account.RequestedByUserId = null;
            account.RequestedComment = null;
            account.LastRejectionReason = null;
            account.LastRejectedAtUtc = null;
        }

        var newOptionsSummary = string.Join(", ", optionLines.Select(o => $"{options.First(x => x.Id == o.OptionId).Name} ×{o.Quantity}"));
        db.SubscriptionChangeLogs.Add(new SubscriptionChangeLog
        {
            Id = Guid.NewGuid(), OwnerUserId = account.OwnerUserId, BillingAccountId = accountId, ChangedByUserId = changedByUserId, ChangedAt = now,
            OldPlanConfigId = oldPlanId, NewPlanConfigId = dto.PlanId, OldPaidUntil = oldPaidUntil, NewPaidUntil = newPaidUntil,
            OldIsActive = oldIsActive, NewIsActive = dto.IsActive,
            ChangeKind = oldPlanId != dto.PlanId ? SubscriptionChangeKind.Plan : SubscriptionChangeKind.Options,
            OldOptionsSummary = oldOptionsSummary, NewOptionsSummary = newOptionsSummary, Comment = dto.Comment,
            ReasonCode = dto.ReasonCode, ReasonDetails = dto.ReasonDetails,
            Line = kind,
        });
        account.UpdatedAtUtc = now;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        logger.LogInformation(
            "{Line} subscription assigned to billing account {AccountId} by {UserId}: plan {PlanName}, options {OptionsSummary}",
            kind, accountId, changedByUserId, plan?.Name ?? (kind switch { CompanyKind.Orders => OrdersFreePlan.Name, _ => "—" }), string.IsNullOrEmpty(newOptionsSummary) ? "—" : newOptionsSummary);
        var freshAccount = await db.BillingAccounts.Include(a => a.Owner).Include(a => a.RequestedPlan).FirstAsync(a => a.Id == accountId);
        return Ok(await BuildAdminAccountDtoAsync(freshAccount));
    }

    private async Task<string?> BuildOptionsSummaryAsync(Guid accountId)
    {
        var rows = await db.AccountSubscriptionOptions.WhereNotRetired().Include(o => o.Option)
            .Where(o => o.BillingAccountId == accountId && (o.EndsAtUtc == null || o.EndsAtUtc > DateTime.UtcNow)).ToListAsync();
        return rows.Count == 0 ? null : string.Join(", ", rows.Select(r => $"{r.Option.Name} ×{r.Quantity}"));
    }

    [HttpGet("billing-accounts/{accountId:guid}/subscription-history")]
    public async Task<IActionResult> GetSubscriptionHistory(Guid accountId, CancellationToken ct)
    {
        var ownerUserId = await db.BillingAccounts.Where(a => a.Id == accountId).Select(a => a.OwnerUserId).FirstOrDefaultAsync(ct);
        if (ownerUserId is null) return NotFound();

        // N3, §49: pre-cycle-5 rows (cycles 1-4) have BillingAccountId == NULL — the column didn't
        // exist yet — so they only match by OwnerUserId, and their ChangeKind is Legacy (the backfilled
        // default, §43.4). Without the OR, every subscription event from before this cycle shipped is
        // invisible in the admin's own history screen (US-73).
        var logs = await db.SubscriptionChangeLogs
            .Where(l => l.BillingAccountId == accountId || (l.BillingAccountId == null && l.OwnerUserId == ownerUserId))
            .OrderByDescending(l => l.ChangedAt).ToListAsync(ct);

        var planIds = logs.SelectMany(l => new[] { l.OldPlanConfigId, l.NewPlanConfigId }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var planNames = await db.SubscriptionPlanConfigs.Where(p => planIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var changedByIds = logs.Select(l => l.ChangedByUserId).Distinct().ToList();
        var changedByNames = await db.Users.Where(u => changedByIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);

        var items = logs.Select(l => new
        {
            id = l.Id,
            changedAt = l.ChangedAt,
            changedByName = l.ChangedByUserId == Services.Billing.TrialActors.System
                ? "Система"
                : changedByNames.GetValueOrDefault(l.ChangedByUserId, l.ChangedByUserId),
            changeKind = l.ChangeKind.ToString(),
            line = l.Line, // ARCHITECTURE_CYCLE24.md §459.5
            companyId = l.CompanyId,
            oldPlanName = l.OldPlanConfigId.HasValue ? planNames.GetValueOrDefault(l.OldPlanConfigId.Value, "—") : null,
            newPlanName = l.NewPlanConfigId.HasValue ? planNames.GetValueOrDefault(l.NewPlanConfigId.Value, "—") : null,
            oldPaidUntil = l.OldPaidUntil,
            newPaidUntil = l.NewPaidUntil,
            oldOptionsSummary = l.OldOptionsSummary,
            newOptionsSummary = l.NewOptionsSummary,
            amount = (decimal?)null,
            comment = l.Comment,
            // ARCHITECTURE_CYCLE20.md §403.1, API_CONTRACT_CYCLE20.md §433.2 (US-20-02) — all three null
            // for rows without a reason (every path except manual assignment/trial regrant).
            reasonCode = l.ReasonCode?.ToString(),
            reasonTitle = l.ReasonCode is { } code ? Services.Billing.SubscriptionChangeReasonTexts.Title(code) : null,
            reasonDetails = l.ReasonDetails,
        }).ToList();

        return Ok(new { items });
    }

    // ARCHITECTURE_CYCLE20.md §403.3, API_CONTRACT_CYCLE20.md §433.3 (US-20-02) — the closed list for
    // the manual-assignment dropdown; the frontend keeps no titles of its own.
    [HttpGet("subscription-change-reasons")]
    public IActionResult GetSubscriptionChangeReasons()
    {
        var items = Services.Billing.SubscriptionChangeReasonTexts.All.Select(r => new
        {
            code = r.Code.ToString(),
            title = Services.Billing.SubscriptionChangeReasonTexts.Title(r.Code),
            detailsRequired = r.DetailsRequired,
            assignableManually = r.AssignableManually,
        }).ToList();
        return Ok(new { items });
    }

    // ── Subscription requests queue (US-67, US-70) ────────────────────────────────

    [HttpGet("subscription-requests")]
    public async Task<IActionResult> GetSubscriptionRequests([FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
    {
        var (currentPage, currentPageSize) = Pagination.Normalize(page, pageSize);

        // Only "Pending" is ever non-empty — see BillingAccount's own remarks: an approved/rejected/
        // cancelled request simply clears its columns rather than being kept as a history row.
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase))
            return Ok(Pagination.CreateContract(new List<object>(), currentPage, currentPageSize, 0));

        // N19 — filtered, ordered and paged entirely in SQL; only the page's own rows come back, not
        // every pending request in the system.
        var pendingQuery = db.BillingAccounts.Where(a => a.RequestedAtUtc != null);
        var total = await pendingQuery.CountAsync(ct);
        var page1 = await pendingQuery.Include(a => a.Owner).Include(a => a.RequestedPlan)
            .OrderBy(a => a.RequestedAtUtc)
            .Skip((currentPage - 1) * currentPageSize).Take(currentPageSize)
            .ToListAsync(ct);
        var allOptions = await db.SubscriptionOptions.ToListAsync(ct);

        var subs = await db.AccountSubscriptions.Include(s => s.PlanConfig)
            .Where(s => s.BillingAccountId != null && page1.Select(a => a.Id).Contains(s.BillingAccountId!.Value)).ToListAsync(ct);
        var ordersSubs = await db.OrdersSubscriptions.Include(s => s.PlanConfig)
            .Where(s => page1.Select(a => a.Id).Contains(s.BillingAccountId)).ToListAsync(ct);
        var staysSubs = await db.StaysSubscriptions.Include(s => s.PlanConfig)
            .Where(s => page1.Select(a => a.Id).Contains(s.BillingAccountId)).ToListAsync(ct);
        var bathsSubs = await db.BathsSubscriptions.Include(s => s.PlanConfig)
            .Where(s => page1.Select(a => a.Id).Contains(s.BillingAccountId)).ToListAsync(ct);
        var companyCounts = await db.Companies.Where(c => c.BillingAccountId != null && page1.Select(a => a.Id).Contains(c.BillingAccountId!.Value))
            .GroupBy(c => c.BillingAccountId!.Value).Select(g => new { AccountId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.AccountId, x => x.Count, ct);

        var items = page1.Select(a =>
        {
            var lines = OwnerSubscriptionService.DeserializeOptionLines(a.RequestedOptionsJson);
            // ARCHITECTURE_CYCLE19.md §407/§408 — a request submitted before the cycle 19 rollout may
            // still name a retired limit option; it is marked `retired` on read (never rewritten) and
            // excluded from the price estimate and the approval-window notice.
            var itemDtos = lines.Select(l =>
            {
                var opt = allOptions.FirstOrDefault(o => o.Id == l.OptionId);
                return new
                {
                    optionId = l.OptionId,
                    name = opt?.Name ?? "—",
                    quantity = l.Quantity,
                    retired = opt is not null && RetiredLimitOptions.IsRetired(opt),
                };
            }).ToList();
            var retiredNames = itemDtos.Where(i => i.retired).Select(i => i.name).Distinct().ToList();
            var requestLine = a.RequestedLine ?? CompanyKind.Services;
            var sub = subs.FirstOrDefault(s => s.BillingAccountId == a.Id);
            var ordersSub = ordersSubs.FirstOrDefault(s => s.BillingAccountId == a.Id);
            var staysSub = staysSubs.FirstOrDefault(s => s.BillingAccountId == a.Id);
            var bathsSub = bathsSubs.FirstOrDefault(s => s.BillingAccountId == a.Id);
            var currentPlanConfig = requestLine switch
            {
                CompanyKind.Orders => ordersSub?.PlanConfig,
                CompanyKind.Stays => staysSub?.PlanConfig,
                CompanyKind.Baths => bathsSub?.PlanConfig,
                _ => sub?.PlanConfig
            };
            var estimated = (currentPlanConfig?.PricePerMonth ?? 0m) + itemDtos.Where(i => !i.retired)
                .Sum(i => (allOptions.FirstOrDefault(o => o.Id == i.optionId)?.PricePerMonth ?? 0m) * i.quantity);
            return new
            {
                id = a.Id,
                billingAccountId = a.Id,
                accountName = a.Name,
                requestedByName = $"{a.Owner.FirstName} {a.Owner.LastName}".Trim(),
                requestedByPhoneMasked = a.Owner.PhoneNumber is null ? null : PhoneDisplayMask.Mask(a.Owner.PhoneNumber),
                createdAt = a.RequestedAtUtc,
                status = "Pending",
                line = requestLine,
                currentPlanName = currentPlanConfig?.Name ?? (requestLine switch { CompanyKind.Orders => OrdersFreePlan.Name, _ => null }),
                desiredPlanName = a.RequestedPlan?.Name,
                items = itemDtos,
                estimatedMonthlyPrice = estimated,
                comment = a.RequestedComment,
                companiesCount = companyCounts.GetValueOrDefault(a.Id, 0),
                retiredOptionsNotice = retiredNames.Count == 0 ? null : BillingTexts.RetiredOptionsInRequestNotice(retiredNames),
            };
        }).ToList();

        return Ok(Pagination.CreateContract(items, currentPage, currentPageSize, total));
    }

    [HttpPost("subscription-requests/{id:guid}/reject")]
    public async Task<IActionResult> RejectSubscriptionRequest(Guid id, [FromBody] RejectRequestDto? dto)
    {
        // The request is keyed by its billing account id (see BillingAccount's own remarks — there is
        // no separate SubscriptionRequest row to look up by its own id).
        // US-70 — the owner must learn WHY the request was rejected; an empty/whitespace-only comment
        // would leave LastRejectionReason meaningless to them, so it's mandatory here rather than
        // silently accepted like the (owner-authored, optional) RequestedComment it replaces.
        if (string.IsNullOrWhiteSpace(dto?.Comment)) return BadRequest("Комментарий обязателен при отклонении заявки.");

        var account = await db.BillingAccounts.FirstOrDefaultAsync(a => a.Id == id);
        if (account is null) return NotFound();
        if (account.RequestedAtUtc is null) return Conflict("Заявка уже обработана.");

        account.RequestedLine = null;
        account.RequestedPlanId = null;
        account.RequestedOptionsJson = null;
        account.RequestedAtUtc = null;
        account.RequestedByUserId = null;
        account.RequestedComment = null;
        // N10, US-70 — the reason goes to the owner-visible LastRejectionReason pair, not into
        // RequestedComment (that field belongs to the OWNER's own words, and is already being cleared
        // here anyway — nobody reads it once RequestedAtUtc is null).
        account.LastRejectionReason = dto!.Comment;
        account.LastRejectedAtUtc = DateTime.UtcNow;
        account.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return NoContent();
    }
}

// ── Local input DTOs of this controller (the former `Billing_` prefix only avoided a clash with dead
// twins in DTOs/Billing/AdminBillingDtos.cs, removed in cycle 22 — ARCHITECTURE_CYCLE22.md §371) ────
public record AssignOptionInput(Guid OptionId, int Quantity, DateOnly? PaidUntil);

public record AssignSubscriptionInput(
    Guid? PlanId, bool IsActive, DateOnly? PaidUntil, List<AssignOptionInput>? Options = null,
    decimal? Amount = null, string? Comment = null, Guid? RequestId = null, bool ConfirmLimitOverflow = false,
    // ARCHITECTURE_CYCLE20.md §403.1, API_CONTRACT_CYCLE20.md §433.1 (US-20-02) — appended at the end,
    // both optional, so every existing positional call in the test suite keeps compiling.
    Core.Enums.SubscriptionChangeReason? ReasonCode = null, string? ReasonDetails = null,
    // ARCHITECTURE_CYCLE24.md §459.5 — which line's subscription is assigned; not sent = "Записи" (as before).
    Core.Enums.CompanyKind? Line = null);

public record RejectRequestDto(string? Comment);
