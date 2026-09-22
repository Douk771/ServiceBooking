using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>contracts/cycle7/openapi.yaml tag billing-admin — options catalog, billing accounts,
/// subscription assignment and the owner request queue (US-66, US-67, US-70). Kept as a separate
/// controller from <see cref="AdminController"/> (same "api/admin" route prefix, same SuperAdmin-only
/// authorization) purely so this cycle's diff doesn't grow an already-780-line file further.</summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "SuperAdmin")]
public class AdminBillingController(
    AppDbContext db, PricingCatalogCache pricingCatalogCache,
    SubscriptionResolver subscriptionResolver, AccountUsageReader usageReader,
    OwnerSubscriptionService ownerSubscriptionService, ILogger<AdminBillingController> logger) : ControllerBase
{
    // ── Options catalog (US-66) ───────────────────────────────────────────────────

    [HttpGet("options")]
    public async Task<IActionResult> GetOptions()
    {
        var options = await db.SubscriptionOptions.OrderBy(o => o.SortOrder).ThenBy(o => o.Name).ToListAsync();
        var counts = await GetOptionSubscriberCountsAsync(options.Select(o => o.Id));
        return Ok(new { options = options.Select(o => MapOptionDto(o, counts.GetValueOrDefault(o.Id))).ToList() });
    }

    [HttpPost("options")]
    public async Task<IActionResult> CreateOption([FromBody] Billing_AdminOptionInput dto)
    {
        var validationError = ValidateOptionInput(dto, existingCode: null);
        if (validationError is not null) return validationError;

        if (await db.SubscriptionOptions.AnyAsync(o => o.Code == dto.Code))
            return Conflict($"Опция с кодом «{dto.Code}» уже существует.");

        var option = new SubscriptionOption
        {
            Id = Guid.NewGuid(),
            Code = dto.Code,
            Name = dto.Name,
            Description = dto.Description,
            Kind = ParseKind(dto.Kind),
            CapabilityKey = dto.CapabilityKey,
            PricePerMonth = dto.PricePerMonth,
            UnitName = dto.UnitName,
            MaxQuantity = dto.MaxQuantity,
            IsPublic = dto.IsPublic,
            IsActive = dto.IsActive,
            SortOrder = dto.SortOrder,
        };
        db.SubscriptionOptions.Add(option);
        await db.SaveChangesAsync();
        pricingCatalogCache.Invalidate();
        return StatusCode(StatusCodes.Status201Created, MapOptionDto(option, 0));
    }

    [HttpPut("options/{id:guid}")]
    public async Task<IActionResult> UpdateOption(Guid id, [FromBody] Billing_AdminOptionInput dto)
    {
        var option = await db.SubscriptionOptions.FindAsync(id);
        if (option is null) return NotFound();

        if (dto.Code != option.Code)
            return BadRequest("Код опции менять нельзя.");

        var validationError = ValidateOptionInput(dto, existingCode: option.Code);
        if (validationError is not null) return validationError;

        option.Name = dto.Name;
        option.Description = dto.Description;
        option.Kind = ParseKind(dto.Kind);
        option.CapabilityKey = dto.CapabilityKey;
        option.PricePerMonth = dto.PricePerMonth;
        option.UnitName = dto.UnitName;
        option.MaxQuantity = dto.MaxQuantity;
        option.IsPublic = dto.IsPublic;
        option.IsActive = dto.IsActive;
        option.SortOrder = dto.SortOrder;
        option.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync();
        pricingCatalogCache.Invalidate();
        var count = await db.AccountSubscriptionOptions.CountAsync(o => o.OptionId == id);
        return Ok(MapOptionDto(option, count));
    }

    [HttpDelete("options/{id:guid}")]
    public async Task<IActionResult> DeactivateOption(Guid id)
    {
        var option = await db.SubscriptionOptions.FindAsync(id);
        if (option is null) return NotFound();

        option.IsActive = false;
        option.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        pricingCatalogCache.Invalidate();
        return NoContent();
    }

    [HttpGet("option-capabilities")]
    public IActionResult GetOptionCapabilities() =>
        Ok(new { capabilities = OptionCapabilityCatalog.Known.Select(c => new { c.Key, c.Kind, c.Name }).ToList() });

    // Contract §48: option codes are machine identifiers, not free text.
    private static readonly System.Text.RegularExpressions.Regex CodePattern =
        new("^[a-z0-9.\\-]{2,64}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    // N — SubscriptionOption.PricePerMonth is `numeric(10,2)` (AppDbContext), 8 integer digits + 2
    // decimal, so anything at or above 10^8 overflows the column and Npgsql throws a raw
    // PostgresException ("numeric field overflow") on SaveChangesAsync — an unhandled 500, not a 400
    // (cycle-07 backend report, item 3, confirmed against the actual column precision). Validated here,
    // before the row ever reaches the DbContext, same as every other business rule in this method.
    public const decimal MaxOptionPricePerMonth = 99_999_999.99m;
    // No column-precision reason for this one (MaxQuantity is a plain `int`) — just a sane upper bound
    // so a denormalized value here can't later blow up a `decimal * int` multiplication elsewhere
    // (BillingCalculator.MonthlyPriceFor multiplies a subscribed quantity by the option's price).
    public const int MaxOptionMaxQuantity = 1_000_000;

    internal static IActionResult? ValidateOptionInput(Billing_AdminOptionInput dto, string? existingCode)
    {
        if (string.IsNullOrWhiteSpace(dto.Code) || !CodePattern.IsMatch(dto.Code))
            return new BadRequestObjectResult("Код опции обязателен и должен соответствовать формату ^[a-z0-9.-]{2,64}$.");
        if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length > 100)
            return new BadRequestObjectResult("Название обязательно (до 100 символов).");
        if (dto.PricePerMonth is < 0)
            return new BadRequestObjectResult("Цена не может быть отрицательной.");
        if (dto.PricePerMonth > MaxOptionPricePerMonth)
            return new BadRequestObjectResult($"Цена не может превышать {MaxOptionPricePerMonth}.");
        if (dto.MaxQuantity is not null && dto.MaxQuantity < 1)
            return new BadRequestObjectResult("Максимальное количество должно быть не меньше 1.");
        if (dto.MaxQuantity > MaxOptionMaxQuantity)
            return new BadRequestObjectResult($"Максимальное количество не может превышать {MaxOptionMaxQuantity}.");

        if (TryParseKind(dto.Kind) is not { } kind)
            return new BadRequestObjectResult("kind должен быть Toggle или Quantity.");
        if (kind == OptionKind.Quantity && string.IsNullOrWhiteSpace(dto.UnitName))
            return new BadRequestObjectResult("Для опции-количества обязательна единица измерения.");
        if (kind == OptionKind.Toggle && !string.IsNullOrWhiteSpace(dto.UnitName))
            return new BadRequestObjectResult("Для опции-переключателя единица измерения не задаётся.");

        return null;
    }

    private static OptionKind? TryParseKind(string? kind) => kind switch
    {
        "Toggle" => OptionKind.Toggle,
        "Quantity" => OptionKind.Quantity,
        _ => null,
    };

    private static OptionKind ParseKind(string kind) =>
        TryParseKind(kind) ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, "kind must be Toggle or Quantity");

    // B5: PostgreSQL "timestamp with time zone" columns require Kind == Utc; DateOnly.ToDateTime always
    // yields Kind == Unspecified, which Npgsql rejects at runtime (500) rather than silently coercing.
    private static DateTime? ToUtc(DateOnly? date) =>
        date is null ? null : DateTime.SpecifyKind(date.Value.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);

    private async Task<Dictionary<Guid, int>> GetOptionSubscriberCountsAsync(IEnumerable<Guid> optionIds)
    {
        var ids = optionIds.ToList();
        return await db.AccountSubscriptionOptions
            .Where(o => ids.Contains(o.OptionId) && (o.EndsAtUtc == null || o.EndsAtUtc > DateTime.UtcNow))
            .GroupBy(o => o.OptionId)
            .Select(g => new { OptionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OptionId, x => x.Count);
    }

    private static object MapOptionDto(SubscriptionOption o, int subscribedAccounts) => new
    {
        id = o.Id,
        code = o.Code,
        name = o.Name,
        description = o.Description,
        kind = o.Kind.ToString(),
        capabilityKey = o.CapabilityKey,
        capabilityKnown = OptionCapabilityCatalog.IsKnown(o.CapabilityKey),
        pricePerMonth = o.PricePerMonth,
        currency = "RUB",
        unitName = o.UnitName,
        maxQuantity = o.MaxQuantity,
        isPublic = o.IsPublic,
        isActive = o.IsActive,
        sortOrder = o.SortOrder,
        subscribedAccounts,
    };

    // ── Billing accounts (US-67) ──────────────────────────────────────────────────

    [HttpGet("billing-accounts")]
    public async Task<IActionResult> GetBillingAccounts(
        [FromQuery] string? search, [FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize)
    {
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

        if (!string.IsNullOrWhiteSpace(search))
        {
            // NB-6 — phones are stored canonical (digits only, US-26); a search string that LOOKS like
            // a phone number must be normalized the same way before matching PhoneNumber, same
            // convention as AdminController.GetUsers, or a formatted phone ("+7 (999) 123-45-67")
            // never matches anything.
            var digitCount = search.Count(char.IsDigit);
            var looksLikePhone = digitCount >= 5 && !search.Any(char.IsLetter);
            var phoneSearch = looksLikePhone ? PhoneNormalizer.Normalize(search) : search;

            joined = joined.Where(x =>
                x.a.Owner.Email!.Contains(search) ||
                (phoneSearch.Length > 0 && x.a.Owner.PhoneNumber!.Contains(phoneSearch)) ||
                x.a.Owner.FirstName.Contains(search) || x.a.Owner.LastName.Contains(search) ||
                db.Companies.Any(c => c.BillingAccountId == x.a.Id && c.Name.Contains(search)));
        }

        var withStatus = joined.Select(x => new
        {
            x.a,
            x.sub,
            status = x.sub == null || x.sub.PlanConfigId == null ? "Free"
                : !x.sub.IsActive || (x.sub.PaidUntil.HasValue && x.sub.PaidUntil < now) ? "Expired"
                : "Active",
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

        var total = await withStatus.CountAsync();
        var page1 = await withStatus.OrderBy(x => x.a.CreatedAtUtc)
            .Skip((currentPage - 1) * currentPageSize).Take(currentPageSize)
            .ToListAsync();

        var accountIds = page1.Select(x => x.a.Id).ToList();
        var plans = await subscriptionResolver.GetEffectivePlansForAccountsAsync(accountIds);
        var usages = await usageReader.GetAsync(accountIds);

        // N4 — the same two figures the account's own card already gets right (BuildAccountCardAsync):
        // numbersRegistered from an actual COUNT, and totalMonthlyPrice including every subscribed
        // option's price, not just the bare plan. Batched (§46) — one query for every account's options,
        // one for every account's registered-channel count, not one per row — and now scoped to just
        // this page's account ids rather than every account in the system.
        var subscribedOptions = await db.AccountSubscriptionOptions.Include(o => o.Option)
            .Where(o => accountIds.Contains(o.BillingAccountId))
            .Where(o => o.EndsAtUtc == null || o.EndsAtUtc > now)
            .ToListAsync();
        var planConfigIds = page1.Where(x => x.sub != null && x.sub.PlanConfigId.HasValue)
            .Select(x => x.sub!.PlanConfigId!.Value).Distinct().ToList();
        var planRules = planConfigIds.Count == 0
            ? []
            : await db.PlanOptionRules.Where(r => planConfigIds.Contains(r.PlanConfigId)).ToListAsync();
        var registeredCounts = await db.NotificationChannels
            .Where(c => c.BillingAccountId != null && accountIds.Contains(c.BillingAccountId!.Value) && c.State != ChannelState.Replaced)
            .GroupBy(c => c.BillingAccountId!.Value)
            .Select(g => new { AccountId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.AccountId, g => g.Count);

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
            };
        }).ToList();

        return Ok(Pagination.CreateContract(result, currentPage, currentPageSize, total));
    }

    [HttpGet("billing-accounts/{accountId:guid}")]
    public async Task<IActionResult> GetBillingAccount(Guid accountId)
    {
        var account = await db.BillingAccounts.Include(a => a.Owner).Include(a => a.RequestedPlan)
            .FirstOrDefaultAsync(a => a.Id == accountId);
        if (account is null) return NotFound();

        var dto = await BuildAdminAccountDtoAsync(account);
        return Ok(dto);
    }

    private async Task<object> BuildAdminAccountDtoAsync(BillingAccount account)
    {
        var now = DateTime.UtcNow;
        var sub = await db.AccountSubscriptions.Include(s => s.PlanConfig).FirstOrDefaultAsync(s => s.BillingAccountId == account.Id);
        var plan = await subscriptionResolver.GetEffectivePlanForAccountAsync(account.Id);
        var usage = (await usageReader.GetAsync([account.Id])).GetValueOrDefault(account.Id) ?? new AccountUsage(account.Id, 0, 0);

        var subscribedOptions = await db.AccountSubscriptionOptions.Include(o => o.Option)
            .Where(o => o.BillingAccountId == account.Id).Where(o => o.EndsAtUtc == null || o.EndsAtUtc > now).ToListAsync();
        var planRules = sub?.PlanConfigId is { } planId ? await db.PlanOptionRules.Where(r => r.PlanConfigId == planId).ToListAsync() : [];

        var companies = await db.Companies.Where(c => c.BillingAccountId == account.Id).ToListAsync();
        var companyIds = companies.Select(c => c.Id).ToList();
        var seatsByCompany = await usageReader.GetCompanySeatsAsync(companyIds);
        var ownerIds = companies.Select(c => c.OwnerUserId).Distinct().ToList();
        var ownerNames = await db.Users.Where(u => ownerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());

        var channels = await db.NotificationChannels.Include(c => c.Assignments)
            .Where(c => c.BillingAccountId == account.Id && c.State != ChannelState.Replaced)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).ToListAsync();
        var funding = ChannelFunding.Rank(channels, plan.PaidNotificationNumbers);

        var optionDtos = subscribedOptions.Select(o =>
        {
            var rule = planRules.FirstOrDefault(r => r.OptionId == o.OptionId);
            // N14 — a missing rule means Unavailable (fail-closed, §43.3), never Extra.
            var availability = rule?.Availability ?? OptionAvailability.Unavailable;
            var monthly = BillingCalculator.MonthlyPriceFor(availability, o.Quantity, o.Option.PricePerMonth ?? 0m, rule?.IncludedQuantity);
            // N5, §38.5 — same four-way status the owner screen now uses (OwnerSubscriptionService.
            // ToSubscribedOptionDto), so the admin card doesn't show "Active" for something the owner
            // sees as "Недоступно"/"Ожидает оплаты".
            var status = availability == OptionAvailability.Unavailable ? "Unavailable"
                : o.EndsAtUtc.HasValue ? "Ending"
                : o.RequestedAtUtc.HasValue ? "PendingPayment"
                : "Active";
            var statusText = status switch
            {
                "Unavailable" => "Недоступно на текущем тарифе",
                "Ending" => $"Действует до {o.EndsAtUtc:dd.MM.yyyy}",
                "PendingPayment" => "Есть заявка на изменение количества",
                _ => "Подключена",
            };
            return new
            {
                optionId = o.OptionId,
                name = o.Option.Name,
                description = o.Option.Description,
                kind = o.Option.Kind.ToString(),
                unitName = o.Option.UnitName,
                quantity = o.Quantity,
                pricePerUnit = o.Option.PricePerMonth ?? 0m,
                pricePerMonth = monthly,
                status,
                statusText,
                endsAt = o.EndsAtUtc,
                canDisable = true,
                paidUntil = o.PaidUntilUtc,
                requestedQuantity = o.RequestedQuantity,
                requestedAt = o.RequestedAtUtc,
            };
        }).ToList();

        var totalMonthlyPrice = (sub?.PlanConfig?.PricePerMonth ?? 0m) + optionDtos.Sum(o => o.pricePerMonth);

        var pendingRequest = ownerSubscriptionService.BuildPendingRequestDto(
            account, subscribedOptions.Select(o => o.Option).Concat(await db.SubscriptionOptions.ToListAsync()).DistinctBy(o => o.Id).ToList(),
            sub?.PlanConfig?.PricePerMonth ?? 0m);

        return new
        {
            id = account.Id,
            name = account.Name,
            ownerUserId = account.OwnerUserId,
            ownerName = $"{account.Owner.FirstName} {account.Owner.LastName}".Trim(),
            ownerPhoneMasked = account.Owner.PhoneNumber is null ? null : PhoneDisplayMask.Mask(account.Owner.PhoneNumber),
            currency = "RUB",
            status = OwnerSubscriptionService.SubscriptionStatusFor(sub, now),
            isActive = sub?.IsActive ?? false,
            planId = sub?.PlanConfigId,
            plan = new { id = sub?.PlanConfigId, name = sub?.PlanConfig?.Name ?? "Бесплатный", description = sub?.PlanConfig?.Description, pricePerMonth = sub?.PlanConfig?.PricePerMonth ?? 0m, includes = Array.Empty<string>() },
            options = optionDtos,
            totalMonthlyPrice,
            paidUntil = sub?.PaidUntil,
            companiesUsed = usage.CompaniesUsed,
            companiesLimit = plan.AccountMaxCompanies,
            employeesUsed = usage.SeatsUsed,
            employeesLimit = plan.AccountMaxEmployees,
            numbersPaid = plan.PaidNotificationNumbers,
            numbersRegistered = channels.Count,
            grandfatheredEmployeeBonus = account.GrandfatheredEmployeeBonus,
            grandfatheredEmployeeBonusText = account.GrandfatheredEmployeeBonus > 0
                ? $"Дополнительно {account.GrandfatheredEmployeeBonus} мест выдано миграцией тарифов." : null,
            companies = companies.Select(c => new
            {
                companyId = c.Id,
                companyName = c.Name,
                ownerUserId = c.OwnerUserId,
                ownerName = ownerNames.GetValueOrDefault(c.OwnerUserId),
                employeeCount = seatsByCompany.GetValueOrDefault(c.Id),
            }).ToList(),
            channels = channels.Select(c => new
            {
                channelId = c.Id,
                phoneMasked = c.PhoneNumber is null ? null : PhoneDisplayMask.Mask(c.PhoneNumber),
                state = c.State.ToString(),
                fundingState = funding.GetValueOrDefault(c.Id, ChannelFundingState.NotPaid).ToString(),
                createdAt = c.CreatedAt,
                assignedCompanies = c.Assignments.Count,
            }).ToList(),
            pendingRequest,
        };
    }

    [HttpPut("billing-accounts/{accountId:guid}/subscription")]
    public async Task<IActionResult> AssignSubscription(Guid accountId, [FromBody] Billing_AssignSubscriptionInput dto)
    {
        var account = await db.BillingAccounts.Include(a => a.Owner).FirstOrDefaultAsync(a => a.Id == accountId);
        if (account is null) return NotFound();

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
        }

        var optionIds = optionLines.Select(o => o.OptionId).ToList();
        var options = await db.SubscriptionOptions.Where(o => optionIds.Contains(o.Id)).ToListAsync();
        if (options.Count != optionIds.Distinct().Count())
            return BadRequest("Одна или несколько опций не найдены.");

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
        // summed limits are lower than what's already occupied, refuse without confirmLimitOverflow.
        // N17: must account for the options being assigned IN THIS SAME REQUEST too — an admin handing
        // out "plan + 10 extra seats" to an account already at 12 employees must not see a false 409
        // just because the check only looked at the bare plan's own limit.
        var extraEmployeesInRequest = optionLines
            .Where(l => options.First(o => o.Id == l.OptionId).CapabilityKey == CapabilityKeys.Employees)
            .Sum(l => l.Quantity);
        var extraCompaniesInRequest = optionLines
            .Where(l => options.First(o => o.Id == l.OptionId).CapabilityKey == CapabilityKeys.Companies)
            .Sum(l => l.Quantity);
        var basePlan = plan is not null ? EffectivePlan.FromConfig(plan) : EffectivePlan.Free;
        var newEffectivePlan = basePlan with
        {
            AccountMaxEmployees = basePlan.AccountMaxEmployees is { } maxE ? maxE + extraEmployeesInRequest : null,
            AccountMaxCompanies = basePlan.AccountMaxCompanies is { } maxC ? maxC + extraCompaniesInRequest : null,
        };
        var usage = (await usageReader.GetAsync([accountId])).GetValueOrDefault(accountId) ?? new AccountUsage(accountId, 0, 0);
        if (!dto.ConfirmLimitOverflow)
        {
            if (newEffectivePlan.AccountMaxCompanies is { } maxCompanies && usage.CompaniesUsed > maxCompanies)
                return Conflict($"На новом тарифе доступно {maxCompanies} компаний, занято {usage.CompaniesUsed}. Подтвердите превышение лимита, чтобы продолжить.");
            if (newEffectivePlan.AccountMaxEmployees is { } maxEmployees && usage.SeatsUsed > maxEmployees + account.GrandfatheredEmployeeBonus)
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

        sub.PlanConfigId = dto.PlanId;
        sub.IsActive = dto.IsActive;
        sub.PaidUntil = ToUtc(dto.PaidUntil);
        sub.UpdatedAt = now;

        var existingOptions = await db.AccountSubscriptionOptions.Where(o => o.BillingAccountId == accountId).ToListAsync();
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

    private async Task<string?> BuildOptionsSummaryAsync(Guid accountId)
    {
        var rows = await db.AccountSubscriptionOptions.Include(o => o.Option)
            .Where(o => o.BillingAccountId == accountId && (o.EndsAtUtc == null || o.EndsAtUtc > DateTime.UtcNow)).ToListAsync();
        return rows.Count == 0 ? null : string.Join(", ", rows.Select(r => $"{r.Option.Name} ×{r.Quantity}"));
    }

    [HttpGet("billing-accounts/{accountId:guid}/subscription-history")]
    public async Task<IActionResult> GetSubscriptionHistory(Guid accountId)
    {
        var ownerUserId = await db.BillingAccounts.Where(a => a.Id == accountId).Select(a => a.OwnerUserId).FirstOrDefaultAsync();
        if (ownerUserId is null) return NotFound();

        // N3, §49: pre-cycle-5 rows (cycles 1-4) have BillingAccountId == NULL — the column didn't
        // exist yet — so they only match by OwnerUserId, and their ChangeKind is Legacy (the backfilled
        // default, §43.4). Without the OR, every subscription event from before this cycle shipped is
        // invisible in the admin's own history screen (US-73).
        var logs = await db.SubscriptionChangeLogs
            .Where(l => l.BillingAccountId == accountId || (l.BillingAccountId == null && l.OwnerUserId == ownerUserId))
            .OrderByDescending(l => l.ChangedAt).ToListAsync();

        var planIds = logs.SelectMany(l => new[] { l.OldPlanConfigId, l.NewPlanConfigId }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var planNames = await db.SubscriptionPlanConfigs.Where(p => planIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name);

        var changedByIds = logs.Select(l => l.ChangedByUserId).Distinct().ToList();
        var changedByNames = await db.Users.Where(u => changedByIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());

        var items = logs.Select(l => new
        {
            id = l.Id,
            changedAt = l.ChangedAt,
            changedByName = changedByNames.GetValueOrDefault(l.ChangedByUserId, l.ChangedByUserId),
            changeKind = l.ChangeKind.ToString(),
            companyId = l.CompanyId,
            oldPlanName = l.OldPlanConfigId.HasValue ? planNames.GetValueOrDefault(l.OldPlanConfigId.Value, "—") : null,
            newPlanName = l.NewPlanConfigId.HasValue ? planNames.GetValueOrDefault(l.NewPlanConfigId.Value, "—") : null,
            oldPaidUntil = l.OldPaidUntil,
            newPaidUntil = l.NewPaidUntil,
            oldOptionsSummary = l.OldOptionsSummary,
            newOptionsSummary = l.NewOptionsSummary,
            amount = (decimal?)null,
            comment = l.Comment,
        }).ToList();

        return Ok(new { items });
    }

    // ── Subscription requests queue (US-67, US-70) ────────────────────────────────

    [HttpGet("subscription-requests")]
    public async Task<IActionResult> GetSubscriptionRequests([FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var (currentPage, currentPageSize) = Pagination.Normalize(page, pageSize);

        // Only "Pending" is ever non-empty — see BillingAccount's own remarks: an approved/rejected/
        // cancelled request simply clears its columns rather than being kept as a history row.
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase))
            return Ok(Pagination.CreateContract(new List<object>(), currentPage, currentPageSize, 0));

        // N19 — filtered, ordered and paged entirely in SQL; only the page's own rows come back, not
        // every pending request in the system.
        var pendingQuery = db.BillingAccounts.Where(a => a.RequestedAtUtc != null);
        var total = await pendingQuery.CountAsync();
        var page1 = await pendingQuery.Include(a => a.Owner).Include(a => a.RequestedPlan)
            .OrderBy(a => a.RequestedAtUtc)
            .Skip((currentPage - 1) * currentPageSize).Take(currentPageSize)
            .ToListAsync();
        var allOptions = await db.SubscriptionOptions.ToListAsync();

        var subs = await db.AccountSubscriptions.Include(s => s.PlanConfig)
            .Where(s => s.BillingAccountId != null && page1.Select(a => a.Id).Contains(s.BillingAccountId!.Value)).ToListAsync();
        var companyCounts = await db.Companies.Where(c => c.BillingAccountId != null && page1.Select(a => a.Id).Contains(c.BillingAccountId!.Value))
            .GroupBy(c => c.BillingAccountId!.Value).Select(g => new { AccountId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.AccountId, x => x.Count);

        var items = page1.Select(a =>
        {
            var lines = OwnerSubscriptionService.DeserializeOptionLines(a.RequestedOptionsJson);
            var itemDtos = lines.Select(l => new { optionId = l.OptionId, name = allOptions.FirstOrDefault(o => o.Id == l.OptionId)?.Name ?? "—", quantity = l.Quantity }).ToList();
            var sub = subs.FirstOrDefault(s => s.BillingAccountId == a.Id);
            var estimated = (sub?.PlanConfig?.PricePerMonth ?? 0m) + lines.Sum(l => (allOptions.FirstOrDefault(o => o.Id == l.OptionId)?.PricePerMonth ?? 0m) * l.Quantity);
            return new
            {
                id = a.Id,
                billingAccountId = a.Id,
                accountName = a.Name,
                requestedByName = $"{a.Owner.FirstName} {a.Owner.LastName}".Trim(),
                requestedByPhoneMasked = a.Owner.PhoneNumber is null ? null : PhoneDisplayMask.Mask(a.Owner.PhoneNumber),
                createdAt = a.RequestedAtUtc,
                status = "Pending",
                currentPlanName = sub?.PlanConfig?.Name,
                desiredPlanName = a.RequestedPlan?.Name,
                items = itemDtos,
                estimatedMonthlyPrice = estimated,
                comment = a.RequestedComment,
                companiesCount = companyCounts.GetValueOrDefault(a.Id, 0),
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

// ── Local input DTOs (kept private-ish to this controller; distinct names avoid clashing with the
// legacy record types already declared at the bottom of AdminController.cs) ─────────────────────────
public record Billing_AdminOptionInput(
    string Code, string Name, string? Description, string Kind, string? CapabilityKey,
    decimal? PricePerMonth, string? UnitName, int? MaxQuantity, bool IsPublic = false, bool IsActive = true, int SortOrder = 0);

public record Billing_AssignOptionInput(Guid OptionId, int Quantity, DateOnly? PaidUntil);

public record Billing_AssignSubscriptionInput(
    Guid? PlanId, bool IsActive, DateOnly? PaidUntil, List<Billing_AssignOptionInput> Options,
    decimal? Amount, string? Comment, Guid? RequestId, bool ConfirmLimitOverflow = false);

public record RejectRequestDto(string? Comment);
