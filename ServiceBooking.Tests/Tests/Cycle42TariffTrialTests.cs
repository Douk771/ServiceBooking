using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Baths;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Baths;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Цикл 42, «Бани»: тарифная линейка и триал (BE-42-2, BE-42-3, стыки BE-42-I) — CY42-20…29. По API_CONTRACT_CYCLE42.md §42.21, §42.27.4, §42.29, §42.34 и
/// ARCHITECTURE_CYCLE42.md §42.5. Подписка «Бани» для подготовки данных пишется прямо в БД; предмет проверки — HTTP-поведение.
/// </summary>
public class Cycle42TariffTrialTests(TestDatabaseFixture fixture) : Cycle39TestBase(fixture)
{
    private sealed record Bath(AuthResponseDto Owner, string Token, BathsCompanyManageDto Company)
    {
        public Guid Id => Company.Id;
    }

    private async Task<AuthResponseDto> VerifiedUserAsync()
    {
        var user = await RegisterAsync();
        await MarkPhoneVerifiedAsync(user.Phone, user.UserId);
        return user;
    }

    private async Task<BathsCompanyCreatedDto> CreateBathsAsync(string token, string? trialTermsVersion = null)
    {
        var r = await AuthedClient(token).PostJsonAsync("/api/baths/companies", new
        {
            name = Unique("Баня "), slug = Unique("bn-").ToLowerInvariant(), cityId = await AnyCityIdAsync(), address = "Шерегеш, ул. Лесная, 5",
            phone = "+79001112233", description = (string?)null, ownerTermsVersion = CurrentOwnerTermsDto().Version, trialTermsVersion
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<BathsCompanyCreatedDto>())!;
    }

    private async Task<Bath> NewBathAsync(bool verified = true, string? trialTermsVersion = null)
    {
        var owner = verified ? await VerifiedUserAsync() : await RegisterAsync();
        var created = await CreateBathsAsync(owner.Token, trialTermsVersion);
        return new Bath(owner, created.Token, created.Company);
    }

    private async Task<BathsCompanyManageDto> CardAsync(Bath b)
    {
        var r = await AuthedClient(b.Token).GetAsync($"/api/baths/companies/{b.Id}");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<BathsCompanyManageDto>())!;
    }

