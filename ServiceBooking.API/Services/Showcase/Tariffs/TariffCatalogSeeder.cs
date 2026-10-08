using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Showcase.Tariffs;

/// <summary>Outcome of <c>ops tariffs plan|apply</c>. <see cref="Lines"/> are printed to stdout as they are.</summary>
public sealed record TariffCatalogReport(IReadOnlyList<string> Lines, int PlansCreated, int RulesAdded, int FreeFieldsAligned, bool LockBusy,
    int OrdersPlansCreated = 0, int OrdersRulesAdded = 0, int OrdersFreeFieldsAligned = 0)
{
    public static TariffCatalogReport Busy { get; } = new([], 0, 0, 0, LockBusy: true);
}

/// <summary>
/// ARCHITECTURE_CYCLE28.md §573.1 — creates the missing tariffs of the "Записи" grid. It ONLY CREATES what is absent (by stable id, then by
/// name ignoring case) and never overwrites what an administrator has set: for a tariff that already exists the report lists the
/// fields that differ from the grid ("в админке цена 990, в сетке 790 — оставлено как есть").
///
/// One deliberate exception, the customer's decision Q28-1: the system free tariff is renamed and widened into "Старт" (online booking,
/// two employees) — but each field only while it still has the value the cycle-7 migration seeded, i.e. while nobody touched it.
///
/// Why a command and not a data migration: a migration would seed the tariffs into the database of every functional test and break
/// the cycle-18 tests that expect "no trial tariff" (§570 A5). Functional tests call this class directly in their own database.
/// </summary>
public class TariffCatalogSeeder(AppDbContext db, ILogger<TariffCatalogSeeder> logger)
{
    /// <summary>Seconds after which the machine's pricing catalog cache (60 s TTL) shows the change.</summary>
    public const int CacheSeconds = 60;

    public Task<TariffCatalogReport> PlanAsync(CancellationToken ct = default) => RunAsync(apply: false, ct);

    public Task<TariffCatalogReport> ApplyAsync(CancellationToken ct = default) => RunAsync(apply: true, ct);

