using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378, §385): the admin billing-account card
/// (<c>GET /api/admin/billing-accounts/{id}</c>, and the body every trial grant/regrant and subscription
/// assignment returns) — shared by <see cref="AdminBillingController"/> and <see cref="AdminTrialController"/>,
/// so moved here verbatim from the former <c>AdminBillingController.BuildAdminAccountDtoAsync</c>/
/// <c>BuildAdminTrialDtoAsync</c> rather than duplicated. Same queries, same order, same shape.
/// </summary>
internal static class AdminAccountDtoBuilder
{
    internal static async Task<object> BuildAsync(
        AppDbContext db, SubscriptionResolver subscriptionResolver, AccountUsageReader usageReader,
        OwnerSubscriptionService ownerSubscriptionService, BillingAccount account)
    {
        var now = DateTime.UtcNow;
        var sub = await db.AccountSubscriptions.Include(s => s.PlanConfig).FirstOrDefaultAsync(s => s.BillingAccountId == account.Id);
        var plan = await subscriptionResolver.GetEffectivePlanForAccountAsync(account.Id);
        var usage = (await usageReader.GetAsync([account.Id])).GetValueOrDefault(account.Id) ?? new AccountUsage(account.Id, 0, 0);

        // ARCHITECTURE_CYCLE19.md §386.1 — retired limit options never contribute to totalMonthlyPrice
        // or the "options" surfaces shown here.
        var subscribedOptions = await db.AccountSubscriptionOptions.WhereNotRetired().Include(o => o.Option)
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

        var subscriptionStatus = OwnerSubscriptionService.SubscriptionStatusFor(sub, now);
        var trialDto = await BuildAdminTrialDtoAsync(db, account, sub, now);

        return new
        {
            id = account.Id,
            name = account.Name,
            ownerUserId = account.OwnerUserId,
            ownerName = $"{account.Owner.FirstName} {account.Owner.LastName}".Trim(),
            ownerPhoneMasked = account.Owner.PhoneNumber is null ? null : PhoneDisplayMask.Mask(account.Owner.PhoneNumber),
            currency = "RUB",
            status = subscriptionStatus,
            statusText = OwnerSubscriptionService.StatusTextFor(subscriptionStatus, sub?.PaidUntil),
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
            trial = trialDto,
            // ARCHITECTURE_CYCLE24.md §459.5 — the subscription of the "Заказы" line next to the "Записи" one above.
            ordersSubscription = await BuildOrdersSubscriptionAsync(db, usageReader, account, now),
        };
    }