    private Task<Guid> AccountIdAsync(Guid companyId) =>
        WithDbAsync(db => db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.BillingAccountId!.Value).SingleAsync());

    private async Task<StaysTrialStateDto> TrialStateAsync(string token)
    {
        var r = await AuthedClient(token).GetAsync("/api/baths/trial");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<StaysTrialStateDto>())!;
    }

    private async Task<(HttpStatusCode Status, StaysTrialOutcomeDto Outcome)> ActivateTrialAsync(string token, string? termsVersion)
    {
        var r = await AuthedClient(token).PostJsonAsync("/api/baths/trial", new StaysTrialInput(termsVersion));
        return (r.StatusCode, (await r.Content.ReadJsonAsync<StaysTrialOutcomeDto>())!);
    }

    private static string TermsVersion => BathsTrialTerms.Version;

    /// <summary>Подписка «Бани» напрямую в БД (подготовка данных).</summary>
    private Task SetBathsPlanAsync(Guid companyId, Guid planId, DateTime? paidUntil = null, bool isActive = true) => WithDbAsync(async db =>
    {
        var accountId = await db.Companies.Where(c => c.Id == companyId).Select(c => c.BillingAccountId!.Value).FirstAsync();
        var sub = await db.BathsSubscriptions.FirstOrDefaultAsync(s => s.BillingAccountId == accountId);
        if (sub is null) db.BathsSubscriptions.Add(sub = new BathsSubscription { Id = Guid.NewGuid(), BillingAccountId = accountId });
        sub.PlanConfigId = planId;
        sub.IsActive = isActive;
        sub.PaidUntil = paidUntil ?? DateTime.UtcNow.AddYears(1);
        await db.SaveChangesAsync();
    });

    /// <summary>Ресурс через HTTP до публикации: создан, настроен (вместимость), окна, цена. Публикацию делает вызывающий.</summary>
    private async Task<Guid> DraftResourceAsync(Bath b, string? name = null)
    {
        var client = AuthedClient(b.Token);
        var baseUrl = $"/api/baths/companies/{b.Id}/services";
        var created = await client.PostJsonAsync(baseUrl, new ServiceCreateInput(name ?? "Русская баня"));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadJsonAsync<ServiceManageDto>())!.Id;
        var setup = await client.PutJsonAsync($"{baseUrl}/{id}/setup", new ServiceSetupInput(
            name ?? "Русская баня", Unique("res-").ToLowerInvariant(), 2, 6, 60, 30, false, 0, null, StayServiceCancellationPolicy.NoDeductions, 12, false, Capacity: 6));
        setup.StatusCode.Should().Be(HttpStatusCode.OK, await setup.Content.ReadAsStringAsync());
        var days = Enumerable.Range(1, 7).Select(d => new WeeklyDayInput(d, [new ServiceWindowInput(DefaultWindow.Start, DefaultWindow.End)])).ToList();
        (await client.PutJsonAsync($"{baseUrl}/{id}/weekly-schedule", new WeeklyScheduleInput(days))).StatusCode.Should().Be(HttpStatusCode.OK);
        var rule = await client.PostJsonAsync($"{baseUrl}/{id}/price-rules", new PriceRuleInput(127, 6, 30, 2000));
        rule.StatusCode.Should().Be(HttpStatusCode.Created, await rule.Content.ReadAsStringAsync());
        return id;
    }

    private Task<HttpResponseMessage> PublishAsync(Bath b, Guid resourceId) =>
        AuthedClient(b.Token).PostJsonAsync($"/api/baths/companies/{b.Id}/services/{resourceId}/publish", new { });

    private async Task PublishOkAsync(Bath b, Guid resourceId)
    {
        var r = await PublishAsync(b, resourceId);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
    }

    private async Task<HttpClient> AdminAsync() => AuthedClient((await LoginAsSuperAdminAsync()).Token);

    private static object AssignBody(Guid? planId, string line = "Baths", bool confirm = false) => new
    {
        planId, isActive = true, paidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)).ToString("yyyy-MM-dd"), line, confirmLimitOverflow = confirm,
        options = Array.Empty<object>()
    };

    // ── CY42-20: триал, состояние ────────────────────────────────────────────────

    [Fact, TestCase("CY42-20")]
    public async Task TrialState_Unverified_IsClosed_Verified_IsOfferedWithOwnTerms()
    {
        var unverified = await RegisterAsync();
        var closed = await TrialStateAsync(unverified.Token);
        closed.Offered.Should().BeTrue();
        closed.Eligible.Should().BeFalse();
        closed.RefusalCode.Should().BeOneOf("PhoneNotVerified", "PhoneVerificationUnavailable");

        var verified = await VerifiedUserAsync();
        var open = await TrialStateAsync(verified.Token);
        open.Offered.Should().BeTrue();
        open.Eligible.Should().BeTrue();
        open.RefusalCode.Should().BeNull();
        open.DurationDays.Should().Be(14);
        open.TermsVersion.Should().Be("baths-2026-10-10", "у «Бань» своя редакция условий, не «Домов»");
        open.TermsVersion.Should().NotBe(StaysTrialTerms.Version);
        open.TermsText.Should().Contain("«Бани»").And.Contain("14 дней").And.Contain("в пробный период не включаются").And.NotContain("доступны весь пробный период");

        (await AnonymousClient().GetAsync("/api/baths/trial")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("CY42-20")]
    public async Task TrialActivate_Grants14Days_WritesGrantWithTermsSnapshot_AndOpensThePlan()
    {
        var b = await NewBathAsync();
        (await CardAsync(b)).Plan.WarningLevel.Should().Be("NoPlan");

        var (status, outcome) = await ActivateTrialAsync(b.Token, TermsVersion);
        status.Should().Be(HttpStatusCode.OK);
        outcome.Granted.Should().BeTrue();
        outcome.EndsAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(14), TimeSpan.FromMinutes(5));

        var card = await CardAsync(b);
        card.Plan.IsTrial.Should().BeTrue();
        card.Plan.PlanName.Should().Contain("Пробный период");
        card.Plan.MaxResources.Should().BeNull();
        card.Plan.WarningLevel.Should().Be("None");
        card.Checklist.Single(i => i.Code == "Plan").Done.Should().BeTrue();

        var accountId = await AccountIdAsync(b.Id);
        await WithDbAsync(async db =>
        {
            var grant = await db.TrialGrants.AsNoTracking().SingleAsync(g => g.BillingAccountId == accountId && g.Line == CompanyKind.Baths);
            grant.Source.Should().Be(TrialGrantSource.OwnerSelfService);
            grant.PlanConfigId.Should().Be(BathsPlans.TrialSeedId);
            grant.DurationDays.Should().Be(14);
            grant.MailingWindowDays.Should().Be(0);
            grant.TermsVersion.Should().Be(TermsVersion);
            grant.TermsTextSha256.Should().Be(BathsTrialTerms.Sha256).And.HaveLength(64);
            grant.TermsAcknowledgedAtUtc.Should().NotBeNull();
            grant.EndsAtUtc.Should().BeCloseTo(outcome.EndsAtUtc!.Value, TimeSpan.FromSeconds(1));
            var sub = await db.BathsSubscriptions.AsNoTracking().SingleAsync(s => s.BillingAccountId == accountId);
            sub.PlanConfigId.Should().Be(BathsPlans.TrialSeedId);
            sub.IsActive.Should().BeTrue();
            (await db.StaysSubscriptions.AnyAsync(s => s.BillingAccountId == accountId)).Should().BeFalse("триал «Бань» не трогает подписку «Домов»");
        });

        var state = await TrialStateAsync(b.Token);
        state.Eligible.Should().BeFalse();
        state.RefusalCode.Should().Be("TrialAlreadyActive");
        state.EndsAtUtc.Should().BeCloseTo(outcome.EndsAtUtc!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact, TestCase("CY42-20")]
    public async Task CreateCompany_WithTrialTermsVersion_GrantsTheTrialAfterCommit()
    {
        var owner = await VerifiedUserAsync();
        var created = await CreateBathsAsync(owner.Token, TermsVersion);
        created.Trial.Should().NotBeNull();
        created.Trial!.Granted.Should().BeTrue(created.Trial.Message);
        created.Trial.EndsAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(14), TimeSpan.FromMinutes(5));
        created.Company.Plan.IsTrial.Should().BeTrue();
        created.Company.Plan.WarningLevel.Should().Be("None");
        created.Company.Gate.ReasonCode.Should().Be("NoProviderInfo", "тариф есть, не хватает сведений об исполнителе");
    }

    [Fact, TestCase("CY42-20")]
    public async Task CreateCompany_RefusedTrial_NeverUndoesTheCreation()
    {
        var unverified = await RegisterAsync();
        var created = await CreateBathsAsync(unverified.Token, TermsVersion);
        created.Trial!.Granted.Should().BeFalse();
        created.Trial.RefusalCode.Should().BeOneOf("PhoneNotVerified", "PhoneVerificationUnavailable");
        created.Company.Plan.WarningLevel.Should().Be("NoPlan");
        created.Company.Gate.ReasonCode.Should().Be("NoPlan");
        (await AuthedClient(created.Token).GetAsync($"/api/baths/companies/{created.Company.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var stale = await CreateBathsAsync((await LoginAsync(unverified.Phone, "Password123!")).Token, "baths-1999-01-01");
        stale.Trial!.Granted.Should().BeFalse();
        stale.Trial.RefusalCode.Should().Be("TrialTermsVersionMismatch");
    }

    // ── CY42-21: триал, отказы ───────────────────────────────────────────────────

    [Fact, TestCase("CY42-21")]
    public async Task TrialActivate_Refusals_AreCodedConflicts_WithServerTexts()
    {
        var user = await RegisterAsync(); // номер не подтверждён
        var mismatch = await ActivateTrialAsync(user.Token, "stale");
        mismatch.Status.Should().Be(HttpStatusCode.Conflict);
        mismatch.Outcome.Granted.Should().BeFalse();
        mismatch.Outcome.RefusalCode.Should().Be("TrialTermsVersionMismatch");
        mismatch.Outcome.Message.Should().Be(StaysTrialTerms.VersionMismatch);
        (await ActivateTrialAsync(user.Token, null)).Outcome.RefusalCode.Should().Be("TrialTermsVersionMismatch");

        var noPhone = await ActivateTrialAsync(user.Token, TermsVersion);
        noPhone.Status.Should().Be(HttpStatusCode.Conflict);
        noPhone.Outcome.RefusalCode.Should().BeOneOf("PhoneNotVerified", "PhoneVerificationUnavailable");

        (await AnonymousClient().PostJsonAsync("/api/baths/trial", new StaysTrialInput(TermsVersion))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // платный тариф «Бань» — триал не выдаётся поверх
        var b = await NewBathAsync();
        await SetBathsPlanAsync(b.Id, BathsPlans.UpToThreeSeedId);
        var paid = await ActivateTrialAsync(b.Token, TermsVersion);
        paid.Status.Should().Be(HttpStatusCode.Conflict);
        paid.Outcome.RefusalCode.Should().Be("AlreadyOnPaidPlan");
        paid.Outcome.Message.Should().Be(StaysTrialTerms.AlreadyOnPaidPlan);
        (await TrialStateAsync(b.Token)).RefusalCode.Should().Be("AlreadyOnPaidPlan");

        // повторная активация — «уже активен»
        var c = await NewBathAsync();
        (await ActivateTrialAsync(c.Token, TermsVersion)).Outcome.Granted.Should().BeTrue();
        var again = await ActivateTrialAsync(c.Token, TermsVersion);
        again.Status.Should().Be(HttpStatusCode.Conflict);
        again.Outcome.RefusalCode.Should().Be("TrialAlreadyActive");
        again.Outcome.Message.Should().Be(StaysTrialTerms.AlreadyActive);
    }

    [Fact, TestCase("CY42-21")]
    public async Task TrialActivate_PlanSwitchedOff_IsNotOffered_ThenOffered_Again()
    {
        var b = await NewBathAsync();
        try
        {
            await WithDbAsync(async db =>
            {
                (await db.SubscriptionPlanConfigs.SingleAsync(p => p.Id == BathsPlans.TrialSeedId)).IsActive = false;
                await db.SaveChangesAsync();
            });
            var state = await TrialStateAsync(b.Token);
            state.Offered.Should().BeFalse();
            state.RefusalCode.Should().Be("TrialNotOffered");
            var refused = await ActivateTrialAsync(b.Token, TermsVersion);
            refused.Status.Should().Be(HttpStatusCode.Conflict);
            refused.Outcome.RefusalCode.Should().Be("TrialNotOffered");
            refused.Outcome.Message.Should().Be(StaysTrialTerms.NotOffered);
        }
        finally
        {
            await WithDbAsync(async db =>
            {
                (await db.SubscriptionPlanConfigs.SingleAsync(p => p.Id == BathsPlans.TrialSeedId)).IsActive = true;
                await db.SaveChangesAsync();
            });
        }
    }

    // ── CY42-22: однократность в линейке ─────────────────────────────────────────

    [Fact, TestCase("CY42-22")]
    public async Task Trial_IsOncePerAccountInTheLine_AfterItEnded_NoSecondOne()
    {
        var b = await NewBathAsync();
        (await ActivateTrialAsync(b.Token, TermsVersion)).Outcome.Granted.Should().BeTrue();

        // триал закончился
        await SetBathsPlanAsync(b.Id, BathsPlans.TrialSeedId, paidUntil: DateTime.UtcNow.AddMinutes(-5));
        var expired = await CardAsync(b);
        expired.Plan.WarningLevel.Should().Be("Expired");
        expired.Plan.Text.Should().Be("Пробный период закончился. Гости не могут бронировать, пока вы не выберете тариф");
        expired.Gate.Accepting.Should().BeFalse();
        expired.Gate.ReasonCode.Should().Be("NoPlan");

        var state = await TrialStateAsync(b.Token);
        state.Eligible.Should().BeFalse();
        state.RefusalCode.Should().Be("TrialAlreadyUsed");
        state.Message.Should().Be(StaysTrialTerms.AlreadyUsed);
        var second = await ActivateTrialAsync(b.Token, TermsVersion);
        second.Status.Should().Be(HttpStatusCode.Conflict);
        second.Outcome.RefusalCode.Should().Be("TrialAlreadyUsed");

        var accountId = await AccountIdAsync(b.Id);
        (await WithDbAsync(db => db.TrialGrants.CountAsync(g => g.BillingAccountId == accountId && g.Line == CompanyKind.Baths))).Should().Be(1);
    }

    [Fact, TestCase("CY42-22")]
    public async Task Trial_IsOncePerPhoneInTheLine_EvenAfterTheFirstAccountIsGone()
    {
        var phone = UniquePhone();
        var a = await RegisterAsync();
        await MarkPhoneVerifiedAsync(phone, a.UserId);
        (await ActivateTrialAsync(a.Token, TermsVersion)).Outcome.Granted.Should().BeTrue();
        // аккаунт A «удалён»: подтверждённый номер освободился, а реестр однократности остаётся
        await WithDbAsync(async db =>
        {
            db.VerifiedPhones.RemoveRange(await db.VerifiedPhones.Where(v => v.UserId == a.UserId).ToListAsync());
            await db.SaveChangesAsync();
        });

        var b = await RegisterAsync();
        await MarkPhoneVerifiedAsync(phone, b.UserId);
        var denied = await ActivateTrialAsync(b.Token, TermsVersion);
        denied.Status.Should().Be(HttpStatusCode.Conflict);
        denied.Outcome.RefusalCode.Should().Be("TrialPhoneAlreadyUsed");
        denied.Outcome.Message.Should().Be(StaysTrialTerms.PhoneAlreadyUsed).And.NotContain(a.UserId);
    }

    // ── CY42-23: независимость от триалов других линеек ──────────────────────────

    [Fact, TestCase("CY42-23")]
    public async Task Trial_IsIndependentOfTheStaysTrial_InBothOrders_AndPhoneRegistryIsPerLine()
    {
        // сначала «Дома», потом «Бани» — один аккаунт
        var first = await VerifiedUserAsync();
        var staysFirst = await AuthedClient(first.Token).PostJsonAsync("/api/stays/trial", new StaysTrialInput(StaysTrialTerms.Version));
        staysFirst.StatusCode.Should().Be(HttpStatusCode.OK, await staysFirst.Content.ReadAsStringAsync());
        (await TrialStateAsync(first.Token)).Eligible.Should().BeTrue("триал «Домов» на «Бани» не влияет");
        (await ActivateTrialAsync(first.Token, TermsVersion)).Outcome.Granted.Should().BeTrue();

        // сначала «Бани», потом «Дома»
        var second = await VerifiedUserAsync();
        (await ActivateTrialAsync(second.Token, TermsVersion)).Outcome.Granted.Should().BeTrue();
        var stateStays = await J(await AuthedClient(second.Token).GetAsync("/api/stays/trial"));
        stateStays.GetProperty("eligible").GetBoolean().Should().BeTrue("триал «Бань» на «Дома» не влияет");
        var staysSecond = await AuthedClient(second.Token).PostJsonAsync("/api/stays/trial", new StaysTrialInput(StaysTrialTerms.Version));
        staysSecond.StatusCode.Should().Be(HttpStatusCode.OK, await staysSecond.Content.ReadAsStringAsync());

        foreach (var user in new[] { first, second })
        {
            var lines = await WithDbAsync(db => db.TrialGrants.AsNoTracking()
                .Where(g => db.BillingAccounts.Any(x => x.Id == g.BillingAccountId && x.OwnerUserId == user.UserId)).Select(g => g.Line).ToListAsync());
            lines.Should().BeEquivalentTo([CompanyKind.Stays, CompanyKind.Baths]);
            await WithDbAsync(async db =>
            {
                var accountId = await db.BillingAccounts.Where(x => x.OwnerUserId == user.UserId).Select(x => x.Id).SingleAsync();
                (await db.StaysSubscriptions.SingleAsync(s => s.BillingAccountId == accountId)).PlanConfigId.Should().Be(StaysPlans.TrialSeedId);
                (await db.BathsSubscriptions.SingleAsync(s => s.BillingAccountId == accountId)).PlanConfigId.Should().Be(BathsPlans.TrialSeedId);
            });
        }

        // реестр номеров ведётся по линейкам: тот же номер, чужой аккаунт — «Дома» закрыты, «Бани» тоже (оба уже записаны); третья линейка не нужна
        var rows = await WithDbAsync(db => db.TrialPhoneRegistrations.AsNoTracking().Select(r => r.Line).Distinct().ToListAsync());
        rows.Should().Contain([CompanyKind.Stays, CompanyKind.Baths]);
    }

    [Fact, TestCase("CY42-23")]
    public async Task Trial_PhoneUsedInStaysOnly_StillGetsTheBathsTrial()
    {
        var phone = UniquePhone();
        var a = await RegisterAsync();
        await MarkPhoneVerifiedAsync(phone, a.UserId);
        (await AuthedClient(a.Token).PostJsonAsync("/api/stays/trial", new StaysTrialInput(StaysTrialTerms.Version))).StatusCode.Should().Be(HttpStatusCode.OK);
        await WithDbAsync(async db =>
        {
            db.VerifiedPhones.RemoveRange(await db.VerifiedPhones.Where(v => v.UserId == a.UserId).ToListAsync());
            await db.SaveChangesAsync();
        });

        var b = await RegisterAsync();
        await MarkPhoneVerifiedAsync(phone, b.UserId);
        var baths = await ActivateTrialAsync(b.Token, TermsVersion);
        baths.Status.Should().Be(HttpStatusCode.OK, "номер уже брал триал «Домов», но не «Бань»: " + baths.Outcome.Message);
        baths.Outcome.Granted.Should().BeTrue();
        var staysRefused = await AuthedClient(b.Token).PostJsonAsync("/api/stays/trial", new StaysTrialInput(StaysTrialTerms.Version));
        staysRefused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await staysRefused.Content.ReadJsonAsync<StaysTrialOutcomeDto>())!.RefusalCode.Should().Be("TrialPhoneAlreadyUsed");
    }

    // ── CY42-24: гейт и 402 публикации ───────────────────────────────────────────

    [Fact, TestCase("CY42-24")]
    public async Task Gate_OpensStepByStep_NoPlan_NoProviderInfo_ThenAccepting_ThenNoPaymentDetails_ThenOverResourceLimit()
    {
        var b = await NewBathAsync();
        var client = AuthedClient(b.Token);
        var card = await CardAsync(b);
        card.Gate.Accepting.Should().BeFalse();
        card.Gate.ReasonCode.Should().Be("NoPlan");

        (await ActivateTrialAsync(b.Token, TermsVersion)).Outcome.Granted.Should().BeTrue();
        card = await CardAsync(b);
        card.Gate.ReasonCode.Should().Be("NoProviderInfo");
        card.Gate.ReasonText.Should().NotBeNullOrWhiteSpace();

        var provider = await client.PutJsonAsync($"/api/baths/companies/{b.Id}/provider",
            new ProviderInput(StayProviderStatus.SelfEmployed, "Иванов Иван Иванович", ValidPersonInn, null, "г. Новокузнецк, ул. Мира, 1"));
        provider.StatusCode.Should().Be(HttpStatusCode.OK, await provider.Content.ReadAsStringAsync());
        (await provider.Content.ReadJsonAsync<BathsCompanyManageDto>())!.Gate.Accepting.Should().BeTrue();

        // ресурс с предоплатой и без реквизитов
        var id = await DraftResourceAsync(b, "Русская баня");
        var setup = await client.PutJsonAsync($"/api/baths/companies/{b.Id}/services/{id}/setup", new ServiceSetupInput(
            "Русская баня", Unique("res-").ToLowerInvariant(), 2, 6, 60, 30, false, 0, 30, StayServiceCancellationPolicy.NoDeductions, 12, false, Capacity: 6));
        setup.StatusCode.Should().Be(HttpStatusCode.OK, await setup.Content.ReadAsStringAsync());
        await PublishOkAsync(b, id);
        card = await CardAsync(b);
        card.Gate.ReasonCode.Should().Be("NoPaymentDetails");
        card.Plan.ResourcesPublished.Should().Be(1);

        (await client.PutJsonAsync($"/api/baths/companies/{b.Id}/payment-details", new PaymentDetailsDto(PaymentDetailsText, "Бронь бани"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CardAsync(b)).Gate.Accepting.Should().BeTrue();

        // понижение тарифа не снимает публикацию — закрывает приём
        var second = await DraftResourceAsync(b, "Купель");
        await PublishOkAsync(b, second);
        await SetBathsPlanAsync(b.Id, BathsPlans.OneBathSeedId);
        card = await CardAsync(b);
        card.Gate.Accepting.Should().BeFalse();
        card.Gate.ReasonCode.Should().Be("OverResourceLimit");
        card.Gate.ReasonText.Should().Be("Опубликовано 2 ресурса при лимите 1: гости не могут бронировать. Снимите лишние ресурсы с публикации или смените тариф");
        card.Plan.WarningLevel.Should().Be("OverLimit");
        card.Plan.ResourcesPublished.Should().Be(2);

        // истёкший платный тариф — снова NoPlan
        await SetBathsPlanAsync(b.Id, BathsPlans.UnlimitedSeedId, paidUntil: DateTime.UtcNow.AddDays(-1));
        card = await CardAsync(b);
        card.Gate.ReasonCode.Should().Be("NoPlan");
        card.Plan.WarningLevel.Should().Be("NoPlan");
    }

    [Fact, TestCase("CY42-24")]
    public async Task Publish_WithoutPlan_Returns402_ThenTrialAllows_ThenLimitIsPerAccount()
    {
        var b = await NewBathAsync();
        var first = await DraftResourceAsync(b, "Баня 1");
        var denied = await PublishAsync(b, first);
        denied.StatusCode.Should().Be((HttpStatusCode)402);
        (await denied.Content.ReadAsStringAsync()).Should().Be("Выберите тариф или активируйте пробный период, чтобы опубликовать баню");

        (await ActivateTrialAsync(b.Token, TermsVersion)).Outcome.Granted.Should().BeTrue();
        await PublishOkAsync(b, first);

        // тариф «Одна баня»: вторая баня того же аккаунта — 402 с названием тарифа и лимитом (счётчик общий на все банные компании аккаунта)
        await SetBathsPlanAsync(b.Id, BathsPlans.OneBathSeedId);
        var other = await CreateBathsAsync((await LoginAsync(b.Owner.Phone, "Password123!")).Token);
        var otherBath = new Bath(b.Owner, other.Token, other.Company);
        var second = await DraftResourceAsync(otherBath, "Баня 2");
        var limited = await PublishAsync(otherBath, second);
        limited.StatusCode.Should().Be((HttpStatusCode)402);
        (await limited.Content.ReadAsStringAsync()).Should().Be("Тариф «Одна баня» позволяет опубликовать 1 ресурс");

        // повторная публикация уже опубликованного — не 402 (идемпотентна), снятие с публикации тариф не проверяет
        await PublishOkAsync(b, first);
        var unpublish = await AuthedClient(b.Token).PostJsonAsync($"/api/baths/companies/{b.Id}/services/{first}/unpublish", new { });
        unpublish.StatusCode.Should().Be(HttpStatusCode.OK, await unpublish.Content.ReadAsStringAsync());
        await PublishOkAsync(otherBath, second); // место освободилось

        // «До 3 бань»: ещё две можно, четвёртую нельзя
        await SetBathsPlanAsync(b.Id, BathsPlans.UpToThreeSeedId);
        await PublishOkAsync(b, first);
        await PublishOkAsync(b, await DraftResourceAsync(b, "Баня 3"));
        var fourth = await PublishAsync(b, await DraftResourceAsync(b, "Баня 4"));
        fourth.StatusCode.Should().Be((HttpStatusCode)402);
        (await fourth.Content.ReadAsStringAsync()).Should().Be("Тариф «До 3 бань» позволяет опубликовать 3 ресурса");

        // «Без ограничения»
        await SetBathsPlanAsync(b.Id, BathsPlans.UnlimitedSeedId);
        await PublishOkAsync(b, await DraftResourceAsync(b, "Баня 5"));
    }

    [Fact, TestCase("CY42-24")]
    public async Task Publish_AfterTrialEnded_Returns402_ButUnpublishAndArchiveStillWork()
    {
        var b = await NewBathAsync();
        (await ActivateTrialAsync(b.Token, TermsVersion)).Outcome.Granted.Should().BeTrue();
        var published = await DraftResourceAsync(b, "Баня А");
        await PublishOkAsync(b, published);
        var draft = await DraftResourceAsync(b, "Баня Б");
        await SetBathsPlanAsync(b.Id, BathsPlans.TrialSeedId, paidUntil: DateTime.UtcNow.AddMinutes(-1));

        (await PublishAsync(b, draft)).StatusCode.Should().Be((HttpStatusCode)402);
        var client = AuthedClient(b.Token);
        (await client.PostJsonAsync($"/api/baths/companies/{b.Id}/services/{published}/unpublish", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostJsonAsync($"/api/baths/companies/{b.Id}/services/{published}/archive", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── CY42-25: кабинет подписки ────────────────────────────────────────────────

    [Fact, TestCase("CY42-25")]
    public async Task BillingSubscription_LineBaths_HasBathsBlock_AndPlansOfTheLineWithoutTrial()
    {
        var b = await NewBathAsync();
        await SetBathsPlanAsync(b.Id, BathsPlans.UpToThreeSeedId);
        var client = AuthedClient(b.Token);

        var sub = await J(await client.GetAsync("/api/billing/subscription?line=Baths"));
        sub.GetProperty("line").GetString().Should().Be("Baths");
        sub.TryGetProperty("stays", out var stays).Should().BeTrue();
        stays.ValueKind.Should().Be(JsonValueKind.Null, "блок «Домов» у линейки «Бани» опущен/пуст");
        var baths = sub.GetProperty("baths");
        baths.GetProperty("maxResources").GetInt32().Should().Be(3);
        baths.GetProperty("resourcesPublished").GetInt32().Should().Be(0);
        baths.GetProperty("isTrial").GetBoolean().Should().BeFalse();
        baths.GetProperty("warningLevel").GetString().Should().Be("None");

        var plans = sub.GetProperty("availablePlans").EnumerateArray().ToList();
        plans.Select(p => p.GetProperty("name").GetString()).Should().Contain(["Одна баня", "До 3 бань"]);
        plans.Should().NotContain(p => p.GetProperty("pricePerMonth").GetDecimal() == 0m, "пробный тариф в перечне к заявке не показывается");
        plans.Single(p => p.GetProperty("name").GetString() == "Одна баня").GetProperty("maxResources").GetInt32().Should().Be(1);
        plans.Single(p => p.GetProperty("name").GetString() == "До 3 бань").GetProperty("pricePerMonth").GetDecimal().Should().Be(500m);

        // заявка на тариф линейки; тариф другой линейки — 400
        var request = await client.PostJsonAsync("/api/billing/subscription/request", new { planId = BathsPlans.OneBathSeedId, line = "Baths" });
        request.StatusCode.Should().Be(HttpStatusCode.OK, await request.Content.ReadAsStringAsync());
        var wrongLine = await client.PostJsonAsync("/api/billing/subscription/request", new { planId = StaysPlans.OneHouseSeedId, line = "Baths" });
        wrongLine.StatusCode.Should().Be(HttpStatusCode.BadRequest, await wrongLine.Content.ReadAsStringAsync());
        // заявка линейки «Бани» ждёт — заявку другой линейки отклоняют
        var otherLine = await client.PostJsonAsync("/api/billing/subscription/request", new { planId = StaysPlans.OneHouseSeedId, line = "Stays" });
        otherLine.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.DeleteAsync("/api/billing/subscription/request")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact, TestCase("CY42-25")]
    public async Task BillingSubscription_Baths_TrialAndExpiredBannersFollowTheCabinetCard()
    {
        var b = await NewBathAsync();
        (await ActivateTrialAsync(b.Token, TermsVersion)).Outcome.Granted.Should().BeTrue();
        var client = AuthedClient(b.Token);

        var trial = (await J(await client.GetAsync("/api/billing/subscription?line=Baths"))).GetProperty("baths");
        trial.GetProperty("isTrial").GetBoolean().Should().BeTrue();
        trial.GetProperty("trialEndsAtUtc").GetDateTime().Should().BeCloseTo(DateTime.UtcNow.AddDays(14), TimeSpan.FromMinutes(5));
        trial.GetProperty("warningLevel").GetString().Should().Be("None");

        await SetBathsPlanAsync(b.Id, BathsPlans.TrialSeedId, paidUntil: DateTime.UtcNow.AddHours(20));
        (await J(await client.GetAsync("/api/billing/subscription?line=Baths"))).GetProperty("baths").GetProperty("warningLevel").GetString().Should().Be("TrialEnding1d");
        await SetBathsPlanAsync(b.Id, BathsPlans.TrialSeedId, paidUntil: DateTime.UtcNow.AddDays(2.5));
        var three = (await J(await client.GetAsync("/api/billing/subscription?line=Baths"))).GetProperty("baths");
        three.GetProperty("warningLevel").GetString().Should().Be("TrialEnding3d");
        await SetBathsPlanAsync(b.Id, BathsPlans.TrialSeedId, paidUntil: DateTime.UtcNow.AddMinutes(-5));
        var expired = (await J(await client.GetAsync("/api/billing/subscription?line=Baths"))).GetProperty("baths");
        expired.GetProperty("warningLevel").GetString().Should().Be("Expired");
        expired.GetProperty("text").GetString().Should().Be("Пробный период закончился. Гости не могут бронировать, пока вы не выберете тариф");
        (await CardAsync(b)).Plan.WarningLevel.Should().Be("Expired", "карточка кабинета и страница подписки говорят одно и то же");
    }

    // ── CY42-26: админка, назначение линейки ─────────────────────────────────────

    [Fact, TestCase("CY42-26")]
    public async Task Admin_AssignsBathsPlan_OpensTheGate_AndCardHasBathsBlock()
    {
        var admin = await AdminAsync();
        var b = await NewBathAsync();
        var accountId = await AccountIdAsync(b.Id);
        var draft = await DraftResourceAsync(b);
        (await PublishAsync(b, draft)).StatusCode.Should().Be((HttpStatusCode)402);

        var assign = await admin.PutJsonAsync($"/api/admin/billing-accounts/{accountId}/subscription", AssignBody(BathsPlans.OneBathSeedId));
        assign.StatusCode.Should().Be(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync());
        await PublishOkAsync(b, draft);
        (await CardAsync(b)).Plan.PlanName.Should().Be("Одна баня");

        var card = await J(await admin.GetAsync($"/api/admin/billing-accounts/{accountId}"));
        var bathsSub = card.GetProperty("bathsSubscription");
        bathsSub.GetProperty("planId").GetGuid().Should().Be(BathsPlans.OneBathSeedId);
        bathsSub.GetProperty("planName").GetString().Should().Be("Одна баня");
        bathsSub.GetProperty("isActive").GetBoolean().Should().BeTrue();
        bathsSub.GetProperty("resourcesPublished").GetInt32().Should().Be(1);
        bathsSub.GetProperty("maxResources").GetInt32().Should().Be(1);
        (await WithDbAsync(db => db.StaysSubscriptions.AnyAsync(s => s.BillingAccountId == accountId))).Should().BeFalse("назначение линейки «Бани» пишет только BathsSubscriptions");
    }

    [Fact, TestCase("CY42-26")]
    public async Task Admin_Assign_OverResourceLimit_Is409_UntilConfirmLimitOverflow()
    {
        var admin = await AdminAsync();
        var b = await NewBathAsync();
        var accountId = await AccountIdAsync(b.Id);
        await SetBathsPlanAsync(b.Id, BathsPlans.UnlimitedSeedId);
        await PublishOkAsync(b, await DraftResourceAsync(b, "Баня 1"));
        await PublishOkAsync(b, await DraftResourceAsync(b, "Баня 2"));

        var refused = await admin.PutJsonAsync($"/api/admin/billing-accounts/{accountId}/subscription", AssignBody(BathsPlans.OneBathSeedId));
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var text = await refused.Content.ReadAsStringAsync();
        text.Should().Contain("На новом тарифе доступно 1").And.Contain("опубликовано 2").And.Contain("Подтвердите превышение лимита");
        (await WithDbAsync(db => db.BathsSubscriptions.AsNoTracking().Where(s => s.BillingAccountId == accountId).Select(s => s.PlanConfigId).SingleAsync()))
            .Should().Be(BathsPlans.UnlimitedSeedId, "отказ ничего не записал");

        var confirmed = await admin.PutJsonAsync($"/api/admin/billing-accounts/{accountId}/subscription", AssignBody(BathsPlans.OneBathSeedId, confirm: true));
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK, await confirmed.Content.ReadAsStringAsync());
        var card = await CardAsync(b);
        card.Gate.ReasonCode.Should().Be("OverResourceLimit");
        card.Plan.ResourcesPublished.Should().Be(2);

        // безлимитный тариф превышения не вызывает
        (await admin.PutJsonAsync($"/api/admin/billing-accounts/{accountId}/subscription", AssignBody(BathsPlans.UnlimitedSeedId))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("CY42-26")]
    public async Task Admin_Assign_TrialPlanCannotBeAssigned_AndPlanOfAnotherLineIs400()
    {
        var admin = await AdminAsync();
        var b = await NewBathAsync();
        var accountId = await AccountIdAsync(b.Id);

        var trial = await admin.PutJsonAsync($"/api/admin/billing-accounts/{accountId}/subscription", AssignBody(BathsPlans.TrialSeedId));
        trial.StatusCode.Should().Be(HttpStatusCode.Conflict, await trial.Content.ReadAsStringAsync());
        (await J(trial)).GetProperty("code").GetString().Should().Be("TrialPlanNotAssignableHere");

        var staysPlan = await admin.PutJsonAsync($"/api/admin/billing-accounts/{accountId}/subscription", AssignBody(StaysPlans.OneHouseSeedId));
        staysPlan.StatusCode.Should().Be(HttpStatusCode.BadRequest, await staysPlan.Content.ReadAsStringAsync());
        var bathsPlanOnStaysLine = await admin.PutJsonAsync($"/api/admin/billing-accounts/{accountId}/subscription", AssignBody(BathsPlans.OneBathSeedId, line: "Stays"));
        bathsPlanOnStaysLine.StatusCode.Should().Be(HttpStatusCode.BadRequest, await bathsPlanOnStaysLine.Content.ReadAsStringAsync());
        (await WithDbAsync(db => db.BathsSubscriptions.AnyAsync(s => s.BillingAccountId == accountId))).Should().BeFalse();
    }

    // ── CY42-27: админка, тарифы линейки ─────────────────────────────────────────

    private static object PlanBody(string name, string line, int? maxResources, bool isActive = true, bool isPublic = false) => new
    {
        name, description = "x", highlights = Array.Empty<string>(), pricePerMonth = 300m, line, maxResources, maxEmployees = (int?)null, maxCompanies = (int?)null,
        allowOnlineBooking = false, allowMailing = false, allowAnalytics = false, allowPublicListing = false, allowOnlinePayment = false, photoQuotaMb = 0,
        isPublic, isActive, sortOrder = 50
    };

    [Fact, TestCase("CY42-27")]
    public async Task AdminPlans_MaxResourcesOnlyForBaths_AndRangeIsChecked()
    {
        var admin = await AdminAsync();
        var created = await admin.PostJsonAsync("/api/admin/plans", PlanBody(Unique("Бани-тариф "), "Baths", 5));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var plan = await J(created);
        plan.GetProperty("line").GetString().Should().Be("Baths");
        plan.GetProperty("maxResources").GetInt32().Should().Be(5);
        var id = plan.GetProperty("id").GetGuid();

        // обновление: лимит меняется без деплоя
        var update = await admin.PutJsonAsync($"/api/admin/plans/{id}", PlanBody(plan.GetProperty("name").GetString()!, "Baths", 8));
        update.StatusCode.Should().Be(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        (await J(update)).GetProperty("maxResources").GetInt32().Should().Be(8);

        const string lineText = "Лимит ресурсов задаётся только тарифам линейки «Бани»";
        foreach (var line in new[] { "Services", "Orders", "Stays" })
        {
            var bad = await admin.PostJsonAsync("/api/admin/plans", PlanBody(Unique("Не бани "), line, 3));
            bad.StatusCode.Should().Be(HttpStatusCode.BadRequest, line + ": " + await bad.Content.ReadAsStringAsync());
            (await bad.Content.ReadAsStringAsync()).Should().Contain(lineText);
        }
        var zero = await admin.PostJsonAsync("/api/admin/plans", PlanBody(Unique("Бани 0 "), "Baths", 0));
        zero.StatusCode.Should().Be(HttpStatusCode.BadRequest, await zero.Content.ReadAsStringAsync());
        // maxHouses у линейки «Бани» тоже нельзя
        var withHouses = await admin.PostJsonAsync("/api/admin/plans", new
        {
            name = Unique("Бани дома "), description = "x", highlights = Array.Empty<string>(), pricePerMonth = 300m, line = "Baths", maxHouses = 2, maxResources = (int?)null,
            allowOnlineBooking = false, allowMailing = false, allowAnalytics = false, allowPublicListing = false, allowOnlinePayment = false, photoQuotaMb = 0,
            isPublic = false, isActive = true, sortOrder = 51
        });
        withHouses.StatusCode.Should().Be(HttpStatusCode.BadRequest, await withHouses.Content.ReadAsStringAsync());

        // появившийся тариф доступен суперадмину в списке с линейкой
        var plans = await J(await admin.GetAsync("/api/admin/plans"));
        plans.GetProperty("plans").EnumerateArray().Should().Contain(p => p.GetProperty("id").GetGuid() == id && p.GetProperty("line").GetString() == "Baths");
    }

    [Fact, TestCase("CY42-27")]
    public async Task AdminPlans_BathsSystemTrial_CannotBeDeletedOrSwitchedOff()
    {
        var admin = await AdminAsync();
        var plans = await J(await admin.GetAsync("/api/admin/plans"));
        var trial = plans.GetProperty("plans").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == BathsPlans.TrialSeedId);
        trial.GetProperty("line").GetString().Should().Be("Baths");
        trial.GetProperty("pricePerMonth").GetDecimal().Should().Be(0m);
        trial.GetProperty("isPublic").GetBoolean().Should().BeFalse();

        var off = await admin.PutJsonAsync($"/api/admin/plans/{BathsPlans.TrialSeedId}", PlanBody(trial.GetProperty("name").GetString()!, "Baths", null, isActive: false));
        off.StatusCode.Should().Be(HttpStatusCode.Conflict, await off.Content.ReadAsStringAsync());
        (await off.Content.ReadAsStringAsync()).Should().Be("Пробный тариф линейки «Бани» нельзя удалить или выключить.");
        (await admin.DeleteAsync($"/api/admin/plans/{BathsPlans.TrialSeedId}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await WithDbAsync(db => db.SubscriptionPlanConfigs.AsNoTracking().Where(p => p.Id == BathsPlans.TrialSeedId).Select(p => p.IsActive).SingleAsync())).Should().BeTrue();
    }

    [Fact, TestCase("CY42-27")]
    public async Task AdminAccounts_ListFilter_KindBaths_ShowsTheCompany()
    {
        var admin = await AdminAsync();
        var b = await NewBathAsync();
        var r = await admin.GetAsync($"/api/admin/companies?kind=Baths&search={Uri.EscapeDataString(b.Company.Name)}");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        (await r.Content.ReadAsStringAsync()).Should().Contain(b.Id.ToString());
    }

    // ── CY42-28: /api/pricing без «Бань» ─────────────────────────────────────────

    [Fact, TestCase("CY42-28")]
    public async Task PublicPricing_NeverShowsTheBathsLine_EvenForAPublicBathsPlan()
    {
        var admin = await AdminAsync();
        var name = Unique("Бани публичный ");
        var created = await admin.PostJsonAsync("/api/admin/plans", PlanBody(name, "Baths", 4, isPublic: true));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        // витрина цен включается переключателем платформы (в БД класса — прямо строкой настройки, как в тестах цикла 15)
        await WithDbAsync(async db =>
        {
            var row = await db.PlatformSettings.FindAsync("pricing.public-enabled");
            if (row is null) db.PlatformSettings.Add(new PlatformSetting { Key = "pricing.public-enabled", Value = "true", UpdatedAt = DateTime.UtcNow });
            else row.Value = "true";
            await db.SaveChangesAsync();
        });

        var pricing = await AnonymousClient().GetAsync("/api/pricing");
        pricing.StatusCode.Should().Be(HttpStatusCode.OK, await pricing.Content.ReadAsStringAsync());
        var body = await pricing.Content.ReadAsStringAsync();
        var preview = await admin.GetAsync("/api/admin/pricing/preview");
        if (preview.IsSuccessStatusCode)
            (await preview.Content.ReadAsStringAsync()).Should().NotContain(name).And.NotContain("Одна баня", "предпросмотр строится тем же построителем, что и публичная витрина");
        body.Should().NotContain(name).And.NotContain("Одна баня").And.NotContain("До 3 бань").And.NotContain("Пробный период «Бани»").And.NotContain(BathsPlans.OneBathSeedId.ToString());
        body.Should().NotContain("maxResources");
        foreach (var path in new[] { "/api/pricing/orders", "/api/pricing/stays" })
        {
            var other = await AnonymousClient().GetAsync(path);
            if (other.IsSuccessStatusCode) (await other.Content.ReadAsStringAsync()).Should().NotContain("Одна баня").And.NotContain(name);
        }
    }
}
