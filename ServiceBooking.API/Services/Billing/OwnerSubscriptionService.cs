using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Billing;

/// <summary>Cycle 7, stage 5 (contracts/cycle7/openapi.yaml /billing/subscription,
/// /billing/subscription/request) — the owner's own "Ваша подписка" screen and their option/plan
/// request. Every text is assembled here (§41 п. 8) — the frontend prints strings as-is.</summary>
public class OwnerSubscriptionService(
    AppDbContext db, SubscriptionResolver subscriptionResolver, AccountUsageReader usageReader,
    TrialStateReader trialStateReader)
{
    public async Task<BillingAccount?> FindAccountForOwnerAsync(string ownerUserId) =>
        await db.BillingAccounts.Include(a => a.RequestedPlan).FirstOrDefaultAsync(a => a.OwnerUserId == ownerUserId);

    public async Task<OwnerSubscriptionDto?> GetAsync(string ownerUserId)
    {
        var account = await FindAccountForOwnerAsync(ownerUserId);
        if (account is null) return null;
        return await BuildAsync(account);
    }

    public async Task<OwnerSubscriptionDto> BuildAsync(BillingAccount account)
    {
        var now = DateTime.UtcNow;
        var sub = await db.AccountSubscriptions.Include(s => s.PlanConfig)
            .FirstOrDefaultAsync(s => s.BillingAccountId == account.Id);
        var plan = await subscriptionResolver.GetEffectivePlanForAccountAsync(account.Id);

        var subscribedOptions = await db.AccountSubscriptionOptions.Include(o => o.Option)
            .Where(o => o.BillingAccountId == account.Id)
            .Where(o => o.EndsAtUtc == null || o.EndsAtUtc > now)
            .ToListAsync();
        var planRules = sub?.PlanConfigId is { } planConfigId
            ? await db.PlanOptionRules.Where(r => r.PlanConfigId == planConfigId).ToListAsync()
            : [];

        var optionDtos = subscribedOptions.Select(o => ToSubscribedOptionDto(o, planRules, now)).ToList();

        var usage = (await usageReader.GetAsync([account.Id])).GetValueOrDefault(account.Id) ?? new AccountUsage(account.Id, 0, 0);

        var companies = await db.Companies.Where(c => c.BillingAccountId == account.Id).ToListAsync();
        var companyIds = companies.Select(c => c.Id).ToList();
        var seatsByCompany = await usageReader.GetCompanySeatsAsync(companyIds);
        var assignedCompanyIds = (await db.ChannelCompanyAssignments
            .Where(a => companyIds.Contains(a.CompanyId))
            .Select(a => a.CompanyId).ToListAsync()).ToHashSet();

        var status = SubscriptionStatusFor(sub, now);
        var statusText = StatusTextFor(status, sub?.PaidUntil);
        var expiresInDays = BillingCalculator.ExpiresInDays(sub?.PaidUntil, now);
        var isExpiringSoon = sub?.PlanConfig is not null &&
            BillingCalculator.IsExpiringSoon(sub.PaidUntil, sub.PlanConfig.NotifyDaysBefore, now);

        var whatsapp = subscribedOptions.FirstOrDefault(o => o.Option.Code == SubscriptionResolver.WhatsAppOptionCode);
        var numbersRegistered = await db.NotificationChannels
            .CountAsync(c => c.BillingAccountId == account.Id && c.State != ChannelState.Replaced);
        var numbersText = BuildNumbersText(plan.PaidNotificationNumbers, numbersRegistered);

        // Cycle 18, API_CONTRACT_CYCLE18.md §365 (Д3) — "деградация = заморозка": going over a limit
        // (e.g. a trial ending and the account falling back to the Free plan's tighter limits) never
        // deletes or disables anything already created; it only blocks adding MORE. 0/null when within
        // limits, matching the contract's "0 when in range" convention exactly.
        var overLimitCompanies = plan.AccountMaxCompanies is { } companiesLimit ? Math.Max(0, usage.CompaniesUsed - companiesLimit) : 0;
        var overLimitEmployees = plan.AccountMaxEmployees is { } employeesLimit ? Math.Max(0, usage.SeatsUsed - employeesLimit) : 0;
        // Code-review finding (cycle 18 recheck) — TrialOverFreeLimitsNotice's wording is hardcoded to
        // "Бесплатный тариф допускает...", so it must always quote the SYSTEM FREE plan's own limits,
        // never the account's current effective plan (which may be a paid plan an admin later shrank).
        // It also must not render when either limit is unbounded (null) — string.Format would silently
        // print an empty slot into legally-loaded text (Т4, §365).
        string? overLimitText = null;
        if (overLimitCompanies > 0 || overLimitEmployees > 0)
        {
            var freePlan = await db.SubscriptionPlanConfigs.AsNoTracking().FirstOrDefaultAsync(p => p.IsSystemFree);
            var freeCompaniesLimit = freePlan?.MaxCompanies ?? EffectivePlan.Free.AccountMaxCompanies;
            var freeEmployeesLimit = freePlan?.MaxEmployees ?? EffectivePlan.Free.AccountMaxEmployees;
            if (freeCompaniesLimit is not null && freeEmployeesLimit is not null)
                overLimitText = string.Format(TrialLegalNotices.TrialOverFreeLimitsNotice,
                    freeCompaniesLimit, freeEmployeesLimit, usage.CompaniesUsed, usage.SeatsUsed);
        }

        var usageDto = new SubscriptionUsageDto(
            usage.CompaniesUsed, plan.AccountMaxCompanies, usage.SeatsUsed, plan.AccountMaxEmployees,
            EmployeesTextFor(usage.SeatsUsed, plan.AccountMaxEmployees), CompaniesTextFor(usage.CompaniesUsed, plan.AccountMaxCompanies),
            plan.PaidNotificationNumbers, numbersRegistered, numbersText,
            overLimitCompanies, overLimitEmployees, overLimitText);

        var coveredCompanies = companies.Select(c => new CoveredCompanyDto(
            c.Id, c.Name, seatsByCompany.GetValueOrDefault(c.Id), assignedCompanyIds.Contains(c.Id))).ToList();

        var planDto = new SubscribedPlanDto(
            sub?.PlanConfigId, sub?.PlanConfig?.Name ?? "Бесплатный", sub?.PlanConfig?.Description, sub?.PlanConfig?.PricePerMonth ?? 0m,
            BuildPlanIncludes(plan));

        var totalMonthlyPrice = BillingCalculator.TotalMonthlyPrice(planDto.PricePerMonth, optionDtos.Select(o => o.PricePerMonth));

        // N24 — planDto's Name/Price come from the raw subscription row (sub.PlanConfig, so the owner
        // can see WHAT they were on even after it lapsed), while `plan`/planDto.Includes come from the
        // RESOLVED EffectivePlan, which falls back to Free the moment status is Expired. That mismatch
        // — a paid plan's name/price next to a Free feature list — is deliberate, not accidental, but
        // it must never go unexplained: the warning below names the actual subscribed plan explicitly
        // so "Профи, 4990 ₽/мес" next to "Онлайн-запись: нет" doesn't read as a bug on screen.
        // ARCHITECTURE_CYCLE17.md §307.1/§307.2 (US-17-08/09, C15-7) — "снят с продажи" = IsActive &&
        // !IsPublic (§255.2/§255.3 cycle 15). Computed once, shared by the "Expiring soon" warning
        // below and by BuildPendingRequestDto further down.
        var currentPlanWithdrawn = sub?.PlanConfig is { IsActive: true, IsPublic: false };
        var warning = BuildWarning(status, isExpiringSoon, expiresInDays, sub, currentPlanWithdrawn);

        // B10: an already-subscribed Quantity option must stay listed here too — otherwise the owner
        // can never ask to buy MORE of something they already have (e.g. one more WhatsApp number).
        // The current quantity, if any, is already visible in `optionDtos` (Options[]) by OptionId;
        // this list only ever answers "can I request this option at all right now".
        var allOptions = await db.SubscriptionOptions.Where(o => o.IsActive && o.PricePerMonth != null).ToListAsync();
        var availableOptions = allOptions
            .Select(o => ToAvailableOptionDto(o, planRules))
            .ToList(); // Unavailable options ARE shown too (§70 п.2) — no filter beyond IsActive/priced above.

        var pendingRequest = BuildPendingRequestDto(
            account, allOptions.Concat(subscribedOptions.Select(o => o.Option)).DistinctBy(o => o.Id).ToList(),
            planDto.PricePerMonth, currentPlanWithdrawn, sub?.PlanConfigId);

        var lastRejectedRequest = account.LastRejectionReason is not null && account.LastRejectedAtUtc.HasValue
            ? new RejectedRequestDto(account.LastRejectionReason, account.LastRejectedAtUtc.Value)
            : null;

        // §365 — the same TrialStateDto GET /api/billing/trial would answer for this owner, so this
        // screen's "Пробный период" card/plashka/button can never drift from what that endpoint says.
        // Code-review finding (cycle 18 recheck) — pass the account/sub/plan already loaded above
        // instead of GetAsync (which would reload them and re-resolve the effective plan), removing
        // ~5 avoidable round-trips from the owner's most-visited screen.
        var trial = await trialStateReader.BuildAsync(account, sub, plan);

        return new OwnerSubscriptionDto(
            "RUB", status, statusText, planDto, optionDtos, totalMonthlyPrice, sub?.PaidUntil, expiresInDays, isExpiringSoon,
            usageDto, coveredCompanies, warning, availableOptions, pendingRequest, CanRequestChanges: true,
            LastRejectedRequest: lastRejectedRequest, Trial: trial);
    }

    public static string SubscriptionStatusFor(AccountSubscription? sub, DateTime now)
    {
        if (sub is null || sub.PlanConfigId is null) return "Free";
        if (!sub.IsActive) return "Expired";
        if (sub.PaidUntil.HasValue && sub.PaidUntil < now) return "Expired";
        return "Active";
    }

    /// <summary>Also used by <c>AdminBillingController</c>'s billing-account DTOs (merge-review
    /// finding: admin surfaces for options/funding already print server-assembled text — see
    /// <see cref="BillingTexts.FundingText"/> and <c>SubscribedOptionDto.StatusText</c> — while the
    /// account-level status was left as a bare enum, forcing the frontend to keep its own
    /// translation dictionary). Same wording either audience sees.</summary>
    public static string StatusTextFor(string status, DateTime? paidUntil) => status switch
    {
        "Free" => "Бесплатный тариф",
        "Expired" => paidUntil.HasValue ? $"Подписка истекла {paidUntil:dd.MM.yyyy}" : "Подписка неактивна",
        _ => paidUntil.HasValue ? $"Оплачено до {paidUntil:dd.MM.yyyy}" : "Активна",
    };

    /// <summary>Internal, not private: cycle 18's TrialStateReader reuses this exact rendering for
    /// TrialStateDto.includes (§362) so the trial preview's feature list can never drift from the
    /// wording the owner sees on the subscribed-plan screen.</summary>
    internal static List<string> BuildPlanIncludes(EffectivePlan plan)
    {
        var list = new List<string>();
        if (plan.AllowOnlineBooking) list.Add("Онлайн-запись");
        if (plan.AllowMailing) list.Add("Рассылки клиентам");
        if (plan.AllowAnalytics) list.Add("Аналитика");
        if (plan.AllowPublicListing) list.Add("Публичная страница компании");
        if (plan.AllowOnlinePayment) list.Add("Онлайн-оплата");
        return list;
    }

    private static string EmployeesTextFor(int used, int? limit) =>
        limit is null ? $"Сотрудников: {used} (без ограничения)" : $"Занято {used} из {limit} мест для сотрудников (суммарно на все точки).";

    private static string CompaniesTextFor(int used, int? limit) =>
        limit is null ? $"Компаний: {used} (без ограничения)" : $"Открыто {used} из {limit} точек, доступных на тарифе.";

    private static string BuildNumbersText(int paid, int registered)
    {
        if (registered == 0) return "Номеров для рассылок не заведено.";
        if (paid <= 0) return $"Заведено {registered}, но не оплачено ни одного номера — рассылки не отправляются.";
        if (paid >= registered) return $"Оплачено {paid} из {registered} — все номера работают.";
        return $"Оплачено {paid} из {registered} заведённых номеров. Чтобы включить остальные, подключите ещё одну «Рассылку в WhatsApp».";
    }

    private SubscribedOptionDto ToSubscribedOptionDto(AccountSubscriptionOption row, List<PlanOptionRule> planRules, DateTime now)
    {
        var rule = planRules.FirstOrDefault(r => r.OptionId == row.OptionId);
        // N14 — a missing rule means Unavailable (fail-closed on money, §43.3), never Extra. This
        // display path shouldn't normally hit a missing rule for an already-subscribed option, but the
        // default must still be the safe one, not the billable one.
        var availability = rule?.Availability ?? OptionAvailability.Unavailable;
        var pricePerUnit = row.Option.PricePerMonth ?? 0m;
        var monthly = BillingCalculator.MonthlyPriceFor(availability, row.Quantity, pricePerUnit, rule?.IncludedQuantity);

        // N5, §38.5: four statuses, not two. Unavailable (rule says so, N13/N14) wins over everything
        // else — the tariff no longer allows it, regardless of what EndsAtUtc/RequestedQuantity say.
        // Ending (already flagged to switch off) comes next. PendingPayment (there IS a request for
        // MORE of this option, quantity/date not confirmed yet) is the one status this row's own
        // columns didn't previously surface at all, even though RequestedQuantity/RequestedAtUtc exist
        // specifically for it.
        var status = availability == OptionAvailability.Unavailable ? "Unavailable"
            : row.EndsAtUtc.HasValue ? "Ending"
            : row.RequestedAtUtc.HasValue ? "PendingPayment"
            : "Active";
        var statusText = status switch
        {
            "Unavailable" => "Недоступно на вашем тарифе — отключится при следующем пересчёте",
            "Ending" => $"Действует до {row.EndsAtUtc:dd.MM.yyyy}, затем отключится",
            "PendingPayment" => "Заявка на изменение количества отправлена, ожидает подтверждения оплаты",
            _ => "Подключена",
        };

        return new SubscribedOptionDto(
            row.OptionId, row.Option.Name, row.Option.Description, row.Option.Kind == OptionKind.Toggle ? "Toggle" : "Quantity",
            row.Option.UnitName, row.Quantity, pricePerUnit, monthly, status, statusText, row.EndsAtUtc, CanDisable: true,
            row.PaidUntilUtc, row.RequestedQuantity, row.RequestedAtUtc);
    }

    private static AvailableOptionDto ToAvailableOptionDto(SubscriptionOption option, List<PlanOptionRule> planRules)
    {
        var rule = planRules.FirstOrDefault(r => r.OptionId == option.Id);
        var availability = rule?.Availability ?? OptionAvailability.Unavailable;
        var text = availability switch
        {
            OptionAvailability.Unavailable => "Недоступно на вашем тарифе",
            OptionAvailability.Included => "Включено в тариф",
            _ => option.UnitPriceText ?? $"{option.PricePerMonth:0.##} ₽/мес",
        };

        return new AvailableOptionDto(
            option.Id, option.Name, option.Description, option.Kind == OptionKind.Toggle ? "Toggle" : "Quantity",
            option.UnitName, option.PricePerMonth, option.MaxQuantity,
            availability.ToString(), text, CanRequest: availability != OptionAvailability.Unavailable);
    }

    private static SubscriptionWarningDto? BuildWarning(
        string status, bool isExpiringSoon, int? expiresInDays, AccountSubscription? sub, bool currentPlanWithdrawn)
    {
        if (status == "Expired")
        {
            // N24 — name the actual lapsed plan explicitly, so a screen showing that plan's name/price
            // right next to a Free-tier feature list reads as "this is why", not as a data bug. Affected
            // features are the ones THIS plan actually had (not a hardcoded guess) that Free doesn't.
            var planName = sub?.PlanConfig?.Name;
            var text = planName is null
                ? "Подписка истекла — доступны только возможности бесплатного тарифа."
                : $"Подписка на тариф «{planName}» истекла — доступны только возможности бесплатного тарифа.";
            var affected = new List<string>();
            if (sub?.PlanConfig is { } p)
            {
                if (p.AllowOnlineBooking) affected.Add("Онлайн-запись");
                if (p.AllowMailing) affected.Add("Рассылки клиентам");
                if (p.AllowAnalytics) affected.Add("Аналитика");
                if (p.AllowOnlinePayment) affected.Add("Онлайн-оплата");
            }
            else
            {
                affected.AddRange(["Онлайн-запись", "Рассылки клиентам", "Аналитика"]);
            }
            return new SubscriptionWarningDto("Expired", text, affected);
        }

        if (isExpiringSoon && expiresInDays is >= 0)
        {
            // ARCHITECTURE_CYCLE17.md §307.2, API_CONTRACT_CYCLE17.md §325.2 (US-17-09, п. 6.8.5) —
            // when publicly listed, the string is byte-for-byte what it was before this cycle
            // (regression asserted by exact string comparison, not Contains). When the current plan is
            // snapshot off sale, legal-counsel's irreversibility line is appended.
            //
            // ⚠️ §307.1 — this is the SECOND obligatory place this warning must appear (п. 6.13.15
            // 03-terms-owner.html): the self-service plan-change screen in the owner cabinet, which
            // does not exist yet. When it's built, its text comes from this same source.
            var baseText = $"Подписка истекает через {expiresInDays} дн. — продлите её, чтобы не потерять возможности тарифа.";
            var text = currentPlanWithdrawn
                ? baseText + " " + LegalNotices.SubscriptionExpiringOnWithdrawnPlanSuffix
                : baseText;
            return new SubscriptionWarningDto("Expiring", text, []);
        }

        return null;
    }

    // ── Requests (§49, US-70) ────────────────────────────────────────────────────

    // ARCHITECTURE_CYCLE17.md §307.1 — `currentPlanWithdrawn` defaults false so
    // AdminBillingController.cs's own call site (a different audience, admin's own DTO shape) keeps
    // compiling and behaving exactly as before without also needing this notice computed there.
    public SubscriptionRequestDto? BuildPendingRequestDto(
        BillingAccount account, List<SubscriptionOption> knownOptions, decimal currentPlanPrice,
        bool currentPlanWithdrawn = false, Guid? currentPlanId = null)
    {
        if (account.RequestedAtUtc is null) return null;

        var lines = DeserializeOptionLines(account.RequestedOptionsJson);
        var items = lines.Select(l =>
        {
            var option = knownOptions.FirstOrDefault(o => o.Id == l.OptionId);
            return new SubscriptionRequestItemDto(l.OptionId, option?.Name ?? "—", l.Quantity);
        }).ToList();

        // Estimated total: base plan price is either the requested new plan's price (if a plan change
        // was requested) or the current one, plus the full price of every requested option (§49's
        // snapshot is illustrative — an admin recomputes the real total on assignment).
        var estimated = currentPlanPrice + lines.Sum(l =>
        {
            var option = knownOptions.FirstOrDefault(o => o.Id == l.OptionId);
            return (option?.PricePerMonth ?? 0m) * l.Quantity;
        });

        // ARCHITECTURE_CYCLE17.md §307.1 — all three conditions at once: an active subscription exists
        // (`currentPlanWithdrawn` is already false without one, computed by the caller from `sub`),
        // its plan is snapshot off sale, AND this request asks for a PLAN CHANGE specifically (options-
        // only requests, or a request for the SAME plan the account is already on, get null).
        var requestsPlanChange = account.RequestedPlanId is not null && account.RequestedPlanId != currentPlanId;
        var irreversibilityNotice = currentPlanWithdrawn && requestsPlanChange
            ? LegalNotices.PendingPlanChangeIrreversibilityNotice
            : null;

        return new SubscriptionRequestDto(
            // Deterministic pseudo-id derived from the account (there's no separate request row to key
            // off — see the entity's own remarks); stable for the lifetime of one pending request.
            account.Id, "Pending", account.RequestedAtUtc.Value, account.RequestedPlanId, account.RequestedPlan?.Name,
            estimated, items, account.RequestedComment, irreversibilityNotice);
    }

    public static List<RequestedOptionLine> DeserializeOptionLines(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<RequestedOptionLine>>(json) ?? [];

    public static string SerializeOptionLines(IEnumerable<RequestedOptionLine> lines) => JsonSerializer.Serialize(lines);
}