    private async Task<TariffCatalogReport> RunAsync(bool apply, CancellationToken ct)
    {
        // Composable under an AMBIENT transaction (cycle 28, pass B): the demo reset calls this inside its own single transaction and commits it itself. Only when
        // there is none does the seeder open — and commit — a transaction of its own (the operator's `ops tariffs apply`).
        var ownsTransaction = apply && db.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await db.Database.BeginTransactionAsync(ct) : null;
        if (apply && !await AdvisoryLock.TryAcquireAsync(db, ShowcaseCatalog.TariffsLockKey))
            return TariffCatalogReport.Busy;

        var lines = new List<string>();
        var services = await db.SubscriptionPlanConfigs.Where(p => p.Line == CompanyKind.Services).ToListAsync(ct);
        // Ids are global: a row with the grid's id counts even if somebody put it into another line.
        var allById = await db.SubscriptionPlanConfigs.Where(p => ZapisTariffCatalog.Grid.Select(g => g.Id).Contains(p.Id)).ToListAsync(ct);

        var created = new List<SubscriptionPlanConfig>();
        var existingTargets = new List<SubscriptionPlanConfig>();

        foreach (var tariff in ZapisTariffCatalog.Grid)
        {
            var existing = allById.FirstOrDefault(p => p.Id == tariff.Id)
                ?? services.FirstOrDefault(p => string.Equals(p.Name, tariff.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existingTargets.Add(existing);
                var differences = DescribeDivergences(existing, tariff);
                lines.Add(differences.Count == 0
                    ? $"уже есть, не трогаю: {existing.Name} — совпадает с сеткой"
                    : $"уже есть, не трогаю: {existing.Name} — {string.Join("; ", differences)}");
                if (tariff.IsSystemTrial && !existing.IsSystemTrial && !services.Any(p => p.IsSystemTrial))
                    lines.Add($"внимание: «{existing.Name}» не помечен как пробный период — пометьте в админке (PUT /api/admin/plans/{{id}}/system-trial), иначе кнопка «Попробовать» не появится");
                continue;
            }

            if (tariff.IsSystemTrial && services.FirstOrDefault(p => p.IsSystemTrial) is { } anotherTrial)
            {
                lines.Add($"пробный уже заведён: {anotherTrial.Name}");
                continue;
            }

            var plan = ToEntity(tariff);
            created.Add(plan);
            lines.Add($"{(apply ? "создан" : "будет создан")}: {plan.Name} (id {plan.Id})");
            if (apply) db.SubscriptionPlanConfigs.Add(plan);
        }

        var rulesAdded = await AddMissingUnavailableRulesAsync(apply, created, existingTargets, ct);
        lines.Add($"правила опций: {(apply ? "добавлено" : "будет добавлено")} {rulesAdded} (у каждой опции каталога — «недоступна», в том числе у рассылок)");

        var aligned = await AlignSystemFreeTariffAsync(apply, lines, ct);

        lines.Add("pricing.public-enabled не меняется: витрину цен включает оператор в админке (PUT /api/admin/platform-settings)");

        var orders = await RunOrdersAsync(apply, lines, ct);
        if (apply)
        {
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            lines.Add($"готово; изменения будут видны в каталоге цен на работающей машине в течение {CacheSeconds} секунд");
            logger.LogInformation("ops tariffs apply: created {Created} plans, {Rules} rules, {Aligned} free-tariff fields",
                created.Count, rulesAdded, aligned);
            logger.LogInformation("ops tariffs apply (Orders): created {Created} plans, {Rules} rules, {Aligned} free-tariff fields",
                orders.Created, orders.Rules, orders.Aligned);
        }
        else
        {
            lines.Add("режим: только показать — команда `ops tariffs apply` выполнит изменения");
        }

        return new TariffCatalogReport(lines, created.Count, rulesAdded, aligned, LockBusy: false,
            OrdersPlansCreated: orders.Created, OrdersRulesAdded: orders.Rules, OrdersFreeFieldsAligned: orders.Aligned);
    }

    /// <summary>ARCHITECTURE_CYCLE37.md §37.10.2 — the "Заказы" section: same create-only rules as the "Записи" grid, looked up within the Orders line only.</summary>
    private async Task<(int Created, int Rules, int Aligned)> RunOrdersAsync(bool apply, List<string> lines, CancellationToken ct)
    {
        var ordersPlans = await db.SubscriptionPlanConfigs.Where(p => p.Line == CompanyKind.Orders).ToListAsync(ct);
        var gridIds = OrdersTariffCatalog.Grid.Select(g => g.Id).ToList();
        var allById = await db.SubscriptionPlanConfigs.Where(p => gridIds.Contains(p.Id)).ToListAsync(ct);

        var created = new List<SubscriptionPlanConfig>();
        var existingTargets = new List<SubscriptionPlanConfig>();
        foreach (var tariff in OrdersTariffCatalog.Grid)
        {
            var existing = allById.FirstOrDefault(p => p.Id == tariff.Id)
                ?? ordersPlans.FirstOrDefault(p => string.Equals(p.Name, tariff.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existingTargets.Add(existing);
                var differences = DescribeOrdersDivergences(existing, tariff);
                lines.Add(differences.Count == 0
                    ? $"«Заказы»: уже есть, не трогаю: {existing.Name} — совпадает с сеткой"
                    : $"«Заказы»: уже есть, не трогаю: {existing.Name} — {string.Join("; ", differences)}");
                continue;
            }

            var plan = ToEntity(tariff);
            created.Add(plan);
            lines.Add($"«Заказы»: {(apply ? "создан" : "будет создан")}: {plan.Name} (id {plan.Id})");
            if (apply) db.SubscriptionPlanConfigs.Add(plan);
        }

        var rules = await AddOrdersRulesAsync(apply, created, existingTargets, ct);
        lines.Add($"«Заказы»: правила опций: {(apply ? "добавлено" : "будет добавлено")} {rules} (у созданных тарифов — «доплата» для {OrdersTariffCatalog.WhatsAppOptionCode}, остальные «недоступна»)");

        var free = await db.SubscriptionPlanConfigs.FirstOrDefaultAsync(p => p.IsSystemFree && p.Line == CompanyKind.Orders, ct);
        var aligned = 0;
        if (free is null)
        {
            lines.Add("«Заказы»: системный бесплатный тариф не найден — не выравнивается");
        }
        else
        {
            var changes = AlignOrdersFreeTariff(free, apply);
            aligned = changes.Count;
            if (changes.Count == 0)
                lines.Add($"«Заказы»: бесплатный тариф «{free.Name}»: уже соответствует сетке или изменён в админке — не трогаю");
            else
                foreach (var change in changes)
                    lines.Add($"«Заказы»: бесплатный тариф (решение Q37-2): {(apply ? "изменено" : "будет изменено")} — {change}");
        }

        lines.Add("«Заказы»: переключателя нет — сетка видна на GET /api/pricing/orders, как только в ней есть публичный тариф");
        return (created.Count, rules, aligned);
    }

    private async Task<int> AddOrdersRulesAsync(
        bool apply, List<SubscriptionPlanConfig> created, List<SubscriptionPlanConfig> existingTargets, CancellationToken ct)
    {
        var options = await db.SubscriptionOptions.WhereNotRetired().ToListAsync(ct);
        var existingIds = existingTargets.Select(p => p.Id).ToList();
        var existingRuleKeys = existingIds.Count == 0
            ? []
            : (await db.PlanOptionRules
                .Where(r => existingIds.Contains(r.PlanConfigId))
                .Select(r => new { r.PlanConfigId, r.OptionId })
                .ToListAsync(ct))
                .Select(r => (r.PlanConfigId, r.OptionId))
                .ToHashSet();

        var added = 0;
        // Created tariffs: whatsapp is "extra" (a paid tariff must not allow less than the free one); everything else "unavailable".
        // Found tariffs: only missing rows, and only "unavailable" (no row means the same) — "extra" is never handed to someone else's tariff.
        foreach (var (plan, isNew) in created.Select(p => (p, true)).Concat(existingTargets.Select(p => (p, false))))
        {
            foreach (var option in options)
            {
                if (existingRuleKeys.Contains((plan.Id, option.Id))) continue;
                added++;
                if (!apply) continue;
                var availability = isNew && option.Code == OrdersTariffCatalog.WhatsAppOptionCode
                    ? OptionAvailability.Extra
                    : OptionAvailability.Unavailable;
                db.PlanOptionRules.Add(new PlanOptionRule { Id = Guid.NewGuid(), PlanConfigId = plan.Id, OptionId = option.Id, Availability = availability });
            }
        }
        return added;
    }

    /// <summary>
    /// Q37-2 — moves the free tariff of the Orders line from the cycle-24 seed to "Бесплатный", field by field, only where the field still equals the seed.
    /// When <paramref name="apply"/> is false nothing is mutated. Limits are never touched.
    /// </summary>
    public static List<string> AlignOrdersFreeTariff(SubscriptionPlanConfig free, bool apply)
    {
        var changes = new List<string>();
        if (free.Name == OrdersTariffCatalog.LegacyFreeName)
        {
            changes.Add($"название: «{free.Name}» → «{OrdersTariffCatalog.FreeName}»");
            if (apply) free.Name = OrdersTariffCatalog.FreeName;
        }
        if (free.Description == OrdersTariffCatalog.LegacyFreeDescription)
        {
            changes.Add("описание обновлено");
            if (apply) free.Description = OrdersTariffCatalog.FreeDescription;
        }
        if (free.Highlights is null)
        {
            changes.Add("преимущества заполнены");
            if (apply) free.Highlights = OrdersTariffCatalog.FreeHighlights;
        }
        if (!free.IsPublic)
        {
            changes.Add("публичный: нет → да");
            if (apply) free.IsPublic = true;
        }
        if (free.SortOrder == OrdersTariffCatalog.LegacyFreeSortOrder)
        {
            changes.Add($"порядок: {OrdersTariffCatalog.LegacyFreeSortOrder} → {OrdersTariffCatalog.FreeSortOrder}");
            if (apply) free.SortOrder = OrdersTariffCatalog.FreeSortOrder;
        }
        return changes;
    }

    /// <summary>Fields of an existing Orders row that differ from the grid, in words.</summary>
    public static List<string> DescribeOrdersDivergences(SubscriptionPlanConfig existing, OrdersTariff grid)
    {
        var differences = new List<string>();
        void Compare<T>(string label, T inAdmin, T inGrid, Func<T, string> show)
        {
            if (!EqualityComparer<T>.Default.Equals(inAdmin, inGrid))
                differences.Add($"{label} в админке {show(inAdmin)}, в сетке {show(inGrid)} — оставлено как есть");
        }

        Compare("цена", existing.PricePerMonth, grid.PricePerMonth, v => v.ToString("0.##", CultureInfo.InvariantCulture));
        Compare("магазинов", existing.MaxCompanies, grid.MaxCompanies, Limit);
        Compare("участников", existing.MaxEmployees, grid.MaxEmployees, Limit);
        Compare("товаров", existing.MaxProductsPerShop, grid.MaxProductsPerShop, Limit);
        Compare("заказов/мес", existing.MaxOrdersPerMonth, grid.MaxOrdersPerMonth, Limit);
        Compare("публичный", existing.IsPublic, true, v => v ? "да" : "нет");
        Compare("активен", existing.IsActive, true, v => v ? "да" : "нет");
        return differences;

        static string Limit(int? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "без ограничения";
    }

    public static SubscriptionPlanConfig ToEntity(OrdersTariff tariff) => new()
    {
        Id = tariff.Id,
        Name = tariff.Name,
        Line = CompanyKind.Orders,
        PricePerMonth = tariff.PricePerMonth,
        MaxCompanies = tariff.MaxCompanies,
        MaxEmployees = tariff.MaxEmployees,
        MaxProductsPerShop = tariff.MaxProductsPerShop,
        MaxOrdersPerMonth = tariff.MaxOrdersPerMonth,
        AllowOrders = true,
        AllowPublicListing = true,
        AllowNotificationChannel = true,
        AllowOnlineBooking = false,
        AllowAnalytics = false,
        AllowMailing = false,
        AllowOnlinePayment = false,
        PhotoQuotaMb = 100,
        PhotoRetention = PhotoRetention.SixMonths,
        NotifyDaysBefore = 7,
        Description = tariff.Description,
        Highlights = string.Join('\n', tariff.Highlights),
        IsActive = true,
        IsPublic = true,
        IsSystemFree = false,
        IsSystemTrial = false,
        SortOrder = tariff.SortOrder,
        CreatedAt = DateTime.UtcNow,
    };

    /// <summary>
    /// The hidden service tariff "Витрина (служебный)" (§573.2): created by <c>ops showcase create</c>, NOT by <c>ops tariffs apply</c> — it is a service row, not a
    /// product. Idempotent, and it never changes an existing row. Runs inside the caller's transaction and saves; the caller commits. Like every new tariff it gets
    /// an explicit "unavailable" rule for each option of the catalog.
    /// </summary>
    public async Task<bool> EnsureShowcasePlanAsync(CancellationToken ct = default)
    {
        var plan = await db.SubscriptionPlanConfigs.FirstOrDefaultAsync(p => p.Id == ShowcaseCatalog.ShowcasePlanId, ct);
        var created = plan is null;
        if (plan is null)
        {
            plan = ToEntity(ZapisTariffCatalog.Showcase);
            db.SubscriptionPlanConfigs.Add(plan);
        }

        var options = await db.SubscriptionOptions.WhereNotRetired().ToListAsync(ct);
        var have = (await db.PlanOptionRules.Where(r => r.PlanConfigId == plan.Id).Select(r => r.OptionId).ToListAsync(ct)).ToHashSet();
        foreach (var option in options.Where(o => !have.Contains(o.Id)))
            db.PlanOptionRules.Add(new PlanOptionRule { Id = Guid.NewGuid(), PlanConfigId = plan.Id, OptionId = option.Id, Availability = OptionAvailability.Unavailable });

        await db.SaveChangesAsync(ct);
        return created;
    }

    /// <summary>
    /// ARCHITECTURE_CYCLE35.md §35.3.3, D35-2 — the hidden service tariff «Демо» of the «Заказы» line, for the five demo shops. Same template as
    /// <see cref="EnsureShowcasePlanAsync"/>: found by Id, only created (an existing row, possibly renamed in the admin panel, is never overwritten), gets an explicit
    /// "unavailable" rule per option. No limit of orders a month (so a demo shop never reaches 80 %/100 % and never answers 402), never public. Called by the
    /// generator only when the graph has shops: <c>ops showcase create</c> on production never creates it.
    /// </summary>
    public async Task<bool> EnsureOrdersShowcasePlanAsync(CancellationToken ct = default)
    {
        var plan = await db.SubscriptionPlanConfigs.FirstOrDefaultAsync(p => p.Id == ShowcaseCatalog.OrdersShowcasePlanId, ct);
        var created = plan is null;
        if (plan is null)
        {
            plan = new SubscriptionPlanConfig
            {
                Id = ShowcaseCatalog.OrdersShowcasePlanId,
                Name = ShowcaseCatalog.OrdersShowcasePlanName,
                Line = CompanyKind.Orders,
                PricePerMonth = 0m,
                MaxCompanies = 3,
                MaxEmployees = 10,
                MaxProductsPerShop = 200,
                MaxOrdersPerMonth = null,
                AllowOrders = true,
                AllowPublicListing = true,
                AllowNotificationChannel = false,
                AllowOnlineBooking = false,
                AllowAnalytics = false,
                AllowMailing = false,
                AllowOnlinePayment = false,
                PhotoQuotaMb = null,
                PhotoRetention = PhotoRetention.TwelveMonths,
                Description = "Служебный тариф демо-магазинов «Заказов». Не назначайте его настоящим аккаунтам.",
                Highlights = null,
                IsActive = true,
                IsPublic = false,
                IsSystemFree = false,
                IsSystemTrial = false,
                SortOrder = 901,
                CreatedAt = DateTime.UtcNow,
            };
            db.SubscriptionPlanConfigs.Add(plan);
        }

        var options = await db.SubscriptionOptions.WhereNotRetired().ToListAsync(ct);
        var have = (await db.PlanOptionRules.Where(r => r.PlanConfigId == plan.Id).Select(r => r.OptionId).ToListAsync(ct)).ToHashSet();
        foreach (var option in options.Where(o => !have.Contains(o.Id)))
            db.PlanOptionRules.Add(new PlanOptionRule { Id = Guid.NewGuid(), PlanConfigId = plan.Id, OptionId = option.Id, Availability = OptionAvailability.Unavailable });

        await db.SaveChangesAsync(ct);
        return created;
    }

    private async Task<int> AddMissingUnavailableRulesAsync(
        bool apply, List<SubscriptionPlanConfig> created, List<SubscriptionPlanConfig> existingTargets, CancellationToken ct)
    {
        var options = await db.SubscriptionOptions.WhereNotRetired().ToListAsync(ct);
        var existingRuleKeys = existingTargets.Count == 0
            ? []
            : (await db.PlanOptionRules
                .Where(r => existingTargets.Select(p => p.Id).Contains(r.PlanConfigId))
                .Select(r => new { r.PlanConfigId, r.OptionId })
                .ToListAsync(ct))
                .Select(r => (r.PlanConfigId, r.OptionId))
                .ToHashSet();

        var added = 0;
        foreach (var plan in created.Concat(existingTargets))
        {
            foreach (var option in options)
            {
                if (existingRuleKeys.Contains((plan.Id, option.Id))) continue;
                added++;
                if (apply)
                    db.PlanOptionRules.Add(new PlanOptionRule
                    {
                        Id = Guid.NewGuid(), PlanConfigId = plan.Id, OptionId = option.Id, Availability = OptionAvailability.Unavailable,
                    });
            }
        }
        return added;
    }

    private async Task<int> AlignSystemFreeTariffAsync(bool apply, List<string> lines, CancellationToken ct)
    {
        var free = await db.SubscriptionPlanConfigs.FirstOrDefaultAsync(p => p.IsSystemFree && p.Line == CompanyKind.Services, ct);
        if (free is null)
        {
            lines.Add("системный бесплатный тариф не найден — «Старт» не выравнивается");
            return 0;
        }

        var changes = AlignFreeTariff(free, apply);
        if (changes.Count == 0)
            lines.Add($"бесплатный тариф «{free.Name}»: уже соответствует «Старту» или изменён в админке — не трогаю");
        else
            foreach (var change in changes)
                lines.Add($"бесплатный тариф «Старт» (решение Q28-1): {(apply ? "изменено" : "будет изменено")} — {change}");
        return changes.Count;
    }

    /// <summary>
    /// Q28-1 — moves the free tariff from the cycle-7 seed to "Старт", field by field, only where the field still equals the seed.
    /// When <paramref name="apply"/> is false nothing is mutated. Returns human-readable descriptions of the changes.
    /// </summary>
    public static List<string> AlignFreeTariff(SubscriptionPlanConfig free, bool apply)
    {
        var changes = new List<string>();
        if (!free.AllowOnlineBooking)
        {
            changes.Add("онлайн-запись: выключена → включена");
            if (apply) free.AllowOnlineBooking = true;
        }
        if (free.MaxEmployees == 1)
        {
            changes.Add($"сотрудников: 1 → {ZapisTariffCatalog.StartMaxEmployees}");
            if (apply) free.MaxEmployees = ZapisTariffCatalog.StartMaxEmployees;
        }
        if (free.Name == ZapisTariffCatalog.LegacyFreeName)
        {
            changes.Add($"название: «{free.Name}» → «{ZapisTariffCatalog.StartName}»");
            if (apply) free.Name = ZapisTariffCatalog.StartName;
        }
        if (free.Description == ZapisTariffCatalog.LegacyFreeDescription)
        {
            changes.Add("описание обновлено");
            if (apply) free.Description = ZapisTariffCatalog.StartDescription;
        }
        if (free.Highlights == ZapisTariffCatalog.LegacyFreeHighlights)
        {
            changes.Add("преимущества обновлены");
            if (apply) free.Highlights = ZapisTariffCatalog.StartHighlights;
        }
        return changes;
    }

    /// <summary>Fields of an existing row that differ from the grid, in words. Only the fields the grid defines and an administrator is
    /// likely to have changed.</summary>
    public static List<string> DescribeDivergences(SubscriptionPlanConfig existing, ZapisTariff grid)
    {
        var differences = new List<string>();
        void Compare<T>(string label, T inAdmin, T inGrid, Func<T, string> show)
        {
            if (!EqualityComparer<T>.Default.Equals(inAdmin, inGrid))
                differences.Add($"{label} в админке {show(inAdmin)}, в сетке {show(inGrid)} — оставлено как есть");
        }

        Compare("цена", existing.PricePerMonth, grid.PricePerMonth, v => v.ToString("0.##", CultureInfo.InvariantCulture));
        Compare("компаний", existing.MaxCompanies, grid.MaxCompanies, Limit);
        Compare("сотрудников", existing.MaxEmployees, grid.MaxEmployees, Limit);
        Compare("квота фото, МБ", existing.PhotoQuotaMb, grid.PhotoQuotaMb, Limit);
        Compare("срок хранения фото", existing.PhotoRetention, grid.PhotoRetention, v => v.ToString());
        Compare("публичный", existing.IsPublic, grid.IsPublic, v => v ? "да" : "нет");
        Compare("активен", existing.IsActive, true, v => v ? "да" : "нет");
        return differences;

        static string Limit(int? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "без ограничения";
    }

    public static SubscriptionPlanConfig ToEntity(ZapisTariff tariff) => new()
    {
        Id = tariff.Id,
        Name = tariff.Name,
        Line = CompanyKind.Services,
        PricePerMonth = tariff.PricePerMonth,
        MaxCompanies = tariff.MaxCompanies,
        MaxEmployees = tariff.MaxEmployees,
        AllowOnlineBooking = true,
        AllowAnalytics = true,
        AllowPublicListing = true,
        AllowMailing = false,
        AllowOnlinePayment = false,
        AllowNotificationChannel = false,
        PhotoQuotaMb = tariff.PhotoQuotaMb,
        PhotoRetention = tariff.PhotoRetention,
        Description = tariff.Description,
        Highlights = tariff.Highlights.Count == 0 ? null : string.Join('\n', tariff.Highlights),
        IsActive = true,
        IsPublic = tariff.IsPublic,
        SortOrder = tariff.SortOrder,
        IsSystemTrial = tariff.IsSystemTrial,
        CreatedAt = DateTime.UtcNow,
    };
}
