using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 35 — US-35-07 (the lock of the demo configuration also demands a demo address of "Заказы"). Written from SPEC_CYCLE35 and API_CONTRACT_CYCLE35.
/// Each production-looking value of the two new settings, one at a time, stops the start with a message that names it.
/// </summary>
[Collection(Cycle35DemoSlots.ReadCollection)]
public class Cycle35DemoLocksTests : IAsyncLifetime
{
    private TestClassDatabaseLease _lease = null!;

    public async Task InitializeAsync() => _lease = await TestRunEnvironment.LeaseClassDatabaseAsync(Cycle35DemoSlots.Read);

    public async Task DisposeAsync()
    {
        if (_lease is not null) await _lease.DropAsync(); // InitializeAsync may have failed before the lease was taken
    }

    private static string Everything(Exception exception)
    {
        var sb = new StringBuilder();
        for (var e = exception; e is not null; e = e.InnerException!) { sb.AppendLine(e.Message); if (e.InnerException is null) break; }
        return sb.ToString();
    }

    public static IEnumerable<object[]> ProductionShapes() =>
    [
        ["PublicSites:OrdersBaseUrl", "", "OrdersBaseUrl"],
        ["PublicSites:OrdersBaseUrl", "https://goods.ezbook.ru", "OrdersBaseUrl"],
        ["PublicSites:OrdersBaseUrl", "https://ezbook.ru", "OrdersBaseUrl"],
        ["AllowedOrigins", "https://demo.visit.ezbook.ru,https://demo.zakaz.ezbook.ru,https://goods.ezbook.ru", "AllowedOrigins"],
        ["AllowedOrigins", "https://goods.ezbook.ru", "AllowedOrigins"],
    ];

    [Theory, TestCase("CY35-80")]
    [MemberData(nameof(ProductionShapes))]
    public void Start_IsRefused_ByProductionLookingAddressOfOrders(string key, string value, string mustBeNamed)
    {
        using var factory = new DemoHostFactory(_lease.ConnectionString, new Dictionary<string, string?> { [key] = value });
        var start = () => { _ = factory.Services; };
        var thrown = start.Should().Throw<Exception>().Which;
        Everything(thrown).Should().Contain(mustBeNamed);
    }

    [Fact, TestCase("CY35-80")]
    public void Start_WithBothDemoAddresses_Succeeds_AndAllProblemsAreReportedAtOnce()
    {
        using (var good = new DemoHostFactory(_lease.ConnectionString))
        {
            var start = () => { _ = good.Services; };
            start.Should().NotThrow();
        }

        using var bad = new DemoHostFactory(_lease.ConnectionString, new Dictionary<string, string?>
        {
            ["PublicSites:OrdersBaseUrl"] = "https://goods.ezbook.ru", ["PublicSites:ServicesBaseUrl"] = "https://visit.ezbook.ru",
        });
        var fail = () => { _ = bad.Services; };
        var message = Everything(fail.Should().Throw<Exception>().Which);
        message.Should().Contain("OrdersBaseUrl").And.Contain("ServicesBaseUrl", "all violations in one message");
    }
}

