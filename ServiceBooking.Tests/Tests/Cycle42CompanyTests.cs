using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Baths;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Цикл 42, «Бани»: компания (BE-42-1) — создание, адрес, карточка с гейтом и чек-листом, настройки, роли, закрытое расписание банщика, ревизия.
/// По API_CONTRACT_CYCLE42.md §42.27, §42.28, §42.33 и contracts/cycle42/openapi.yaml. Ресурсы, тарифы и триал «Бань» — другие задачи: подписка и ресурсы
/// здесь пишутся прямо в БД как подготовка данных.
/// </summary>
public class Cycle42CompanyTests(TestDatabaseFixture fixture) : Cycle37TestBase(fixture)
{
    private sealed record BathsCtx(AuthResponseDto Owner, string OwnerToken, BathsCompanyManageDto Company)
    {
        public Guid Id => Company.Id;
        public string Slug => Company.Slug;
    }

    private async Task<BathsCompanyCreatedDto> CreateAsync(string token, Action<Dictionary<string, object?>>? tweak = null)
    {
        var r = await PostCreateAsync(token, tweak);
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<BathsCompanyCreatedDto>())!;
    }

    private async Task<HttpResponseMessage> PostCreateAsync(string token, Action<Dictionary<string, object?>>? tweak = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = Unique("Баня "), ["slug"] = Unique("bn-").ToLowerInvariant(), ["cityId"] = await AnyCityIdAsync(), ["address"] = "Шерегеш, ул. Лесная, 5",
            ["phone"] = "+79001112233", ["description"] = null, ["ownerTermsVersion"] = CurrentOwnerTermsDto().Version, ["trialTermsVersion"] = null
        };
        tweak?.Invoke(body);
        return await AuthedClient(token).PostJsonAsync("/api/baths/companies", body);
    }

    private async Task<BathsCtx> NewCompanyAsync(Action<Dictionary<string, object?>>? tweak = null)
    {
        var owner = await RegisterAsync();
        var created = await CreateAsync(owner.Token, tweak);
        return new BathsCtx(owner, created.Token, created.Company);
    }

    private async Task<BathsCompanyManageDto> GetAsync(Guid id, string token)
    {
        var r = await AuthedClient(token).GetAsync($"/api/baths/companies/{id}");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<BathsCompanyManageDto>())!;
    }

    private Task GiveBathsPlanAsync(Guid companyId, Guid planId, DateTime? paidUntil = null) => WithDbAsync(async db =>
    {
        var accountId = await db.Companies.Where(c => c.Id == companyId).Select(c => c.BillingAccountId).FirstAsync();
        var sub = await db.BathsSubscriptions.FirstOrDefaultAsync(s => s.BillingAccountId == accountId);
        if (sub is null) db.BathsSubscriptions.Add(sub = new BathsSubscription { Id = Guid.NewGuid(), BillingAccountId = accountId!.Value });
        sub.PlanConfigId = planId;
        sub.IsActive = true;
        sub.PaidUntil = paidUntil ?? DateTime.UtcNow.AddYears(1);
        await db.SaveChangesAsync();
    });

    private Task<Guid> AddPublishedResourceAsync(Guid companyId, string name, int? prepay = null) => WithDbAsync(async db =>
    {
        var s = new StayService { Id = Guid.NewGuid(), CompanyId = companyId, Name = name, Slug = Unique("r-").ToLowerInvariant(), IsPublished = true, StandalonePrepayPercent = prepay, Capacity = 6, AvailableForHouseBookings = false };
        db.StayServices.Add(s);
        await db.SaveChangesAsync();
        return s.Id;
    });

    private async Task<(AuthResponseDto User, string Token)> AddStaffAsync(BathsCtx c, string position)
    {
        var user = await RegisterAsync();
        var r = await AuthedClient(c.OwnerToken).PostJsonAsync($"/api/Companies/{c.Id}/members",
            new { phone = user.Phone, firstName = user.FirstName, lastName = user.LastName, role = "Master", bio = (string?)null, email = (string?)null, position });
        r.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created], await r.Content.ReadAsStringAsync());
        return (user, (await LoginAsync(user.Phone, "Password123!")).Token);
    }

    // ── CY42-10: создание ────────────────────────────────────────────────────────

    [Fact, TestCase("CY42-10")]
    public async Task Create_MakesBathsCompany_WithSettingsOfTheVertical_AndFreshToken()
    {
        var owner = await RegisterAsync();
        var cityId = await AnyCityIdAsync();
        var created = await CreateAsync(owner.Token, b => { b["description"] = "Банный комплекс"; b["slug"] = Unique("bn-").ToLowerInvariant(); });

        created.Token.Should().NotBeNullOrEmpty();
        created.Trial.Should().BeNull();
        var c = created.Company;
        c.MyRole.Should().Be(StaysMyRole.Owner);
        c.CityId.Should().Be(cityId);
        c.CityName.Should().NotBeNullOrEmpty();
        c.TimeZoneId.Should().NotBeNullOrEmpty();
        c.IsActive.Should().BeTrue();
        c.ShowInCatalog.Should().BeTrue();
        c.PublicUrl.Should().Be($"https://bani.ezbook.ru/{c.Slug}");
        c.Settings!.SessionReminderEnabled.Should().BeTrue();
        c.Settings.SessionReminderHours.Should().Be(3);
        c.Settings.HousekeeperSeesGuestComment.Should().BeFalse();

        await WithDbAsync(async db =>
        {
            var company = await db.Companies.AsNoTracking().FirstAsync(x => x.Id == c.Id);
            company.Kind.Should().Be(CompanyKind.Baths);
            company.OwnerUserId.Should().Be(owner.UserId);
            company.ShowInPublicListing.Should().BeTrue();
            var settings = await db.StaysSettings.AsNoTracking().FirstAsync(x => x.CompanyId == c.Id);
            settings.AcceptServiceOrdersWithoutStay.Should().BeTrue();
            settings.ServiceReminderHours.Should().Be(3);
            settings.ArrivalReminderEnabled.Should().BeFalse();
            (await db.CompanyMembers.CountAsync(m => m.CompanyId == c.Id && m.UserId == owner.UserId && m.Role == UserRole.CompanyOwner)).Should().Be(1);
        });

        // новый токен действует на владельческих маршрутах
        (await AuthedClient(created.Token).GetAsync($"/api/baths/companies/{c.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("CY42-10")]
    public async Task Create_WithoutSlug_SuggestsOneFromTheName()
    {
        var owner = await RegisterAsync();
        var created = await CreateAsync(owner.Token, b => { b["name"] = "Баня у ручья"; b["slug"] = null; });
        created.Company.Slug.Should().StartWith("banya-u-ruchya");
    }

    // ── CY42-11: проверки создания ───────────────────────────────────────────────

    [Fact, TestCase("CY42-11")]
    public async Task Create_Validation_ReturnsPlainText400_InContractOrder()
    {
        var owner = await RegisterAsync();
        async Task<string> Expect400(Action<Dictionary<string, object?>> tweak, string text)
        {
            var r = await PostCreateAsync(owner.Token, tweak);
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest, text);
            var body = await r.Content.ReadAsStringAsync();
            body.Should().Contain(text);
            return body;
        }
        await Expect400(b => b["ownerTermsVersion"] = null, "Для создания компании нужно принять соглашение с владельцем.");
        await Expect400(b => b["name"] = "  ", "Укажите название");
        await Expect400(b => b["description"] = new string('я', 2001), "Описание — не длиннее 2000 символов");
        await Expect400(b => b["address"] = new string('я', 301), "Адрес — не длиннее 300 символов");
        await Expect400(b => b["phone"] = "123", "Введите номер телефона в формате +7 (900) 000-00-00");
        await Expect400(b => b["cityId"] = null, "Укажите город комплекса");
        await Expect400(b => b["cityId"] = 999999, "Город не найден");
        (await AuthedClient(owner.Token).GetAsync("/api/baths/companies/my")).Content.ReadJsonAsync<List<BathsCompanyListItemDto>>().Result.Should().BeEmpty("отказ не создаёт строк");
    }

    [Fact, TestCase("CY42-11")]
    public async Task Create_StaleOwnerTerms_Returns409Text()
    {
        var owner = await RegisterAsync();
        var r = await PostCreateAsync(owner.Token, b => b["ownerTermsVersion"] = "устарело-0");
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await r.Content.ReadAsStringAsync()).Should().Contain("перечитайте");
    }

    [Fact, TestCase("CY42-11")]
    public async Task Create_RequiresToken()
    {
        var r = await AnonymousClient().PostJsonAsync("/api/baths/companies", new { name = "x" });
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── CY42-12: адрес ───────────────────────────────────────────────────────────

    [Theory, TestCase("CY42-12")]
    [InlineData("AB", "SlugInvalid")]
    [InlineData("-bad-", "SlugInvalid")]
    [InlineData("primer-bani", "SlugInvalid")]
    [InlineData("cabinet", "SlugReserved")]
    [InlineData("bath", "SlugReserved")]
    [InlineData("sauna", "SlugReserved")]
    public async Task Create_BadSlug_Returns409Json(string slug, string code)
    {
        var owner = await RegisterAsync();
        var r = await PostCreateAsync(owner.Token, b => b["slug"] = slug);
        r.StatusCode.Should().Be(HttpStatusCode.Conflict, await r.Content.ReadAsStringAsync());
        (await J(r)).GetProperty("code").GetString().Should().Be(code);
    }

    [Fact, TestCase("CY42-12")]
    public async Task Create_SlugTakenByAnyCompany_Returns409_CaseInsensitively()
    {
        var existing = await CreateStaysCompanyAsync(); // общее пространство адресов платформы
        var owner = await RegisterAsync();
        var r = await PostCreateAsync(owner.Token, b => b["slug"] = existing.Slug.ToUpperInvariant());
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await J(r)).GetProperty("code").GetString().Should().Be("SlugTaken");
    }

    [Fact, TestCase("CY42-12")]
    public async Task SlugCheck_AlwaysAnswers200_WithReasonOrSuggestion()
    {
        var c = await NewCompanyAsync();
        var client = AuthedClient(c.OwnerToken);

        var free = await client.GetAsync($"/api/baths/slug-check?slug={Unique("fr-").ToLowerInvariant()}");
        free.StatusCode.Should().Be(HttpStatusCode.OK);
        (await free.Content.ReadJsonAsync<BathsSlugCheckDto>())!.Available.Should().BeTrue();

        var taken = (await (await client.GetAsync($"/api/baths/slug-check?slug={c.Slug}")).Content.ReadJsonAsync<BathsSlugCheckDto>())!;
        taken.Available.Should().BeFalse();
        taken.Conflict!.Code.Should().Be("SlugTaken");

        var own = (await (await client.GetAsync($"/api/baths/slug-check?slug={c.Slug}&companyId={c.Id}")).Content.ReadJsonAsync<BathsSlugCheckDto>())!;
        own.Available.Should().BeTrue("свой адрес не считается занятым");

        var reserved = (await (await client.GetAsync("/api/baths/slug-check?slug=login")).Content.ReadJsonAsync<BathsSlugCheckDto>())!;
        reserved.Conflict!.Code.Should().Be("SlugReserved");

        var suggested = (await (await client.GetAsync("/api/baths/slug-check?name=Сауна%20Север")).Content.ReadJsonAsync<BathsSlugCheckDto>())!;
        suggested.Available.Should().BeTrue();
        suggested.Suggested.Should().StartWith("sauna-sever");

        var none = await client.GetAsync("/api/baths/slug-check");
        none.StatusCode.Should().Be(HttpStatusCode.OK);
        (await none.Content.ReadJsonAsync<BathsSlugCheckDto>())!.Conflict!.Code.Should().Be("SlugInvalid");

        (await AnonymousClient().GetAsync("/api/baths/slug-check?slug=abc")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("CY42-12")]
    public async Task ChangeSlug_Works_ForOwner_AndRefusesTakenAndReserved()
    {
        var a = await NewCompanyAsync();
        var b = await NewCompanyAsync();
        var client = AuthedClient(a.OwnerToken);

        var taken = await client.PutJsonAsync($"/api/baths/companies/{a.Id}/slug", new SlugInput(b.Slug));
        taken.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await J(taken)).GetProperty("code").GetString().Should().Be("SlugTaken");
        var reserved = await client.PutJsonAsync($"/api/baths/companies/{a.Id}/slug", new SlugInput("cabinet"));
        (await J(reserved)).GetProperty("code").GetString().Should().Be("SlugReserved");

        var fresh = Unique("nw-").ToLowerInvariant();
        var ok = await client.PutJsonAsync($"/api/baths/companies/{a.Id}/slug", new SlugInput(fresh));
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var dto = (await ok.Content.ReadJsonAsync<BathsCompanyManageDto>())!;
        dto.Slug.Should().Be(fresh);
        dto.PublicUrl.Should().EndWith("/" + fresh);
    }

    // ── CY42-13: карточка, гейт, чек-лист ────────────────────────────────────────

    [Fact, TestCase("CY42-13")]
    public async Task Card_NewCompany_IsClosed_WithFixedChecklistOrder()
    {
        var c = await NewCompanyAsync();
        var card = await GetAsync(c.Id, c.OwnerToken);

        card.Gate.Accepting.Should().BeFalse();
        card.Gate.ReasonCode.Should().Be("NoPlan");
        card.Checklist.Select(i => i.Code).Should().Equal("ProfileFilled", "PaymentDetails", "ProviderInfo", "ResourcePublished", "Plan");
        card.Checklist.Single(i => i.Code == "ProfileFilled").Done.Should().BeTrue();
        card.Checklist.Single(i => i.Code == "PaymentDetails").Done.Should().BeTrue("ни у одного опубликованного ресурса нет предоплаты");
        card.Checklist.Single(i => i.Code == "ProviderInfo").Done.Should().BeFalse();
        card.Checklist.Single(i => i.Code == "ResourcePublished").Done.Should().BeFalse();
        card.Checklist.Single(i => i.Code == "Plan").Done.Should().BeFalse();
        card.Plan.WarningLevel.Should().Be("NoPlan");
        card.Plan.PlanName.Should().BeNull();
        card.Plan.ResourcesPublished.Should().Be(0);
        card.AwaitingPaymentCount.Should().Be(0);
        card.PaymentDetails.Should().NotBeNull();
    }

    [Fact, TestCase("CY42-13")]
    public async Task Card_Gate_OpensStepByStep_PlanThenProvider_AndPrepayNeedsRequisites()
    {
        var c = await NewCompanyAsync();
        var client = AuthedClient(c.OwnerToken);

        await GiveBathsPlanAsync(c.Id, BathsPlans.UnlimitedSeedId);
        var card = await GetAsync(c.Id, c.OwnerToken);
        card.Gate.ReasonCode.Should().Be("NoProviderInfo");
        card.Plan.PlanName.Should().NotBeNullOrEmpty();
        card.Plan.MaxResources.Should().BeNull();
        card.Plan.WarningLevel.Should().Be("None");

        var provider = await client.PutJsonAsync($"/api/baths/companies/{c.Id}/provider",
            new ProviderInput(StayProviderStatus.SelfEmployed, "Иванов Иван Иванович", ValidPersonInn, null, "г. Новокузнецк, ул. Мира, 1"));
        provider.StatusCode.Should().Be(HttpStatusCode.OK, await provider.Content.ReadAsStringAsync());
        card = (await provider.Content.ReadJsonAsync<BathsCompanyManageDto>())!;
        card.Gate.Accepting.Should().BeTrue();
        card.Provider!.Inn.Should().Be(ValidPersonInn);

        // ресурс с предоплатой без реквизитов закрывает приём; чек-лист называет ресурс
        await AddPublishedResourceAsync(c.Id, "Русская баня", prepay: 30);
        card = await GetAsync(c.Id, c.OwnerToken);
        card.Gate.ReasonCode.Should().Be("NoPaymentDetails");
        card.Checklist.Single(i => i.Code == "ResourcePublished").Done.Should().BeTrue();
        var pay = card.Checklist.Single(i => i.Code == "PaymentDetails");
        pay.Done.Should().BeFalse();
        pay.Text.Should().Contain("«Русская баня»");

        var details = await client.PutJsonAsync($"/api/baths/companies/{c.Id}/payment-details", new PaymentDetailsDto(PaymentDetailsText, "Бронь бани"));
        details.StatusCode.Should().Be(HttpStatusCode.OK, await details.Content.ReadAsStringAsync());
        card = (await details.Content.ReadJsonAsync<BathsCompanyManageDto>())!;
        card.Gate.Accepting.Should().BeTrue();
        card.PaymentDetails!.PaymentDetails.Should().Be(PaymentDetailsText);
    }

    [Fact, TestCase("CY42-13")]
    public async Task Card_OverResourceLimit_ClosesGate_WithServerText()
    {
        var c = await NewCompanyAsync();
        await GiveBathsPlanAsync(c.Id, BathsPlans.OneBathSeedId);
        await AuthedClient(c.OwnerToken).PutJsonAsync($"/api/baths/companies/{c.Id}/provider",
            new ProviderInput(StayProviderStatus.SelfEmployed, "Иванов Иван Иванович", ValidPersonInn, null, "г. Новокузнецк, ул. Мира, 1"));
        await AddPublishedResourceAsync(c.Id, "Баня 1");
        (await GetAsync(c.Id, c.OwnerToken)).Gate.Accepting.Should().BeTrue();

        await AddPublishedResourceAsync(c.Id, "Баня 2");
        var card = await GetAsync(c.Id, c.OwnerToken);
        card.Gate.ReasonCode.Should().Be("OverResourceLimit");
        card.Plan.MaxResources.Should().Be(1);
        card.Plan.ResourcesPublished.Should().Be(2);
        card.Plan.WarningLevel.Should().Be("OverLimit");
        card.Plan.Text.Should().Contain("Опубликовано 2 ресурса при лимите 1");
    }

    [Fact, TestCase("CY42-13")]
    public async Task Card_TrialEnding_And_ExpiredPlan_WarningLevels()
    {
        var c = await NewCompanyAsync();
        await GiveBathsPlanAsync(c.Id, BathsPlans.TrialSeedId, DateTime.UtcNow.AddDays(2));
        var card = await GetAsync(c.Id, c.OwnerToken);
        card.Plan.IsTrial.Should().BeTrue();
        card.Plan.WarningLevel.Should().Be("TrialEnding3d");

        await GiveBathsPlanAsync(c.Id, BathsPlans.TrialSeedId, DateTime.UtcNow.AddHours(5));
        (await GetAsync(c.Id, c.OwnerToken)).Plan.WarningLevel.Should().Be("TrialEnding1d");

        await GiveBathsPlanAsync(c.Id, BathsPlans.TrialSeedId, DateTime.UtcNow.AddDays(-1));
        card = await GetAsync(c.Id, c.OwnerToken);
        card.Plan.WarningLevel.Should().Be("Expired");
        card.Gate.ReasonCode.Should().Be("NoPlan");
    }

    // ── CY42-14: настройки ───────────────────────────────────────────────────────

    [Fact, TestCase("CY42-14")]
    public async Task Settings_FullReplacement_Saves_AndShowInCatalogWritesTheCompany()
    {
        var c = await NewCompanyAsync();
        var client = AuthedClient(c.OwnerToken);
        var r = await client.PutJsonAsync($"/api/baths/companies/{c.Id}/settings", new BathsSettingsDto(90, 45, true, false, true, 5));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var card = (await r.Content.ReadJsonAsync<BathsCompanyManageDto>())!;
        card.Settings.Should().Be(new BathsSettingsDto(90, 45, true, false, true, 5));
        card.ShowInCatalog.Should().BeFalse();
        await WithDbAsync(async db =>
        {
            (await db.Companies.AsNoTracking().FirstAsync(x => x.Id == c.Id)).ShowInPublicListing.Should().BeFalse();
            var s = await db.StaysSettings.AsNoTracking().FirstAsync(x => x.CompanyId == c.Id);
            (s.HorizonDays, s.HoldMinutes, s.ServiceReminderHours, s.HousekeeperSeesGuestComment).Should().Be((90, 45, 5, true));
            s.AcceptServiceOrdersWithoutStay.Should().BeTrue("настройки бани этот флаг не трогают");
        });

        // напоминание выключено: в БД NULL, наружу — 3 по умолчанию
        var off = (await (await client.PutJsonAsync($"/api/baths/companies/{c.Id}/settings", new BathsSettingsDto(90, 45, true, true, false, 3)))
            .Content.ReadJsonAsync<BathsCompanyManageDto>())!;
        off.Settings!.SessionReminderEnabled.Should().BeFalse();
        await WithDbAsync(async db => (await db.StaysSettings.AsNoTracking().FirstAsync(x => x.CompanyId == c.Id)).ServiceReminderHours.Should().BeNull());
    }

    [Theory, TestCase("CY42-14")]
    [InlineData(29, 30, 3, "Горизонт бронирования — от 30 до 730 дней")]
    [InlineData(731, 30, 3, "Горизонт бронирования — от 30 до 730 дней")]
    [InlineData(365, 9, 3, "Время на оплату — от 10 до 180 минут")]
    [InlineData(365, 181, 3, "Время на оплату — от 10 до 180 минут")]
    [InlineData(365, 30, 0, "Напоминание — за 1…24 часа до начала")]
    [InlineData(365, 30, 25, "Напоминание — за 1…24 часа до начала")]
    public async Task Settings_OutOfRange_Returns400Text(int horizon, int hold, int hours, string text)
    {
        var c = await NewCompanyAsync();
        var r = await AuthedClient(c.OwnerToken).PutJsonAsync($"/api/baths/companies/{c.Id}/settings", new BathsSettingsDto(horizon, hold, false, true, true, hours));
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain(text);
    }

    [Fact, TestCase("CY42-14")]
    public async Task NotificationSettings_Work_And_MessengerWithoutChannelIs409()
    {
        var c = await NewCompanyAsync();
        var client = AuthedClient(c.OwnerToken);
        var get = await client.GetAsync($"/api/baths/companies/{c.Id}/notification-settings");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        (await J(get)).GetProperty("messengerAvailable").GetBoolean().Should().BeFalse();

        var refuse = await client.PutJsonAsync($"/api/baths/companies/{c.Id}/notification-settings",
            new StaysNotificationSettingsInput(true, true, true, true, null, null));
        refuse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await J(refuse)).GetProperty("code").GetString().Should().Be("MessengerUnavailable");

        var ok = await client.PutJsonAsync($"/api/baths/companies/{c.Id}/notification-settings",
            new StaysNotificationSettingsInput(false, false, true, false, null, null));
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        (await J(ok)).GetProperty("guestWebPushEnabled").GetBoolean().Should().BeTrue();
    }

    [Fact, TestCase("CY42-14")]
    public async Task Qr_ReturnsPng()
    {
        var c = await NewCompanyAsync();
        var r = await AuthedClient(c.OwnerToken).GetAsync($"/api/baths/companies/{c.Id}/qr");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await r.Content.ReadAsByteArrayAsync()).Take(4).Should().Equal(0x89, 0x50, 0x4E, 0x47);
    }

    // ── CY42-15: список ──────────────────────────────────────────────────────────

    [Fact, TestCase("CY42-15")]
    public async Task My_ListsOnlyBathsCompaniesOfTheUser_WithRoleAndAwaitingCount()
    {
        var c = await NewCompanyAsync();
        var second = await CreateAsync(c.Owner.Token, b => b["name"] = "Яблоня");
        var stays = await CreateStaysCompanyForAsync(c.Owner.Token, Unique("dom-").ToLowerInvariant());
        var stranger = await NewCompanyAsync();

        var list = (await (await AuthedClient(c.OwnerToken).GetAsync("/api/baths/companies/my")).Content.ReadJsonAsync<List<BathsCompanyListItemDto>>())!;
        list.Select(i => i.Id).Should().BeEquivalentTo([c.Id, second.Company.Id]);
        list.Should().NotContain(i => i.Id == stays.Company.Id).And.NotContain(i => i.Id == stranger.Id);
        list.Should().OnlyContain(i => i.MyRole == StaysMyRole.Owner && !i.AcceptingBookings);
        list.Select(i => i.Name).Should().BeInAscendingOrder(StringComparer.Ordinal);
        list.First().PublicUrl.Should().StartWith("https://bani.ezbook.ru/");

        (await AnonymousClient().GetAsync("/api/baths/companies/my")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── CY42-16: роли и изоляция вида ────────────────────────────────────────────

    [Fact, TestCase("CY42-16")]
    public async Task Roles_HousekeeperSeesOnlyCardWithoutSettings_ManagerNoOwnerRoutes_StrangerIs404()
    {
        var c = await NewCompanyAsync();
        var manager = await AddStaffAsync(c, "Manager");
        var keeper = await AddStaffAsync(c, "Housekeeper");
        var stranger = await RegisterAsync();

        var keeperCard = await GetAsync(c.Id, keeper.Token);
        keeperCard.MyRole.Should().Be(StaysMyRole.Housekeeper);
        keeperCard.MyPermissions.Should().Equal(StaysPermission.ViewSchedule);
        keeperCard.Settings.Should().BeNull();
        keeperCard.PaymentDetails.Should().BeNull();
        keeperCard.Provider.Should().BeNull();
        keeperCard.AwaitingPaymentCount.Should().BeNull();

        var managerCard = await GetAsync(c.Id, manager.Token);
        managerCard.MyRole.Should().Be(StaysMyRole.Manager);
        managerCard.Settings.Should().NotBeNull();
        managerCard.PaymentDetails.Should().BeNull("реквизиты — только с ManageCompany");

        var ownerOnly = new (HttpMethod M, string Url, object? Body)[]
        {
            (HttpMethod.Put, $"/api/baths/companies/{c.Id}/settings", new BathsSettingsDto(90, 45, false, true, true, 3)),
            (HttpMethod.Put, $"/api/baths/companies/{c.Id}/payment-details", new PaymentDetailsDto("x", "y")),
            (HttpMethod.Put, $"/api/baths/companies/{c.Id}/slug", new SlugInput("some-slug")),
            (HttpMethod.Put, $"/api/baths/companies/{c.Id}/provider", new ProviderInput(null, null, null, null, null)),
        };
        foreach (var (m, url, body) in ownerOnly)
            foreach (var staff in new[] { manager.Token, keeper.Token })
            {
                var r = await AuthedClient(staff).SendAsync(new HttpRequestMessage(m, url) { Content = System.Net.Http.Json.JsonContent.Create(body) });
                r.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{m} {url}");
                (await r.Content.ReadAsStringAsync()).Should().BeEmpty();
            }
        (await AuthedClient(keeper.Token).GetAsync($"/api/baths/companies/{c.Id}/qr")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AuthedClient(keeper.Token).GetAsync($"/api/baths/companies/{c.Id}/notification-settings")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        foreach (var url in new[] { "", "/schedule", "/revision", "/qr", "/notification-settings" })
        {
            var r = await AuthedClient(stranger.Token).GetAsync($"/api/baths/companies/{c.Id}{url}");
            r.StatusCode.Should().Be(HttpStatusCode.NotFound, url);
            (await r.Content.ReadAsStringAsync()).Should().BeEmpty();
        }
    }

    [Fact, TestCase("CY42-16")]
    public async Task KindIsolation_BathsRoutesDoNotSeeStaysOrSalon_AndStaysRoutesDoNotSeeBaths()
    {
        var baths = await NewCompanyAsync();
        var stays = await CreateStaysCompanyAsync();
        var salonOwner = await RegisterAsync();
        var salon = await CreateCompanyAsync(salonOwner.Token);

        foreach (var (token, id) in new[] { (stays.OwnerToken, stays.Id), (salonOwner.Token, salon.Id) })
            foreach (var url in new[] { "", "/schedule", "/revision", "/qr", "/notification-settings" })
            {
                var r = await AuthedClient(token).GetAsync($"/api/baths/companies/{id}{url}");
                r.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{id}{url}");
                (await r.Content.ReadAsStringAsync()).Should().BeEmpty();
            }

        foreach (var url in new[] { "", "/qr", "/notification-settings", "/arrival-reminder" })
            (await AuthedClient(baths.OwnerToken).GetAsync($"/api/stays/companies/{baths.Id}{url}")).StatusCode.Should().Be(HttpStatusCode.NotFound, url);
        (await AuthedClient(baths.OwnerToken).GetAsync("/api/stays/companies/my")).Content.ReadJsonAsync<List<StaysCompanyListItemDto>>().Result.Should().NotContain(i => i.Id == baths.Id);
    }

    // ── CY42-17: ревизия ─────────────────────────────────────────────────────────

    [Fact, TestCase("CY42-17")]
    public async Task Revision_ReturnsBookingsRevision_ForOwnerAndHousekeeper()
    {
        var c = await NewCompanyAsync();
        var keeper = await AddStaffAsync(c, "Housekeeper");
        async Task<long> Rev(string token)
        {
            var r = await AuthedClient(token).GetAsync($"/api/baths/companies/{c.Id}/revision");
            r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
            return (await r.Content.ReadJsonAsync<BathsRevisionDto>())!.Revision;
        }
        var before = await Rev(c.OwnerToken);
        await WithDbAsync(db => db.StaysSettings.Where(s => s.CompanyId == c.Id).ExecuteUpdateAsync(u => u.SetProperty(s => s.BookingsRevision, s => s.BookingsRevision + 1)));
        (await Rev(c.OwnerToken)).Should().Be(before + 1);
        (await Rev(keeper.Token)).Should().Be(before + 1);
    }

    // ── CY42-70: расписание банщика ──────────────────────────────────────────────

    private static readonly string[] ScheduleSessionProperties =
        ["sessionId", "serviceName", "timeLabel", "preparedUntilLabel", "guestName", "guestsCount", "items", "comment", "paymentUnconfirmed"];

    private async Task<Guid> AddSessionAsync(Guid companyId, Guid serviceId, DateOnly businessDate, int startMinute, int hours, StayBookingStatus status,
        string guest = "Анна Гость", string? comment = "Не греть выше 90", int? guestsCount = 4)
    {
        return await WithDbAsync(async db =>
        {
            var tz = await db.Companies.Where(c => c.Id == companyId).Select(c => c.TimeZoneId).FirstAsync();
            var start = BusinessClock.ToUtc(tz, businessDate, startMinute);
            var order = new StayServiceOrder
            {
                Id = Guid.NewGuid(), CompanyId = companyId, ServiceId = serviceId, PublicToken = Guid.NewGuid().ToString("N"), IdempotencyKey = Guid.NewGuid(),
                Status = status, GuestName = guest, GuestPhone = "+79005554433", Comment = comment, GuestsCount = guestsCount, TotalRub = 5000, ServiceAmountRub = 4400, ItemsAmountRub = 600, PrepayRub = 1500, DueOnSiteRub = 3500,
                TimeZoneIdSnapshot = tz, PaymentDetailsSnapshot = "СЕКРЕТНЫЕ РЕКВИЗИТЫ", GuestKind = StayActorKind.Guest
            };
            var session = new StayServiceSession
            {
                Id = Guid.NewGuid(), CompanyId = companyId, ServiceId = serviceId, StayServiceOrderId = order.Id, BusinessDate = businessDate, StartMinute = startMinute,
                Hours = hours, StartUtc = start, EndUtc = start.AddHours(hours), BufferMinutesSnapshot = 30, OccupiedUntilUtc = start.AddHours(hours).AddMinutes(30),
                ServiceNameSnapshot = "Русская баня", ItemsJson = """[{"itemId":"00000000-0000-0000-0000-000000000001","name":"Веник берёзовый","unitPriceRub":300,"quantity":2,"amountRub":600}]""",
                ServiceAmountRub = 4400, ItemsAmountRub = 600, TotalRub = 5000, AddedByKind = StayActorKind.Guest
            };
            db.StayServiceOrders.Add(order);
            db.StayServiceSessions.Add(session);
            await db.SaveChangesAsync();
            return session.Id;
        });
    }

    [Fact, TestCase("CY42-70")]
    public async Task Schedule_ClosedShape_NoPhoneNoMoneyNoRequisites_HeldIsHidden_MidnightUsesCalendarDates()
    {
        var c = await NewCompanyAsync();
        var keeper = await AddStaffAsync(c, "Housekeeper");
        var svc = await AddPublishedResourceAsync(c.Id, "Русская баня");
        var day = InDays(5);
        var confirmed = await AddSessionAsync(c.Id, svc, day, 1380, 3, StayBookingStatus.Confirmed);
        var awaiting = await AddSessionAsync(c.Id, svc, day, 600, 2, StayBookingStatus.AwaitingPaymentCheck, guest: "Борис", comment: null, guestsCount: null);
        await AddSessionAsync(c.Id, svc, day, 780, 2, StayBookingStatus.Held);
        await AddSessionAsync(c.Id, svc, day, 1020, 2, StayBookingStatus.CancelledByGuest);
        await AddSessionAsync(c.Id, svc, day.AddDays(40), 600, 2, StayBookingStatus.Confirmed);

        var r = await AuthedClient(keeper.Token).GetAsync($"/api/baths/companies/{c.Id}/schedule?from={D(day)}&days=2");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var raw = await r.Content.ReadAsStringAsync();
        raw.Should().NotContain("79005554433").And.NotContain("СЕКРЕТНЫЕ").And.NotContain("5000").And.NotContain("1500").And.NotContainEquivalentOf("phone");
        var json = JsonDocument.Parse(raw).RootElement;
        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("today", "days");
        var days = json.GetProperty("days").EnumerateArray().ToList();
        days.Should().HaveCount(2);
        days[0].EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("date", "label", "sessions");
        days[0].GetProperty("date").GetString().Should().Be(D(day));
        days[1].GetProperty("sessions").GetArrayLength().Should().Be(0);

        var sessions = days[0].GetProperty("sessions").EnumerateArray().ToList();
        sessions.Should().HaveCount(2, "удержанная и отменённая брони не показываются");
        foreach (var s in sessions) s.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(ScheduleSessionProperties);

        // по времени начала: 10:00, потом 23:00 через полночь
        sessions[0].GetProperty("sessionId").GetGuid().Should().Be(awaiting);
        sessions[0].GetProperty("paymentUnconfirmed").GetBoolean().Should().BeTrue();
        sessions[0].GetProperty("guestsCount").ValueKind.Should().Be(JsonValueKind.Null);
        sessions[1].GetProperty("sessionId").GetGuid().Should().Be(confirmed);
        sessions[1].GetProperty("paymentUnconfirmed").GetBoolean().Should().BeFalse();
        sessions[1].GetProperty("guestsCount").GetInt32().Should().Be(4);
        sessions[1].GetProperty("serviceName").GetString().Should().Be("Русская баня");
        var label = sessions[1].GetProperty("timeLabel").GetString()!;
        label.Should().Contain("23:00").And.Contain("02:00").And.Contain("—");
        label.Split('—')[1].Should().MatchRegex(@"\d{1,2} \p{L}+", "после полуночи — календарная дата");
        sessions[1].GetProperty("preparedUntilLabel").GetString().Should().Contain("02:30");
        var items = sessions[1].GetProperty("items").EnumerateArray().Single();
        items.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("name", "quantity");
        items.GetProperty("name").GetString().Should().Be("Веник берёзовый");
        items.GetProperty("quantity").GetInt32().Should().Be(2);
    }

    [Fact, TestCase("CY42-71")]
    public async Task Schedule_Comment_HiddenFromHousekeeper_UntilOwnerAllows_AlwaysForOwnerAndManager()
    {
        var c = await NewCompanyAsync();
        var keeper = await AddStaffAsync(c, "Housekeeper");
        var manager = await AddStaffAsync(c, "Manager");
        var svc = await AddPublishedResourceAsync(c.Id, "Русская баня");
        var day = InDays(4);
        await AddSessionAsync(c.Id, svc, day, 600, 2, StayBookingStatus.Confirmed, comment: "Аллергия на веники");

        async Task<string?> CommentFor(string token)
        {
            var r = await AuthedClient(token).GetAsync($"/api/baths/companies/{c.Id}/schedule?from={D(day)}&days=1");
            r.StatusCode.Should().Be(HttpStatusCode.OK);
            var s = (await J(r)).GetProperty("days")[0].GetProperty("sessions")[0].GetProperty("comment");
            return s.ValueKind == JsonValueKind.Null ? null : s.GetString();
        }
        (await CommentFor(keeper.Token)).Should().BeNull();
        (await CommentFor(manager.Token)).Should().Be("Аллергия на веники");
        (await CommentFor(c.OwnerToken)).Should().Be("Аллергия на веники");

        var put = await AuthedClient(c.OwnerToken).PutJsonAsync($"/api/baths/companies/{c.Id}/settings", new BathsSettingsDto(365, 30, true, true, true, 3));
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CommentFor(keeper.Token)).Should().Be("Аллергия на веники");
    }

    [Theory, TestCase("CY42-72")]
    [InlineData("from=2026-13-40")]
    [InlineData("from=завтра")]
    [InlineData("days=0")]
    [InlineData("days=32")]
    public async Task Schedule_BadPeriod_Returns400Text(string query)
    {
        var c = await NewCompanyAsync();
        var r = await AuthedClient(c.OwnerToken).GetAsync($"/api/baths/companies/{c.Id}/schedule?{query}");
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Be("Неверный период");
    }

    [Fact, TestCase("CY42-72")]
    public async Task Schedule_DefaultsToSevenDaysFromToday_AndNeedsViewSchedule()
    {
        var c = await NewCompanyAsync();
        var r = await AuthedClient(c.OwnerToken).GetAsync($"/api/baths/companies/{c.Id}/schedule");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await J(r);
        var days = json.GetProperty("days");
        days.GetArrayLength().Should().Be(7);
        days[0].GetProperty("date").GetString().Should().Be(json.GetProperty("today").GetString());
        (await AnonymousClient().GetAsync($"/api/baths/companies/{c.Id}/schedule")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