    private static async Task<object> BuildOrdersSubscriptionAsync(AppDbContext db, AccountUsageReader usageReader, BillingAccount account, DateTime now)
    {
        var sub = await db.OrdersSubscriptions.Include(s => s.PlanConfig).FirstOrDefaultAsync(s => s.BillingAccountId == account.Id);
        var plan = OrdersPlanResolver.Resolve(sub, await db.SubscriptionPlanConfigs.AsNoTracking().FirstOrDefaultAsync(p => p.IsSystemFree && p.Line == CompanyKind.Orders), now);
        var usage = (await usageReader.GetAsync([account.Id], CompanyKind.Orders)).GetValueOrDefault(account.Id) ?? new AccountUsage(account.Id, 0, 0);
        var firstShopZone = await db.Companies.AsNoTracking().Where(c => c.BillingAccountId == account.Id && c.Kind == CompanyKind.Orders)
            .OrderBy(c => c.CreatedAt).Select(c => c.TimeZoneId).FirstOrDefaultAsync() ?? "Europe/Moscow";
        var local = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, TimeZoneInfo.FindSystemTimeZoneById(firstShopZone)));
        var month = new DateOnly(local.Year, local.Month, 1);
        var ordersThisMonth = await db.OrderMonthlyUsages.AsNoTracking().Where(u => u.BillingAccountId == account.Id && u.Month == month)
            .Select(u => (int?)u.Count).FirstOrDefaultAsync() ?? 0;
        return new
        {
            planId = plan.IsFreeTier ? null : plan.PlanId,
            planName = plan.PlanName,
            paidUntil = sub?.PaidUntil,
            isActive = sub?.IsActive ?? false,
            isFreeTier = plan.IsFreeTier,
            shopsUsed = usage.CompaniesUsed,
            shopsLimit = plan.MaxShops,
            seatsUsed = usage.SeatsUsed,
            seatsLimit = plan.MaxSeats,
            ordersThisMonth,
            ordersLimit = plan.MaxOrdersPerMonth,
        };
    }

    // Cycle 18 (API_CONTRACT_CYCLE18.md §369) — no phone number or its hash anywhere in here (the
    // trial-phone registry is never exposed by any DTO, admin included — §3 SPEC minimization).
    // AdminAccountTrialDto.state is a closed enum [Never, Active, Expired] and `trial` itself is NOT
    // nullable (contract fix, code-review finding #2) — an account that never had a trial gets
    // { state: "Never" }, not a null object, so the frontend's grant-trial button (rendered only when
    // state == "Never") is reachable for the one case it actually exists for.
    private static async Task<object> BuildAdminTrialDtoAsync(AppDbContext db, BillingAccount account, AccountSubscription? sub, DateTime now)
    {
        if (account.TrialStartedAtUtc is null) return new { state = "Never" };

        var isCurrentlyUsable = SubscriptionUsability.IsUsable(sub, now)
            && account.TrialEndsAtUtc.HasValue && account.TrialEndsAtUtc >= now;
        var state = isCurrentlyUsable ? "Active" : "Expired";

        var grants = await db.TrialGrants.Where(g => g.BillingAccountId == account.Id && g.Line == CompanyKind.Services)
            .OrderByDescending(g => g.GrantedAtUtc).ToListAsync();
        var grantedByIds = grants.Select(g => g.GrantedByUserId).Distinct().ToList();
        var grantedByNames = await db.Users.Where(u => grantedByIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());

        int? daysLeft = isCurrentlyUsable && account.TrialEndsAtUtc.HasValue
            ? Math.Max((int)Math.Ceiling((account.TrialEndsAtUtc.Value - now).TotalDays), 0)
            : null;

        return new
        {
            state,
            startedAt = account.TrialStartedAtUtc,
            endsAt = account.TrialEndsAtUtc,
            daysLeft,
            durationDays = account.TrialDurationDays,
            warningThresholdsDays = Services.Billing.TrialWindow.ParseThresholds(account.TrialWarningThresholdsDays),
            grantSource = account.TrialGrantSource?.ToString(),
            grantedByName = account.TrialGrantSource == Core.Enums.TrialGrantSource.OwnerSelfService
                ? null : grantedByNames.GetValueOrDefault(account.TrialGrantedByUserId ?? string.Empty),
            termsVersion = account.TrialTermsVersion,
            termsShownAt = account.TrialGrantSource == Core.Enums.TrialGrantSource.OwnerSelfService
                ? account.TrialStartedAtUtc : null,
            termsAcknowledgedAt = account.TrialTermsAcknowledgedAtUtc,
            mailingWindow = new
            {
                state = account.TrialChannelFirstAuthorizedAtUtc is null ? "NotStarted"
                    : account.TrialMailingWindowEndsAtUtc is null || account.TrialMailingWindowEndsAtUtc <= now ? "Ended"
                    : "Running",
                startedAt = account.TrialChannelFirstAuthorizedAtUtc,
                endsAt = account.TrialMailingWindowEndsAtUtc,
                daysLeft = account.TrialMailingWindowEndsAtUtc is { } end && end > now
                    ? (int?)Math.Ceiling((end - now).TotalDays) : null,
            },
            grants = grants.Select(g => new
            {
                grantedAt = g.GrantedAtUtc,
                endsAt = g.EndsAtUtc,
                source = g.Source.ToString(),
                grantedByName = g.Source == Core.Enums.TrialGrantSource.OwnerSelfService
                    ? "—" : grantedByNames.GetValueOrDefault(g.GrantedByUserId, g.GrantedByUserId),
                reason = g.Reason,
                termsVersion = g.TermsVersion,
                termsSha256 = g.TermsTextSha256,
                termsShownAt = g.TermsShownAtUtc,
                termsAcknowledgedAt = g.TermsAcknowledgedAtUtc,
                warningThresholdsDays = Services.Billing.TrialWindow.ParseThresholds(g.WarningThresholdsDays),
            }).ToList(),
        };
    }
}