/// <summary>
/// QA cycle 35 — what must NOT change outside the demo (US-35-04 last item, US-35-05 control, US-35-07): the three new restrictions do nothing on an ordinary host, the
/// demo routes answer an empty 404, no demo header appears, and the outgoing-message guard is decided by the showcase mark alone: a showcase-marked shop on an ordinary
/// host with every sender switched on queues nothing, while the same flow on an unmarked shop queues rows (the control that proves the zeros are not vacuous).
/// </summary>
public class Cycle35OffDemoTests(TestDatabaseFixture fixture) : Cycle24TestBase(fixture)
{
    [Fact, TestCase("CY35-81")]
    public async Task OutsideDemo_DemoRoutesAreEmpty404_NoDemoHeaders_AndNewRestrictionsBehaveAsBefore()
    {
        foreach (var url in new[] { "/api/demo/status", "/api/demo/status?product=orders", "/api/demo/status?product=garbage" })
        {
            var r = await AnonymousClient().GetAsync(url);
            r.StatusCode.Should().Be(HttpStatusCode.NotFound, url);
            (await r.Content.ReadAsStringAsync()).Should().BeEmpty(url + ": 404 without a body, before the parameter is parsed");
        }
        foreach (var role in new[] { "owner", "shop-owner", "shop-staff", "shop-customer", "garbage" })
        {
            var r = await AnonymousClient().PostAsJsonAsync("/api/demo/login", new { role });
            r.StatusCode.Should().Be(HttpStatusCode.NotFound, role);
            (await r.Content.ReadAsStringAsync()).Should().BeEmpty(role);
        }

        var shop = await CreateShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var owner = AuthedClient(shop.OwnerToken);

        // the slug of an ordinary shop still changes
        var newSlug = Unique("renamed-");
        var slug = await owner.PutJsonAsync($"/api/shops/{shop.Id}/slug", new ShopSlugInput(newSlug));
        slug.StatusCode.Should().Be(HttpStatusCode.OK, await slug.Content.ReadAsStringAsync());
        slug.Headers.Contains("X-Demo-Restricted").Should().BeFalse();

        // a staff member of an ordinary shop can be removed by the owner
        var members = await J(await owner.GetAsync($"/api/companies/{shop.Id}/members"));
        var memberId = members.EnumerateArray().First(m => m.GetProperty("userId").GetString() == staff.UserId).GetProperty("id").GetGuid();
        var delete = await owner.DeleteAsync($"/api/Companies/{shop.Id}/members/{memberId}");
        delete.IsSuccessStatusCode.Should().BeTrue(await delete.Content.ReadAsStringAsync());
        delete.Headers.Contains("X-Demo-Restricted").Should().BeFalse();

        // an ordinary host has no noindex on its answers and no demo header anywhere
        var health = await AnonymousClient().GetAsync("/api/health/ready");
        health.Headers.Contains("X-Robots-Tag").Should().BeFalse();
        (await owner.PostAsync("/api/staff-max/link-sessions", null)).Headers.Contains("X-Demo-Restricted").Should().BeFalse();
    }

    private async Task MarkShowcaseAsync(Guid shopId) => await DbAsync(async db =>
    {
        var company = await db.Companies.FirstAsync(c => c.Id == shopId);
        company.IsShowcase = true;
        await db.SaveChangesAsync();
    });

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task DbAsync(Func<AppDbContext, Task> action) => await DbAsync(async db => { await action(db); return 0; });

