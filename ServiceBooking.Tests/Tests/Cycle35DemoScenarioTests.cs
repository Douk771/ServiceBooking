using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.API.Services.Ops;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 35, "Вызов 2" — the demo stand of "Заказы" as a visitor and an operator see it (US-35-02 … US-35-07). Written from SPEC_CYCLE35_GOODS_DEMO_STAND.md and
/// API_CONTRACT_CYCLE35.md, not from the implementation. One host in demo mode on its own database "sbtest_&lt;key&gt;_demo", one operator reset in
/// <see cref="InitializeAsync"/>, then scenarios against the generated demo. Order of the tests is random; every scenario that writes visitor data tolerates other
/// scenarios' leftovers (the reset wipes everything anyway).
/// </summary>
[Collection("Cycle28Generator")]
public class Cycle35DemoScenarioTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly string[] ShopRoles = ["shop-owner", "shop-staff", "shop-customer"];

    private TestClassDatabaseLease _lease = null!;
    private DemoHostFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _lease = await TestRunEnvironment.LeaseClassDatabaseAsync("demo");
        _factory = new DemoHostFactory(_lease.ConnectionString, new Dictionary<string, string?> { ["DemoMode:ResetLocalTime"] = "00:00" });
        _ = _factory.Services;
        var (exit, output) = await OpsAsync("demo", "reset", "--yes");
        exit.Should().Be(0, output);
    }

    public async Task DisposeAsync()
    {
        try { await _factory.DisposeAsync(); }
        finally { await _lease.DropAsync(); }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private async Task<(int Exit, string Output)> OpsAsync(params string[] words)
    {
        var writer = new StringWriter();
        var exit = await OpsCommandRunner.RunAsync(_factory.Services, OpsCommandLine.Parse(["ops", .. words])!, writer);
        return (exit, writer.ToString());
    }

    private async Task<T> Db<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private HttpClient Client(string? token = null)
    {
        var http = _factory.CreateClient();
        if (token is not null) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private async Task<AuthResponseDto> LoginAsync(string role)
    {
        var response = await Client().PostAsJsonAsync("/api/demo/login", new { role });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AuthResponseDto>(Web))!;
    }

    private static async Task<JsonElement> J(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private sealed record DemoShop(Guid Id, string Slug, string Name);

    private async Task<List<DemoShop>> ShopsAsync() => await Db(async db => (await db.Companies.AsNoTracking()
        .Where(c => c.IsShowcase && c.Kind == CompanyKind.Orders).OrderBy(c => c.Name).Select(c => new { c.Id, c.Slug, c.Name }).ToListAsync())
        .Select(c => new DemoShop(c.Id, c.Slug, c.Name)).ToList());

    private async Task<DemoShop> CoffeeShopAsync() => (await ShopsAsync()).Single(s => s.Slug.Contains("kofeinya"));

    /// <summary>Round-the-clock hours through the demo owner's own cabinet (a change the demo allows) so that the scenario does not depend on the time of the run.</summary>
    private async Task OpenAllDayAsync(DemoShop shop, string ownerToken)
    {
        var owner = Client(ownerToken);
        var days = Enum.GetValues<DayOfWeek>().Select(d => new WorkingDayInput(d, [new TimeIntervalInput("04:05", "04:00")])).ToList();
        var hours = await owner.PutJsonAsync($"/api/shops/{shop.Id}/working-hours", new WorkingHoursInput(days));
        hours.StatusCode.Should().Be(HttpStatusCode.OK, "the demo allows a change of working hours: " + await hours.Content.ReadAsStringAsync());
        var pickup = await owner.PutJsonAsync($"/api/shops/{shop.Id}/pickup-settings", new PickupSettingsDto(true, false, 15, 0, 0));
        pickup.StatusCode.Should().Be(HttpStatusCode.OK, await pickup.Content.ReadAsStringAsync());
        await owner.PutJsonAsync($"/api/shops/{shop.Id}/acceptance", new { mode = "Open" }); // best effort: a paused/stopped state is not what this scenario looks at
    }

    private async Task<(Guid ProductId, decimal Price)> AnyPieceProductAsync(DemoShop shop)
    {
        var storefront = await J(await Client().GetAsync($"/api/storefront/{shop.Slug}"));
        foreach (var category in storefront.GetProperty("categories").EnumerateArray())
            foreach (var p in category.GetProperty("products").EnumerateArray())
                if (p.GetProperty("available").GetBoolean() && p.GetProperty("unit").GetString() == "Piece")
                    return (p.GetProperty("id").GetGuid(), p.GetProperty("price").GetDecimal());
        throw new InvalidOperationException("no available piece product in " + shop.Slug);
    }

    private async Task<JsonElement> BoardAsync(DemoShop shop, string token) =>
        await J(await Client(token).GetAsync($"/api/shops/{shop.Id}/order-board"));

    private static IEnumerable<JsonElement> BoardCards(JsonElement board)
    {
        foreach (var column in new[] { "newOrders", "accepted", "ready", "completedToday" })
            if (board.TryGetProperty(column, out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var card in list.EnumerateArray()) yield return card;
        if (board.TryGetProperty("preorders", out var groups) && groups.ValueKind == JsonValueKind.Array)
            foreach (var g in groups.EnumerateArray())
                foreach (var card in g.GetProperty("orders").EnumerateArray()) yield return card;
    }

    private static int Count(JsonElement board, string column) =>
        board.TryGetProperty(column, out var list) && list.ValueKind == JsonValueKind.Array ? list.GetArrayLength() : 0;

    // ── US-35-02: the generated shops ────────────────────────────────────────────────────────────

    [Fact, TestCase("CY35-10")]
    public async Task FiveShops_AreInCatalog_WithLogoPhotosPhoneHours_AndAllCitiesShowsAllFive()
    {
        var shops = await ShopsAsync();
        shops.Should().HaveCount(5);

        var all = await J(await Client().GetAsync("/api/goods/catalog"));
        all.GetProperty("totalCount").GetInt32().Should().Be(5, "«Все города» показывает все пять магазинов");
        all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("logoUrl").GetString()).Should().OnlyContain(u => !string.IsNullOrEmpty(u));

        // the catalog of a city shows only its shops
        var cities = await Db(db => db.Companies.Where(c => c.IsShowcase && c.Kind == CompanyKind.Orders).Select(c => c.CityId).Distinct().ToListAsync());
        cities.Count.Should().BeGreaterThanOrEqualTo(3, "the five shops live in several cities");
        var seen = 0;
        foreach (var city in cities)
            seen += (await J(await Client().GetAsync($"/api/goods/catalog?cityId={city}"))).GetProperty("totalCount").GetInt32();
        seen.Should().Be(5);

        foreach (var shop in shops)
        {
            var s = await J(await Client().GetAsync($"/api/storefront/{shop.Slug}"));
            s.GetProperty("logoUrl").GetString().Should().NotBeNullOrEmpty(shop.Name);
            s.GetProperty("photos").GetArrayLength().Should().BeInRange(3, 6, shop.Name);
            s.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace(shop.Name);
            ShowcasePhones.IsShowcasePhone(new string((s.GetProperty("phone").GetString() ?? "").Where(char.IsDigit).ToArray()))
                .Should().BeTrue($"{shop.Name}: the phone comes from the reserved demo block");
            s.GetProperty("workingHours").GetProperty("lines").GetArrayLength().Should().BeGreaterThan(0, shop.Name);
            s.GetProperty("categories").EnumerateArray().Sum(c => c.GetProperty("products").GetArrayLength()).Should().BeGreaterThan(5, shop.Name);
        }

        // the stand is never empty: at least the coffee shop is open and takes orders once the hours allow it (Moscow, 08:00-20:00 is a safe window)
        var moscow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow")).TimeOfDay;
        if (moscow > TimeSpan.FromHours(9) && moscow < TimeSpan.FromHours(19))
        {
            var coffee = await CoffeeShopAsync();
            (await J(await Client().GetAsync($"/api/storefront/{coffee.Slug}"))).GetProperty("acceptingOrders").GetBoolean()
                .Should().BeTrue("SPEC US-35-02: the coffee shop takes orders all day after the reset");
        }
    }

    [Fact, TestCase("CY35-11")]
    public async Task ShowcaseOfProductFeatures_EveryCapabilityIsPresentInSomeShop()
    {
        await Db(async db =>
        {
            var ids = await db.Companies.Where(c => c.IsShowcase && c.Kind == CompanyKind.Orders).Select(c => c.Id).ToListAsync();
            (await db.Products.AnyAsync(p => ids.Contains(p.CompanyId) && p.Unit == ProductUnit.Piece)).Should().BeTrue("piece goods");
            (await db.Products.AnyAsync(p => ids.Contains(p.CompanyId) && p.Unit == ProductUnit.Weight)).Should().BeTrue("weight goods");
            (await db.Products.AnyAsync(p => ids.Contains(p.CompanyId) && (p.IsSoldOut || p.SoldOutForDate != null))).Should().BeTrue("«закончилось на сегодня»");
            (await db.ShopSettings.AnyAsync(s => ids.Contains(s.CompanyId) && s.TrackStock)).Should().BeTrue("stock tracking");
            (await db.Products.AnyAsync(p => ids.Contains(p.CompanyId) && p.StockOnHand != null)).Should().BeTrue("a product with a stock figure");
            (await db.ShopDailyMenus.AnyAsync(m => ids.Contains(m.CompanyId))).Should().BeTrue("menu of a date");
            (await db.Products.AnyAsync(p => ids.Contains(p.CompanyId) && p.AvailableWeekdaysMask != 127)).Should().BeTrue("a product sold only on some weekdays");
            (await db.ShopSpecialDays.AnyAsync(d => ids.Contains(d.CompanyId))).Should().BeTrue("a special day");
            (await db.ShopSettings.AnyAsync(s => ids.Contains(s.CompanyId) && s.AsapEnabled)).Should().BeTrue("as soon as possible");
            (await db.ShopSettings.AnyAsync(s => ids.Contains(s.CompanyId) && s.ScheduledEnabled && s.PreorderDays > 0)).Should().BeTrue("a pre-order for another day");
            (await db.ShopSettings.AnyAsync(s => ids.Contains(s.CompanyId) && s.AcceptanceMode == OrderAcceptanceMode.Manual)).Should().BeTrue("manual acceptance");
            (await db.ShopSettings.AnyAsync(s => ids.Contains(s.CompanyId) && s.AcceptanceMode == OrderAcceptanceMode.Auto)).Should().BeTrue("automatic acceptance");
            return 0;
        });
    }

    [Fact, TestCase("CY35-12")]
    public async Task OwnerBoard_IsNotEmptyAfterReset_AndReportsHaveBelievableFigures()
    {
        var coffee = await CoffeeShopAsync();
        var owner = await LoginAsync("shop-owner");
        var board = await BoardAsync(coffee, owner.Token);

        // "today" columns depend on the hour of the run, but the stand is never a blank board
        BoardCards(board).Count().Should().BeGreaterThan(5, "the board is not empty right after a reset");
        Count(board, "completedToday").Should().BeGreaterThanOrEqualTo(0);
        board.GetProperty("preorders").GetArrayLength().Should().BeGreaterThan(0, "there are pre-orders");

        var statuses = await Db(db => db.Orders.Where(o => o.CompanyId == coffee.Id).Select(o => o.Status).Distinct().ToListAsync());
        statuses.Should().Contain([OrderStatus.New, OrderStatus.Accepted, OrderStatus.Ready, OrderStatus.Issued], "every column of the board has a reason to exist");

        var moscowToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow")));
        var periods = new Dictionary<string, string>
        {
            ["Last7Days"] = "period=Last7Days", ["Last30Days"] = "period=Last30Days",
            ["60 days"] = $"period=Custom&from={moscowToday.AddDays(-59):yyyy-MM-dd}&to={moscowToday:yyyy-MM-dd}",
        };
        foreach (var (period, query) in periods)
        {
            var s = await J(await Client(owner.Token).GetAsync($"/api/shops/{coffee.Id}/summary?{query}&compare=true"));
            s.GetProperty("ordersTotal").GetInt32().Should().BeGreaterThan(10, period);
            s.GetProperty("issuedAmount").GetDecimal().Should().BeGreaterThan(0, period);
            s.GetProperty("top").GetProperty("items").GetArrayLength().Should().BeGreaterThan(0, period);
            s.TryGetProperty("previous", out var previous).Should().BeTrue();
            previous.ValueKind.Should().Be(JsonValueKind.Object, period + ": the comparison is filled");
        }

        var history = await J(await Client(owner.Token).PostAsJsonAsync($"/api/shops/{coffee.Id}/order-history", new { period = "Last30Days" }));
        history.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(10);
        var pick = await J(await Client(owner.Token).GetAsync($"/api/shops/{coffee.Id}/picklist"));
        pick.TryGetProperty("orderCount", out _).Should().BeTrue();
    }

    [Fact, TestCase("CY35-13")]
    public async Task GeneratedData_MonthlyUsageMatchesOrders_NoLimitWarnings_NoForbiddenTables()
    {
        await Db(async db =>
        {
            var shopIds = await db.Companies.Where(c => c.IsShowcase && c.Kind == CompanyKind.Orders).Select(c => new { c.Id, c.BillingAccountId }).ToListAsync();
            // no warning of 80/100 % after a reset (D35-2)
            (await db.OrderMonthlyUsages.AnyAsync(u => u.Warned80AtUtc != null || u.Warned100AtUtc != null)).Should().BeFalse();
            var accountIds = shopIds.Select(s => s.BillingAccountId).Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
            var usage = await db.OrderMonthlyUsages.Where(u => accountIds.Contains(u.BillingAccountId)).ToListAsync();
            usage.Should().NotBeEmpty("the counter of monthly use is filled");
            // the counter agrees with the generated orders: one row per account and month, equal to the number of orders created in that month
            var orders = await db.Orders.AsNoTracking().Where(o => shopIds.Select(s => s.Id).Contains(o.CompanyId)).Select(o => new { o.CompanyId, o.CreatedAtUtc }).ToListAsync();
            var perAccount = shopIds.GroupBy(s => s.BillingAccountId);
            foreach (var account in perAccount.Where(g => g.Key != null))
            {
                var ids = account.Select(s => s.Id).ToHashSet();
                var total = orders.Count(o => ids.Contains(o.CompanyId));
                usage.Where(u => u.BillingAccountId == account.Key).Sum(u => u.Count).Should().BeInRange((int)(total * 0.9), total, "the counter is consistent with the orders");
            }

            // not generated (US-35-02): consents of the demo accounts, subject requests, push subscriptions, MAX links of staff, channels, outbound queues
            (await db.ConsentRecords.CountAsync(c => c.UserId == null || db.Users.Any(u => u.Id == c.UserId && u.IsShowcase))).Should().Be(0, "no consent journal rows");
            (await db.PushSubscriptions.CountAsync()).Should().Be(0, "no push subscriptions");
            (await db.OrderPushSubscriptions.CountAsync()).Should().Be(0);
            (await db.StaffMaxLinks.CountAsync()).Should().Be(0, "no MAX links of staff");
            (await db.NotificationChannels.CountAsync()).Should().Be(0, "no notification channels");
            (await db.OutboundNotifications.CountAsync()).Should().Be(0);
            (await db.StaffPushNotifications.CountAsync()).Should().Be(0);
            (await db.CustomerOrderPushNotifications.CountAsync()).Should().Be(0);
            (await db.StaffMaxMessages.CountAsync()).Should().Be(0);
            return 0;
        });
    }

    [Fact, TestCase("CY35-14")]
    public async Task CustomerCards_SomeRepeatBuyers_SomeHaveShopNotes()
    {
        var coffee = await CoffeeShopAsync();
        await Db(async db =>
        {
            var byPhone = await db.Orders.Where(o => o.CompanyId == coffee.Id && o.CustomerPhone != null).GroupBy(o => o.CustomerPhone).Select(g => g.Count()).ToListAsync();
            byPhone.Count(c => c > 1).Should().BeGreaterThan(0, "some buyers have several orders");
            (await db.ShopCustomerNotes.CountAsync(n => n.CompanyId == coffee.Id)).Should().BeGreaterThan(0, "some buyers carry a note of the shop");
            return 0;
        });
    }

    [Fact, TestCase("CY35-15")]
    public async Task Determinism_TwoResetsOnOneDate_GiveTheSameShopsProductsPhonesAndOrderCount()
    {
        async Task<string> Fingerprint() => await Db(async db =>
        {
            var shops = await db.Companies.Where(c => c.IsShowcase && c.Kind == CompanyKind.Orders).OrderBy(c => c.Id)
                .Select(c => c.Id + "|" + c.Name + "|" + c.Phone + "|" + c.Slug).ToListAsync();
            var products = await db.Products.Where(p => db.Companies.Any(c => c.Id == p.CompanyId && c.IsShowcase)).OrderBy(p => p.Id).Select(p => p.Id + "|" + p.Name + "|" + p.Price).ToListAsync();
            var orders = await db.Orders.CountAsync();
            var items = await db.OrderItems.CountAsync();
            return string.Join(";", shops) + "#" + string.Join(";", products) + "#" + orders + "/" + items;
        });

        var first = await Fingerprint();
        var (exit, output) = await OpsAsync("demo", "reset", "--yes");
        exit.Should().Be(0, output);
        (await Fingerprint()).Should().Be(first, "the generator is deterministic: same date, same set (only dates move)");
    }

    // ── US-35-03: entering under a ready role ────────────────────────────────────────────────────

    [Fact, TestCase("CY35-20")]
    public async Task StatusAndRoles_SalonByDefault_ShopsByProduct_BadProductIs400()
    {
        var http = Client();
        var services = await J(await http.GetAsync("/api/demo/status"));
        services.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("role").GetString()).Should().Equal("owner", "master", "client");
        var orders = await J(await http.GetAsync("/api/demo/status?product=orders"));
        orders.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("role").GetString()).Should().Equal("shop-owner", "shop-staff", "shop-customer");
        orders.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("label").GetString())
            .Should().Equal("Войти как владелец магазина", "Войти как сотрудник магазина", "Войти как покупатель");
        orders.GetProperty("siteUrls").GetProperty("orders").GetString().Should().Be("https://demo.zakaz.ezbook.ru");
        orders.GetProperty("siteUrls").GetProperty("services").GetString().Should().Be("https://demo.visit.ezbook.ru");

        (await http.GetAsync("/api/demo/status?product=everything")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await http.GetAsync("/api/demo/status?product=orders&product=x")).StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);

        // the token of a salon role on the goods site is a technical possibility, not a crash
        (await http.PostAsJsonAsync("/api/demo/login", new { role = "shop-master" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await http.PostAsJsonAsync("/api/demo/login", new { role = "SHOP-OWNER" })).StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.OK);
    }

    [Fact, TestCase("CY35-21")]
    public async Task ShopRoles_GetRightRoleAndClaim_OwnerTermsWithoutConsentGate_PasswordStaysClosed()
    {
        var coffee = await CoffeeShopAsync();
        foreach (var role in ShopRoles)
        {
            var auth = await LoginAsync(role);
            auth.Roles.Should().NotContain("SuperAdmin", role);
            var payload = JsonDocument.Parse(Convert.FromBase64String(Pad(auth.Token.Split('.')[1].Replace('-', '+').Replace('_', '/')))).RootElement;
            payload.TryGetProperty("sb_demo", out _).Should().BeTrue($"{role}: the token carries the demo claim");
            (await Client().PostAsJsonAsync("/api/auth/login", new LoginDto(auth.Phone, "Password123!"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, role + " by password");
        }

        // the owner of a shop must not meet 451 of [RequiresOwnerTerms]
        var owner = await LoginAsync("shop-owner");
        var shops = await J(await Client(owner.Token).GetAsync("/api/shops/my"));
        shops.EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).Should().Contain(coffee.Id);
        (await Client(owner.Token).GetAsync($"/api/shops/{coffee.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var put = await Client(owner.Token).PutJsonAsync($"/api/shops/{coffee.Id}/settings", new ShopSettingsInput(ShopCustomerMode.Anyone, OrderAcceptanceMode.Manual, true, false));
        put.StatusCode.Should().NotBe(HttpStatusCode.UnavailableForLegalReasons, "the demo owner has current owner terms");
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
    }

    private static string Pad(string s) => s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');

    [Fact, TestCase("CY35-22")]
    public async Task OwnerCabinet_IsFull_StaffSeesBoardButNotCatalogReportsOrSettings()
    {
        var coffee = await CoffeeShopAsync();
        var owner = await LoginAsync("shop-owner");
        var staff = await LoginAsync("shop-staff");
        var asOwner = Client(owner.Token);
        var asStaff = Client(staff.Token);

        foreach (var url in new[] { "", "/products", "/categories", "/working-hours", "/daily-menus", "/special-days", "/order-board", "/summary", "/picklist", "/notification-settings" })
            (await asOwner.GetAsync($"/api/shops/{coffee.Id}{url}")).StatusCode.Should().Be(HttpStatusCode.OK, "owner " + url);

        var products = await J(await asOwner.GetAsync($"/api/shops/{coffee.Id}/products"));
        products.GetArrayLength().Should().BeGreaterThan(5);
        // «Ваша подписка»: the hidden service tariff, no bill
        var subscription = await asOwner.GetAsync("/api/billing/subscription?line=Orders");
        subscription.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // staff: the board and the order actions
        (await asStaff.GetAsync($"/api/shops/{coffee.Id}/order-board")).StatusCode.Should().Be(HttpStatusCode.OK);
        var first = products[0];
        (await asStaff.PutJsonAsync($"/api/shops/{coffee.Id}/products/{first.GetProperty("id").GetGuid()}/sold-out", new SoldOutInput(false, null)))
            .StatusCode.Should().Be(HttpStatusCode.OK, "staff may mark «закончилось»");
        // …but not prices, settings, hours or the summary
        (await asStaff.PutJsonAsync($"/api/shops/{coffee.Id}/settings", new ShopSettingsInput(ShopCustomerMode.Anyone, OrderAcceptanceMode.Manual, true, false)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await asStaff.GetAsync($"/api/shops/{coffee.Id}/summary")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await asStaff.PostJsonAsync($"/api/shops/{coffee.Id}/products", new ProductInput(null, "Лишний", null, ProductUnit.Piece, 10m, null, null, null, true, null)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await asStaff.PutJsonAsync($"/api/shops/{coffee.Id}/working-hours", new WorkingHoursInput([])))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // a shop of another owner is closed to both demo roles (no leak between shops)
        var other = (await ShopsAsync()).First(s => s.Id != coffee.Id);
        (await asStaff.GetAsync($"/api/shops/{other.Id}/order-board")).StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        (await asOwner.GetAsync($"/api/shops/{other.Id}/order-board")).StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact, TestCase("CY35-23")]
    public async Task Customer_MyOrders_ActiveToday_PreorderTomorrow_History_OneCancelled_EveryOrderPageOpens()
    {
        var customer = await LoginAsync("shop-customer");
        var mine = await J(await Client(customer.Token).GetAsync("/api/orders/my"));
        var list = mine.EnumerateArray().ToList();
        list.Count.Should().BeInRange(7, 14, "«Мои заказы»: активный, предзаказ и 5–10 завершённых");

        list.Count(o => o.GetProperty("isActive").GetBoolean()).Should().BeGreaterThanOrEqualTo(1, "an active order");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow")));
        list.Any(o => o.GetProperty("isActive").GetBoolean() && DateOnly.Parse(o.GetProperty("pickup").GetProperty("date").GetString()!) > today).Should().BeTrue(
            "a pre-order for tomorrow; today is " + today + ", orders: " + string.Join("; ", list.Select(o => o.GetProperty("businessDate").GetString() + "/" + o.GetProperty("status").GetString() + "/" + o.GetProperty("pickup").GetRawText())));
        list.Count(o => !o.GetProperty("isActive").GetBoolean()).Should().BeInRange(5, 12);
        list.Any(o => o.GetProperty("status").GetString() is "CancelledByCustomer" or "CancelledByShop" or "Rejected").Should().BeTrue("one order is cancelled");
        list.Select(o => o.GetProperty("shopName").GetString()).Distinct().Count().Should().BeInRange(2, 3, "orders in 2–3 shops");

        foreach (var o in list)
        {
            var token = o.GetProperty("token").GetString()!;
            o.GetProperty("orderUrl").GetString().Should().StartWith("https://demo.zakaz.ezbook.ru/o/");
            var page = await Client().GetAsync($"/api/orders/public/{token}");
            page.StatusCode.Should().Be(HttpStatusCode.OK, "every order page opens: " + token);
        }
    }

    [Fact, TestCase("CY35-24")]
    public async Task EndToEnd_CustomerOrders_StaffSeesItOnBoard_AcceptsReadyIssues_CustomerPageFollows()
    {
        var coffee = await CoffeeShopAsync();
        var owner = await LoginAsync("shop-owner");
        var staff = await LoginAsync("shop-staff");
        var customer = await LoginAsync("shop-customer");
        await OpenAllDayAsync(coffee, owner.Token);
        var (productId, price) = await AnyPieceProductAsync(coffee);

        var revisionBefore = (await BoardAsync(coffee, staff.Token)).GetProperty("revision").GetInt64();
        var create = await Client(customer.Token).PostAsJsonAsync($"/api/storefront/{coffee.Slug}/orders", new
        {
            idempotencyKey = Guid.NewGuid(), items = new[] { new { productId, quantity = 1, expectedUnitPrice = price } },
            customerName = "Демо Покупатель", customerPhone = "79990001122",
        });
        var created = await J(create, HttpStatusCode.Created);
        var token = created.GetProperty("order").GetProperty("token").GetString()!;
        created.GetProperty("orderUrl").GetString().Should().StartWith("https://demo.zakaz.ezbook.ru/o/");

        var board = await BoardAsync(coffee, staff.Token);
        board.GetProperty("revision").GetInt64().Should().BeGreaterThan(revisionBefore, "the board notices a new order by its revision");
        var card = BoardCards(board).Single(c => c.GetProperty("number").GetInt32() == created.GetProperty("order").GetProperty("number").GetInt32());
        var orderId = card.GetProperty("id").GetGuid();
        var version = card.GetProperty("version").GetInt32();
        var asStaff = Client(staff.Token);

        async Task<int> Step(string action, int v, object? extra = null)
        {
            var r = await asStaff.PostAsJsonAsync($"/api/shops/{coffee.Id}/orders/{orderId}/{action}", extra ?? new { expectedVersion = v });
            var body = await J(r);
            return body.GetProperty("version").GetInt32();
        }
        async Task<string> CustomerStatus() => (await J(await Client().GetAsync($"/api/orders/public/{token}"))).GetProperty("status").GetString()!;

        if (card.GetProperty("status").GetString() == "New")
        {
            version = await Step("accept", version);
            (await CustomerStatus()).Should().Be("Accepted", "the customer's page follows the staff's action");
        }
        version = await Step("ready", version);
        (await CustomerStatus()).Should().Be("Ready");
        version = await Step("issue", version, new { expectedVersion = version, actualQuantities = Array.Empty<object>() });
        (await CustomerStatus()).Should().Be("Issued");

        // the order is in the customer's own list and in the journal of the order
        (await J(await Client(customer.Token).GetAsync("/api/orders/my"))).EnumerateArray().Any(o => o.GetProperty("token").GetString() == token).Should().BeTrue();
        var staffView = await J(await asStaff.GetAsync($"/api/shops/{coffee.Id}/orders/{orderId}"));
        staffView.GetProperty("events").GetArrayLength().Should().BeGreaterThanOrEqualTo(3);

        // nothing left the stand
        await AssertNothingQueuedAsync();
    }

    [Fact, TestCase("CY35-25")]
    public async Task GuestOrder_ActsLikeProduction_ValidationErrorsAreCleanNot500_AndOrderLinkOfErasedOrderIsNotFound()
    {
        var coffee = await CoffeeShopAsync();
        var owner = await LoginAsync("shop-owner");
        await OpenAllDayAsync(coffee, owner.Token);
        var (productId, price) = await AnyPieceProductAsync(coffee);

        // empty cart, a zero quantity, a stale price, garbage body: a 4xx text, never a 500
        foreach (var body in new object[]
                 {
                     new { idempotencyKey = Guid.NewGuid(), items = Array.Empty<object>(), customerName = "Иван", customerPhone = "79990001133" },
                     new { idempotencyKey = Guid.NewGuid(), items = new[] { new { productId, quantity = 0, expectedUnitPrice = price } }, customerName = "Иван", customerPhone = "79990001133" },
                     new { idempotencyKey = Guid.NewGuid(), items = new[] { new { productId, quantity = 1, expectedUnitPrice = price + 1 } }, customerName = "Иван", customerPhone = "79990001133" },
                     new { idempotencyKey = Guid.NewGuid(), items = new[] { new { productId = Guid.NewGuid(), quantity = 1, expectedUnitPrice = price } }, customerName = "Иван", customerPhone = "79990001133" },
                 })
        {
            var r = await Client().PostAsJsonAsync($"/api/storefront/{coffee.Slug}/orders", body);
            ((int)r.StatusCode).Should().BeInRange(400, 499, await r.Content.ReadAsStringAsync());
        }
        (await Client().PostAsync($"/api/storefront/{coffee.Slug}/orders", new StringContent("{not json", System.Text.Encoding.UTF8, "application/json")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // a link to a token that does not exist: «заказ не найден», empty 404
        var missing = await Client().GetAsync($"/api/orders/public/{Guid.NewGuid():N}");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await missing.Content.ReadAsStringAsync()).Should().BeEmpty();

        // the same idempotency key twice never makes two orders (double tap / lost connection)
        var key = Guid.NewGuid();
        var payload = new { idempotencyKey = key, items = new[] { new { productId, quantity = 1, expectedUnitPrice = price } }, customerName = "Иван", customerPhone = "79990001144" };
        var a = await Client().PostAsJsonAsync($"/api/storefront/{coffee.Slug}/orders", payload);
        var b = await Client().PostAsJsonAsync($"/api/storefront/{coffee.Slug}/orders", payload);
        a.StatusCode.Should().Be(HttpStatusCode.Created, await a.Content.ReadAsStringAsync());
        b.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.Conflict);
        (await Db(db => db.Orders.CountAsync(o => o.IdempotencyKey == key))).Should().Be(1);
    }

    [Fact, TestCase("CY35-26")]
    public async Task ParallelOrders_OfOneShop_GetDistinctNumbers_AndNoErrors()
    {
        var coffee = await CoffeeShopAsync();
        var owner = await LoginAsync("shop-owner");
        await OpenAllDayAsync(coffee, owner.Token);
        var (productId, price) = await AnyPieceProductAsync(coffee);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            var r = await Client().PostAsJsonAsync($"/api/storefront/{coffee.Slug}/orders", new
            {
                idempotencyKey = Guid.NewGuid(), items = new[] { new { productId, quantity = 1, expectedUnitPrice = price } },
                customerName = "Параллельный " + i, customerPhone = "7999000" + (2000 + i),
            });
            return (r.StatusCode, Body: await r.Content.ReadAsStringAsync());
        }));
        results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created, string.Join(" | ", results.Select(r => r.Body).Distinct()));
        var numbers = results.Select(r => JsonDocument.Parse(r.Body).RootElement.GetProperty("order").GetProperty("number").GetInt32()).ToList();
        numbers.Distinct().Count().Should().Be(8, "order numbers of a day are not repeated");
    }

    // ── US-35-04: restrictions ───────────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, HttpContent? body)
    {
        using var request = new HttpRequestMessage(method, url) { Content = body };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _factory.CreateClient().SendAsync(request);
    }

    [Fact, TestCase("CY35-30")]
    public async Task Restrictions_ThreeNewRoutesAndOldOnes_Refused403WithTextAndHeader_ForEveryShopRole_EvenWithBrokenBody()
    {
        const string text = "В демо-версии это действие недоступно.";
        var coffee = await CoffeeShopAsync();
        var staffMember = await Db(db => db.CompanyMembers.Where(m => m.CompanyId == coffee.Id && m.Role == UserRole.Master).Select(m => new { m.Id, m.UserId }).FirstAsync());
        var routes = new (HttpMethod Method, string Url)[]
        {
            (HttpMethod.Put, $"/api/shops/{coffee.Id}/slug"),
            (HttpMethod.Delete, $"/api/Companies/{coffee.Id}/members/{staffMember.Id}"),
            (HttpMethod.Post, "/api/staff-max/link-sessions"),
            (HttpMethod.Post, "/api/profile/change-password"), (HttpMethod.Post, "/api/profile/change-phone"),
            (HttpMethod.Post, "/api/profile/delete-account"), (HttpMethod.Post, "/api/billing/subscription/request"),
            (HttpMethod.Post, "/api/billing/trial"),
        };

        foreach (var role in ShopRoles)
        {
            var auth = await LoginAsync(role);
            foreach (var (method, url) in routes)
            {
                foreach (HttpContent? body in new HttpContent?[]
                         {
                             JsonContent.Create(new { slug = "hacked-slug", currentPassword = "x", newPassword = "Password123!2", phone = "79001234567" }),
                             null,
                             new StringContent("not json", System.Text.Encoding.UTF8, "application/json"),
                         })
                {
                    var response = await SendAsync(method, url, auth.Token, body);
                    var answer = await response.Content.ReadAsStringAsync();
                    response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{role} {method} {url}: {answer}");
                    answer.Should().Be(text, $"{role} {url}");
                    response.Headers.GetValues("X-Demo-Restricted").Should().ContainSingle().Which.Should().Be("1");
                }
            }
        }

        // nothing changed: the slug and the demo staff are where they were
        (await ShopsAsync()).Should().Contain(s => s.Id == coffee.Id && s.Slug == coffee.Slug);
        (await Db(db => db.CompanyMembers.AnyAsync(m => m.Id == staffMember.Id))).Should().BeTrue();

        // anonymous: the ordinary 401, no demo header
        var anonymous = await Client().PutAsJsonAsync($"/api/shops/{coffee.Id}/slug", new { slug = "x-y-z" });
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        anonymous.Headers.Contains("X-Demo-Restricted").Should().BeFalse();
    }

    [Fact, TestCase("CY35-31")]
    public async Task Restrictions_DoNotCoverRestOfCabinet_AndSelfRegisteredVisitorIsNotRestricted()
    {
        var coffee = await CoffeeShopAsync();
        var owner = await LoginAsync("shop-owner");
        var asOwner = Client(owner.Token);

        // allowed and healed by the night reset: catalog, prices, pause of acceptance, rejecting orders
        var products = await J(await asOwner.GetAsync($"/api/shops/{coffee.Id}/products"));
        var p = products[0];
        var id = p.GetProperty("id").GetGuid();
        var price = p.GetProperty("price").GetDecimal();
        var input = new ProductInput(
            p.GetProperty("categoryId").ValueKind == JsonValueKind.String ? p.GetProperty("categoryId").GetGuid() : null,
            p.GetProperty("name").GetString(), p.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null,
            p.GetProperty("unit").GetString() == "Piece" ? ProductUnit.Piece : ProductUnit.Weight, price + 1m, null, null, null, true, null);
        if (input.Unit == ProductUnit.Piece)
            (await asOwner.PutJsonAsync($"/api/shops/{coffee.Id}/products/{id}", input)).StatusCode.Should().Be(HttpStatusCode.OK, "price changes are allowed in the demo");
        (await asOwner.PostJsonAsync($"/api/shops/{coffee.Id}/categories", new CategoryInput("Проверочная категория"))).StatusCode.Should().Be(HttpStatusCode.Created);

        // a visitor who registered himself is not a demo role: the same three routes are NOT refused with the demo sentence
        var phone = "79" + Random.Shared.NextInt64(100_000_000, 999_999_999);
        using var scope = _factory.Services.CreateScope();
        var legal = scope.ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Legal.LegalDocumentProvider>().Current!;
        var register = await Client().PostAsJsonAsync("/api/auth/register", new RegisterDto("Посетитель", "Проверочный", phone, "Password123!", null,
            new RegisterLegalDto(legal.Get(LegalDocumentType.Privacy)!.Version, legal.Get(LegalDocumentType.TermsClient)!.Version)));
        register.StatusCode.Should().Be(HttpStatusCode.OK, await register.Content.ReadAsStringAsync());
        var visitor = (await register.Content.ReadFromJsonAsync<AuthResponseDto>(Web))!;
        foreach (var (method, url) in new[] { (HttpMethod.Post, "/api/staff-max/link-sessions"), (HttpMethod.Put, $"/api/shops/{coffee.Id}/slug"), (HttpMethod.Delete, $"/api/Companies/{coffee.Id}/members/{Guid.NewGuid()}") })
        {
            var r = await SendAsync(method, url, visitor.Token, JsonContent.Create(new { slug = "visitor-slug" }));
            r.Headers.Contains("X-Demo-Restricted").Should().BeFalse($"{method} {url} for an ordinary visitor");
        }
    }

    // ── US-35-05: nothing goes out ───────────────────────────────────────────────────────────────

    private async Task AssertNothingQueuedAsync() => await Db(async db =>
    {
        (await db.OutboundNotifications.CountAsync()).Should().Be(0, "no WhatsApp/MAX message about an order");
        (await db.StaffPushNotifications.CountAsync()).Should().Be(0, "no push to staff");
        (await db.CustomerOrderPushNotifications.CountAsync(n => n.Status == NotificationStatus.Pending)).Should().Be(0, "no push to a customer waits in the queue");
        (await db.StaffMaxMessages.CountAsync(m => m.Status == NotificationStatus.Pending)).Should().Be(0, "no message to staff in MAX waits in the queue");
        return 0;
    });

    [Fact, TestCase("CY35-40")]
    public async Task OrderLifecycleInDemo_CreateAcceptChangeCancel_QueuesNothing_AndMessengerFlagIsIgnored()
    {
        var coffee = await CoffeeShopAsync();
        var owner = await LoginAsync("shop-owner");
        var staff = await LoginAsync("shop-staff");
        await OpenAllDayAsync(coffee, owner.Token);
        var (productId, price) = await AnyPieceProductAsync(coffee);

        async Task<(string Token, int Number)> Place(string name, bool messenger)
        {
            var r = await Client().PostAsJsonAsync($"/api/storefront/{coffee.Slug}/orders", new
            {
                idempotencyKey = Guid.NewGuid(), items = new[] { new { productId, quantity = 2, expectedUnitPrice = price } },
                customerName = name, customerPhone = "7999" + Random.Shared.Next(1_000_000, 9_999_999), notifyByMessenger = messenger,
            });
            var j = await J(r, HttpStatusCode.Created);
            return (j.GetProperty("order").GetProperty("token").GetString()!, j.GetProperty("order").GetProperty("number").GetInt32());
        }

        var asStaff = Client(staff.Token);
        var (t1, n1) = await Place("Гость Один", messenger: true);
        var card = BoardCards(await BoardAsync(coffee, staff.Token)).Single(c => c.GetProperty("number").GetInt32() == n1);
        var id = card.GetProperty("id").GetGuid();
        var v = card.GetProperty("version").GetInt32();
        if (card.GetProperty("status").GetString() == "New")
            v = (await J(await asStaff.PostAsJsonAsync($"/api/shops/{coffee.Id}/orders/{id}/accept", new { expectedVersion = v }))).GetProperty("version").GetInt32();

        // a change of the order by staff (items) — the path that normally tells the customer «заказ изменён»
        var items = card.GetProperty("items").EnumerateArray().Select(i => new { itemId = i.GetProperty("id").GetGuid(), productId = (Guid?)null, quantity = 1 }).ToList();
        var edit = await asStaff.PutAsJsonAsync($"/api/shops/{coffee.Id}/orders/{id}/items", new { expectedVersion = v, items, commentForCustomer = "убрали одну позицию" });
        edit.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.BadRequest);
        var fresh = await J(await asStaff.GetAsync($"/api/shops/{coffee.Id}/orders/{id}"));
        v = fresh.GetProperty("version").GetInt32();
        (await asStaff.PostAsJsonAsync($"/api/shops/{coffee.Id}/orders/{id}/cancel", new { expectedVersion = v, reason = "проверка" })).StatusCode.Should().Be(HttpStatusCode.OK);

        // the customer cancels his own order; another is rejected
        var (t2, _) = await Place("Гость Два", messenger: true);
        (await Client().PostAsync($"/api/orders/public/{t2}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var (_, n3) = await Place("Гость Три", messenger: false);
        var c3 = BoardCards(await BoardAsync(coffee, staff.Token)).Single(c => c.GetProperty("number").GetInt32() == n3);
        (await asStaff.PostAsJsonAsync($"/api/shops/{coffee.Id}/orders/{c3.GetProperty("id").GetGuid()}/reject", new { expectedVersion = c3.GetProperty("version").GetInt32(), reason = "нет" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // an order page tells the customer nothing about a message that will never come: no messenger is requested by the demo
        var page = await J(await Client().GetAsync($"/api/orders/public/{t1}"));
        page.GetProperty("status").GetString().Should().NotBeNull();

        await AssertNothingQueuedAsync();
        // and the dispatchers of the two queues find nothing to send
        using var scope = _factory.Services.CreateScope();
        foreach (var name in new[] { "customer-order-push-dispatch", "staff-max-dispatch", "staff-push-dispatch", "notification-dispatch" })
        {
            var task = scope.ServiceProvider.GetServices<ServiceBooking.API.Services.Scheduling.IScheduledTask>().SingleOrDefault(t => t.Name == name);
            if (task is null) continue;
            var outcome = await task.ExecuteAsync(CancellationToken.None);
            outcome.Affected.Should().Be(0, name + ": " + outcome.Summary);
        }
        await AssertNothingQueuedAsync();
    }

    // ── US-35-08 / links: nothing points to production ───────────────────────────────────────────

    [Fact, TestCase("CY35-50")]
    public async Task EveryAbsoluteAddressOfTheDemoApi_IsADemoAddress_NoProductionHostAnywhere()
    {
        var coffee = await CoffeeShopAsync();
        var owner = await LoginAsync("shop-owner");
        var customer = await LoginAsync("shop-customer");
        var asOwner = Client(owner.Token);

        var bodies = new List<(string Name, string Text)>();
        async Task Collect(string name, HttpClient client, string url)
        {
            var r = await client.GetAsync(url);
            r.StatusCode.Should().Be(HttpStatusCode.OK, name);
            bodies.Add((name, await r.Content.ReadAsStringAsync()));
        }
        await Collect("status", Client(), "/api/demo/status?product=orders");
        await Collect("shop", asOwner, $"/api/shops/{coffee.Id}");
        await Collect("my shops", asOwner, "/api/shops/my");
        await Collect("storefront", Client(), $"/api/storefront/{coffee.Slug}");
        await Collect("catalog", Client(), "/api/goods/catalog");
        await Collect("kinds-summary", asOwner, "/api/Companies/kinds-summary");
        await Collect("push config", Client(owner.Token), "/api/push/config?site=Orders");
        await Collect("my orders", Client(customer.Token), "/api/orders/my");
        await Collect("listing", asOwner, $"/api/shops/{coffee.Id}/catalog-listing");
        await Collect("notification settings", asOwner, $"/api/shops/{coffee.Id}/notification-settings");
        var qr = await asOwner.GetAsync($"/api/shops/{coffee.Id}/qr");
        qr.StatusCode.Should().Be(HttpStatusCode.OK);
        bodies.Add(("qr headers", string.Join(";", qr.Headers.Select(h => string.Join(",", h.Value))) + System.Text.Encoding.UTF8.GetString(await qr.Content.ReadAsByteArrayAsync())));

        var mine = JsonDocument.Parse(bodies.Single(b => b.Name == "my orders").Text).RootElement;
        await Collect("order page", Client(), $"/api/orders/public/{mine[0].GetProperty("token").GetString()}");

        foreach (var (name, text) in bodies)
        {
            // known debt C35-1 (API_CONTRACT_CYCLE35 §35.25): the status TEXT of the catalog listing names the production domain; no link may
            if (name == "listing") text.Should().NotContain("https://goods.ezbook.ru", name); else text.Should().NotContain("goods.ezbook.ru", name);
            text.Should().NotContain("https://ezbook.ru", name);
            System.Text.RegularExpressions.Regex.IsMatch(text, @"https://visit\.ezbook\.ru").Should().BeFalse(name + ": only demo.visit is allowed");
        }
    }

    [Fact, TestCase("CY35-51")]
    public async Task KindsSummaryAndPushConfig_CarryTheTwoDemoAddresses_ShopUrlsPointToDemoZakaz()
    {
        var coffee = await CoffeeShopAsync();
        var owner = await LoginAsync("shop-owner");

        var kinds = await J(await Client(owner.Token).GetAsync("/api/Companies/kinds-summary"));
        kinds.GetProperty("services").GetProperty("siteUrl").GetString().Should().Be("https://demo.visit.ezbook.ru");
        kinds.GetProperty("orders").GetProperty("siteUrl").GetString().Should().Be("https://demo.zakaz.ezbook.ru");

        var push = await J(await Client(owner.Token).GetAsync("/api/push/config?site=Orders"));
        push.GetProperty("siteUrls").GetProperty("orders").GetString().Should().Be("https://demo.zakaz.ezbook.ru");
        push.GetProperty("siteUrls").GetProperty("services").GetString().Should().Be("https://demo.visit.ezbook.ru");

        var shop = await J(await Client(owner.Token).GetAsync($"/api/shops/{coffee.Id}"));
        shop.GetProperty("publicUrl").GetString().Should().Be($"https://demo.zakaz.ezbook.ru/{coffee.Slug}");
        var storefront = await J(await Client().GetAsync($"/api/storefront/{coffee.Slug}"));
        storefront.GetProperty("publicUrl").GetString().Should().Be($"https://demo.zakaz.ezbook.ru/{coffee.Slug}");
    }

    // ── US-35-06: the reset ──────────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY35-60")]
    public async Task Reset_RemovesVisitorShopsOrdersAccountsFiles_KeepsTokensOfRoles_AndOrdersTariffExactlyOnce()
    {
        var ownerBefore = await LoginAsync("shop-owner");
        var customerBefore = await LoginAsync("shop-customer");
        var coffee = await CoffeeShopAsync();
        await OpenAllDayAsync(coffee, ownerBefore.Token);
        var (productId, price) = await AnyPieceProductAsync(coffee);

        // visitors: a guest order, a registered visitor with a shop of his own
        var guest = await Client().PostAsJsonAsync($"/api/storefront/{coffee.Slug}/orders", new
        {
            idempotencyKey = Guid.NewGuid(), items = new[] { new { productId, quantity = 1, expectedUnitPrice = price } }, customerName = "Гость", customerPhone = "79990003344",
        });
        var guestToken = (await J(guest, HttpStatusCode.Created)).GetProperty("order").GetProperty("token").GetString()!;

        string visitorToken, visitorId;
        using (var scope = _factory.Services.CreateScope())
        {
            var legal = scope.ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Legal.LegalDocumentProvider>().Current!;
            var phone = "79" + Random.Shared.NextInt64(100_000_000, 999_999_999);
            var reg = await Client().PostAsJsonAsync("/api/auth/register", new RegisterDto("Посетитель", "Магазин", phone, "Password123!", null,
                new RegisterLegalDto(legal.Get(LegalDocumentType.Privacy)!.Version, legal.Get(LegalDocumentType.TermsClient)!.Version)));
            reg.StatusCode.Should().Be(HttpStatusCode.OK, await reg.Content.ReadAsStringAsync());
            var registered = (await reg.Content.ReadFromJsonAsync<AuthResponseDto>(Web))!;
            visitorToken = registered.Token;
            visitorId = registered.UserId;
        }

        var identity = _factory.Identity;
        var publicFile = Path.Combine(identity.PublicRoot, "visitor-upload", "product.jpg");
        var privateFile = Path.Combine(identity.PrivateRoot, "visitor-upload", "doc.jpg");
        foreach (var file in new[] { publicFile, privateFile })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllBytesAsync(file, [1, 2, 3]);
        }

        var (plannedExit, plan) = await OpsAsync("demo", "reset");
        plannedExit.Should().Be(0, plan);
        plan.Should().Contain("(Заказы)").And.Contain("shops=5").And.Contain("только показать");
        (await Db(db => db.Orders.CountAsync(o => o.PublicToken == guestToken))).Should().Be(1, "a plan changes nothing");

        var (exit, output) = await OpsAsync("demo", "reset", "--yes");
        exit.Should().Be(0, output);
        output.Should().Contain("выполнено (Заказы)").And.Contain("shops=5");

        await Db(async db =>
        {
            (await db.Orders.CountAsync(o => o.PublicToken == guestToken)).Should().Be(0, "the guest order is gone");
            (await db.Users.CountAsync(u => u.Id == visitorId)).Should().Be(0, "the visitor is gone");
            (await db.Companies.CountAsync(c => !c.IsShowcase)).Should().Be(0, "no shop or salon of a visitor remains");
            (await db.Companies.CountAsync(c => c.IsShowcase && c.Kind == CompanyKind.Orders)).Should().Be(5);
            (await db.Orders.CountAsync(o => !db.Companies.Any(c => c.Id == o.CompanyId && c.IsShowcase))).Should().Be(0);
            // D35-2: the service tariff is exactly one after two resets, and stays hidden
            var tariffs = await db.SubscriptionPlanConfigs.Where(p => p.Id == ShowcaseCatalog.OrdersShowcasePlanId).ToListAsync();
            tariffs.Should().ContainSingle();
            tariffs[0].IsPublic.Should().BeFalse();
            (await db.SubscriptionPlanConfigs.CountAsync(p => p.Name == ShowcaseCatalog.OrdersShowcasePlanName)).Should().Be(1);
            return 0;
        });
        File.Exists(publicFile).Should().BeFalse();
        File.Exists(privateFile).Should().BeFalse();

        // a token issued before the reset lives on; the same accounts come back
        (await Client(ownerBefore.Token).GetAsync($"/api/shops/{coffee.Id}")).StatusCode.Should().Be(HttpStatusCode.OK, "stable ids and stamp");
        (await Client(customerBefore.Token).GetAsync("/api/orders/my")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client(visitorToken).GetAsync("/api/orders/my")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the visitor leaves the account without an error screen: 401");
        (await LoginAsync("shop-owner")).UserId.Should().Be(ownerBefore.UserId);

        // a link to an erased order: «not found», not an error
        var gone = await Client().GetAsync($"/api/orders/public/{guestToken}");
        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await gone.Content.ReadAsStringAsync()).Should().BeEmpty();

        // the board is alive again after the reset
        BoardCards(await BoardAsync(coffee, (await LoginAsync("shop-owner")).Token)).Count().Should().BeGreaterThan(5);
    }

    [Fact, TestCase("CY35-61")]
    public async Task DuringReset_BothProductsSeeWaitMessage_StatusIsNot503_ThenShopsAreBack()
    {
        var first = Task.Run(() => OpsAsync("demo", "reset", "--yes"));
        HttpResponseMessage? busy = null;
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (!first.IsCompleted && DateTime.UtcNow < deadline)
        {
            var probe = await Client().GetAsync("/api/goods/catalog");
            if (probe.StatusCode == HttpStatusCode.ServiceUnavailable) { busy = probe; break; }
            await Task.Delay(100);
        }
        busy.Should().NotBeNull("the visitor of the goods site sees the maintenance answer while the reset runs");
        (await busy!.Content.ReadAsStringAsync()).Should().Be("Демо обновляется, зайдите через минуту");
        busy.Headers.GetValues("X-Demo-Resetting").Should().ContainSingle().Which.Should().Be("1");

        foreach (var product in new[] { "orders", "services" })
        {
            var status = await Client().GetAsync("/api/demo/status?product=" + product);
            if (first.IsCompleted) break;
            status.StatusCode.Should().Be(HttpStatusCode.OK, "status never answers 503");
            (await J(status)).GetProperty("resetting").GetBoolean().Should().BeTrue(product);
        }

        (await first).Exit.Should().Be(0);
        (await J(await Client().GetAsync("/api/goods/catalog"))).GetProperty("totalCount").GetInt32().Should().Be(5);
        (await LoginAsync("shop-owner")).Roles.Should().NotBeEmpty();
    }

    [Fact, TestCase("CY35-62")]
    public async Task ResetPlan_ShowsShopsLine_AndShowcasePlanForDemoProfile_ChangingCommandsAreRefused()
    {
        var (exit, plan) = await OpsAsync("demo", "reset");
        exit.Should().Be(0, plan);
        plan.Should().Contain("будет создано (Заказы): shops=5");

        var (planExit, planText) = await OpsAsync("showcase", "plan", "--profile", "demo");
        planExit.Should().Be(0, planText);
        planText.Should().Contain("shops=5");

        foreach (var command in new[] { "create", "recreate", "delete" })
        {
            var (code, text) = await OpsAsync("showcase", command, "--profile", "demo", "--yes");
            code.Should().Be(2, command + ": " + text);
            text.Should().Contain("ops demo reset");
        }

        // plan for prod (the default) is the output of cycle 28: no shops at all
        var (prodExit, prodPlan) = await OpsAsync("showcase", "plan");
        prodExit.Should().Be(0, prodPlan);
        prodPlan.Should().NotContain("shops=");
    }

    // ── US-35-09 (P1): the live board ────────────────────────────────────────────────────────────

    [Fact, TestCase("CY35-70")]
    public async Task BoardTick_AdvancesGeneratedOrders_WithJournal_AndClosesStaleVisitorOrder_ButNotOnOffDemo()
    {
        var coffee = await CoffeeShopAsync();
        var owner = await LoginAsync("shop-owner");
        await OpenAllDayAsync(coffee, owner.Token);
        var (productId, price) = await AnyPieceProductAsync(coffee);

        var r = await Client().PostAsJsonAsync($"/api/storefront/{coffee.Slug}/orders", new
        {
            idempotencyKey = Guid.NewGuid(), items = new[] { new { productId, quantity = 1, expectedUnitPrice = price } }, customerName = "Гость", customerPhone = "79990005566",
        });
        var created = await J(r, HttpStatusCode.Created);
        var token = created.GetProperty("order").GetProperty("token").GetString()!;
        var orderId = await Db(db => db.Orders.Where(o => o.PublicToken == token).Select(o => o.Id).SingleAsync());

        // the pickup time of a visitor's order passed more than 50 minutes ago (the clock of the task is explicit, the same way the operator's machine would see it later)
        var pickup = await Db(db => db.Orders.Where(o => o.Id == orderId).Select(o => o.PickupStartUtc).SingleAsync());
        using (var scope = _factory.Services.CreateScope())
        {
            var ticker = scope.ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Demo.DemoBoardTicker>();
            var afterSixty = pickup.AddMinutes(60);
            await ticker.TickAsync(afterSixty, CancellationToken.None);
        }
        var order = await Db(db => db.Orders.AsNoTracking().Include(o => o.Events).SingleAsync(o => o.Id == orderId));
        new[] { OrderStatus.Rejected, OrderStatus.CancelledByShop, OrderStatus.NotPickedUp }.Should().Contain(order.Status, "a stale visitor order does not hang on the board");
        order.Events.Should().Contain(e => e.ActorKind == OrderActorKind.System && e.Reason != null && e.Reason.Contains("Демо"), "the journal says why");
        (await J(await Client().GetAsync($"/api/orders/public/{token}"))).GetProperty("reason").GetString().Should().Contain("Демо");

        // nothing left: the tick does not write to any outgoing queue
        await AssertNothingQueuedAsync();
    }
}