    /// <summary>Connected, funded channel of the owner's account: what makes a REAL shop send a message to a customer (the same setup as CY24-61).</summary>
    private async Task ConnectMessengerAsync(ShopCtx shop)
    {
        await DbAsync(async db =>
        {
            var company = await db.Companies.AsNoTracking().SingleAsync(c => c.Id == shop.Id);
            var accountId = company.BillingAccountId ?? (await db.BillingAccounts.SingleAsync(a => a.OwnerUserId == shop.Owner.UserId)).Id;
            var sub = await db.AccountSubscriptions.Include(s => s.PlanConfig).SingleAsync(s => s.OwnerUserId == shop.Owner.UserId);
            sub.PlanConfig!.AllowNotificationChannel = true;
            await NotificationTestBase.EnsureWhatsAppPlanRuleAsync(db, sub.PlanConfigId!.Value);
            await db.SaveChangesAsync();
            await NotificationTestBase.EnsureWhatsAppPaidAsync(db, accountId);
            var channel = new NotificationChannel
            {
                Id = Guid.NewGuid(), OwnerUserId = shop.Owner.UserId, BillingAccountId = accountId, State = ChannelState.Connected,
                PhoneNumber = UniquePhone().TrimStart('+'), ProviderInstanceId = Unique("instance"),
                ConnectedAtUtc = DateTime.UtcNow.AddDays(-1), RiskAcceptedAtUtc = DateTime.UtcNow.AddDays(-1),
            };
            channel.ProviderSecretCiphertext = SecretProtector.Encrypt("test-provider-token", NotificationDispatchTestFactory.TestEncryptionKeyBase64, channel.Id);
            db.NotificationChannels.Add(channel);
            await db.SaveChangesAsync();
        });
        var assign = await DbAsync(db => db.NotificationChannels.Where(c => c.OwnerUserId == shop.Owner.UserId).Select(c => c.Id).FirstAsync());
        var r = await AuthedClient(shop.OwnerToken).PostJsonAsync($"/api/notification-channels/{assign}/companies", new { companyId = shop.Id, warningAcknowledged = true });
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        (await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/notification-settings",
            new ShopNotificationSettingsInput(true, true, true, null, null))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed record Queues(int Messenger, int StaffPush, int CustomerPush);

    private Task<Queues> QueuesAsync(Guid shopId) => DbAsync(async db => new Queues(
        await db.OutboundNotifications.CountAsync(n => n.CompanyId == shopId),
        await db.StaffPushNotifications.CountAsync(n => n.CompanyId == shopId),
        await db.CustomerOrderPushNotifications.CountAsync(n => n.CompanyId == shopId)));

    /// <summary>Every path of the order life in one run, with every sender on: messenger to the customer, push to staff, push to the customer of an order.</summary>
    private async Task<(Queues Queues, ShopCtx Shop)> RunOrderFlowAsync(bool showcase)
    {
        var shop = await CreateShopAsync();
        var staff = await AddShopStaffAsync(shop);
        await ConnectMessengerAsync(shop);
        if (showcase) await MarkShowcaseAsync(shop.Id);
        var product = await CreateProductAsync(shop, "Кофе", 200m);

        await using var push = new PushEnabledFactory(ConnectionString);
        HttpClient Push(string? token = null)
        {
            var c = push.CreateClient();
            if (token is not null) c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            return c;
        }
        foreach (var t in new[] { shop.OwnerToken, staff.Token })
            (await Push(t).PostJsonAsync("/api/push/subscriptions", new CreatePushSubscriptionInput(
                "https://push.example.test/cy35/" + Guid.NewGuid(), new CreatePushSubscriptionKeysInput("p", "a"), "d", CompanyKind.Orders))).IsSuccessStatusCode.Should().BeTrue();

        // guest order asking for messenger messages, with a web push subscription of the customer's order
        var r = await Push().PostJsonAsync($"/api/storefront/{shop.Slug}/orders",
            new CreateOrderInput(Guid.NewGuid(), [Line(product, 1)], "Покупатель", UniquePhone(), null, null, null, true));
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        var placed = (await r.Content.ReadJsonAsync<CreateOrderResponse>())!;
        await Push().PostJsonAsync($"/api/orders/public/{placed.Order.Token}/push-subscription", new OrderPushSubscribeInput(
            "https://push.example.test/cy35-customer/" + Guid.NewGuid(), new PushKeysInput("BKey-p256dh-" + Guid.NewGuid().ToString("N"), "auth-" + Guid.NewGuid().ToString("N")[..12]), "Chrome"));

        var order = await GetStaffOrderAsync(shop, placed.Order.Token);
        var accepted = await ActOkAsync(shop, order, "accept");
        var ready = await ActOkAsync(shop, accepted, "ready");
        await ActOkAsync(shop, ready, "issue");

        // a second order: cancelled by the customer; a third: rejected by the shop
        var second = await (await Push().PostJsonAsync($"/api/storefront/{shop.Slug}/orders",
            new CreateOrderInput(Guid.NewGuid(), [Line(product, 1)], "Покупатель", UniquePhone(), null, null, null, true))).Content.ReadJsonAsync<CreateOrderResponse>();
        (await Push().PostAsync($"/api/orders/public/{second!.Order.Token}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var third = await (await Push().PostJsonAsync($"/api/storefront/{shop.Slug}/orders",
            new CreateOrderInput(Guid.NewGuid(), [Line(product, 1)], "Покупатель", UniquePhone(), null, null, null, true))).Content.ReadJsonAsync<CreateOrderResponse>();
        await ActOkAsync(shop, await GetStaffOrderAsync(shop, third!.Order.Token), "reject", reason: "нет");
        return (await QueuesAsync(shop.Id), shop);
    }

    [Fact, TestCase("CY35-82")]
    public async Task UnmarkedShop_QueuesMessengerAndStaffPush_ShowcaseMarkedShop_QueuesNothingInAnyOfThem()
    {
        var control = await RunOrderFlowAsync(showcase: false);
        control.Queues.Messenger.Should().BeGreaterThan(0, "control: a real shop with a funded channel queues messages to customers");
        control.Queues.StaffPush.Should().BeGreaterThan(0, "control: a real shop queues push to staff");

        var marked = await RunOrderFlowAsync(showcase: true);
        marked.Queues.Should().Be(new Queues(0, 0, 0), "US-35-05: the showcase mark alone stops messenger, staff push and customer push, whatever the configuration");
    }
}
