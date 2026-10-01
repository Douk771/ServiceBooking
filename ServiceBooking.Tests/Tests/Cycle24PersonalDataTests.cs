using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 24, «Вызов 2»: персональные данные новых полей заказа — выгрузка, удаление аккаунта, сроки хранения
/// (API_CONTRACT_CYCLE24.md §488, SPEC §6 «Безопасность и ПДн»).
/// </summary>
public class Cycle24PersonalDataTests(TestDatabaseFixture fixture) : Cycle24TestBase(fixture)
{
    private async Task<(ShopCtx Shop, ProductDto Product)> ShopWithMessengerAsync()
    {
        var shop = await CreateScheduledShopAsync();
        var p = await CreateProductAsync(shop);
        return (shop, p);
    }

    [Fact, TestCase("CY24-90")]
    public async Task Export_ContainsPickupMessengerChoiceAndPushCounts_NeverEndpointsOrKeys()
    {
        var (shop, p) = await ShopWithMessengerAsync();
        var buyer = await RegisterAsync();
        var tomorrow = ShopToday(shop).AddDays(1);
        var placed = await PlaceOrderAsync(shop.Slug, GuestAt([Line(p, 1)], await SlotAsync(shop.Slug, tomorrow)), buyer.Token);

        var push = Fixture.ClassHost("push", cs => new PushEnabledFactory(cs));
        var endpoint = "https://push.example.test/cy24-export/" + Guid.NewGuid();
        var sub = await push.CreateClient().PostJsonAsync($"/api/orders/public/{placed.Order.Token}/push-subscription",
            new OrderPushSubscribeInput(endpoint, new PushKeysInput("p256dh-key", "auth-key"), null));
        sub.StatusCode.Should().Be(HttpStatusCode.Created, await sub.Content.ReadAsStringAsync());

        var export = await J(await AuthedClient(buyer.Token).GetAsync("/api/profile/export"));
        var order = export.GetProperty("orders").EnumerateArray().Single();
        var pickup = order.GetProperty("pickup");
        pickup.GetProperty("kind").GetString().Should().Be("Slot");
        pickup.GetProperty("date").GetString().Should().Be(D(tomorrow));
        pickup.GetProperty("text").GetString().Should().StartWith("Завтра, к ");
        order.GetProperty("notifyByMessenger").GetBoolean().Should().BeFalse();
        order.TryGetProperty("messengerConsentVersion", out _).Should().BeTrue("поле есть в выгрузке (значение null, пока текста согласия нет — [legal L9])");
        var subs = order.GetProperty("webPushSubscriptions");
        subs.GetProperty("count").GetInt32().Should().Be(1);
        subs.GetProperty("createdAtUtc").GetArrayLength().Should().Be(1);
        export.GetRawText().Should().NotContain(endpoint).And.NotContain("p256dh-key").And.NotContain("auth-key");
    }

    [Fact, TestCase("CY24-91")]
    public async Task DeleteAccount_RemovesOrderPushSubscriptions_ResetsMessengerFlag_KeepsConsentSnapshot()
    {
        var (shop, p) = await ShopWithMessengerAsync();
        var buyer = await RegisterAsync();
        var slot = await SlotAsync(shop.Slug, ShopToday(shop).AddDays(1));
        var placed = await PlaceOrderAsync(shop.Slug, GuestAt([Line(p, 1)], slot), buyer.Token);

        var push = Fixture.ClassHost("push", cs => new PushEnabledFactory(cs));
        (await push.CreateClient().PostJsonAsync($"/api/orders/public/{placed.Order.Token}/push-subscription",
            new OrderPushSubscribeInput("https://push.example.test/cy24-del/" + Guid.NewGuid(), new PushKeysInput("k1", "k2"), null)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // выбор мессенджера и снимок согласия проставляются оформлением, когда магазин мессенджер предлагает; здесь — прямая пометка в БД,
        // чтобы проверить именно обезличивание (сам выбор покрыт CY24-67).
        Guid orderId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var o = await db.Orders.SingleAsync(x => x.PublicToken == placed.Order.Token);
            o.NotifyByMessenger = true;
            o.MessengerConsentAtUtc = DateTime.UtcNow;
            o.MessengerConsentVersion = "v-test";
            await db.SaveChangesAsync();
            orderId = o.Id;
        }

        var del = await AuthedClient(buyer.Token).PostAsJsonAsync("/api/profile/delete-account", new { currentPassword = "Password123!" });
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope2 = Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db2.OrderPushSubscriptions.CountAsync(s => s.OrderId == orderId)).Should().Be(0, "подписки браузера удалены вместе с аккаунтом");
        var after = await db2.Orders.AsNoTracking().SingleAsync(x => x.Id == orderId);
        after.NotifyByMessenger.Should().BeFalse("выбор канала сброшен");
        after.MessengerConsentVersion.Should().Be("v-test", "снимок согласия — правовые данные, не удаляются");
        after.MessengerConsentAtUtc.Should().NotBeNull();
        (await GetPublicOrderAsync(placed.Order.Token)).Notifications.MessengerRequested.Should().BeFalse();
    }

    [Fact, TestCase("CY24-92")]
    public async Task RetentionPolicy_ExposesNewOrderPushPeriods()
    {
        var admin = AuthedClient((await LoginAsSuperAdminAsync()).Token);
        var r = await admin.GetAsync("/api/admin/retention/policy");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var j = await J(r);
        // Контракт §488 называет правила «order-push-subscriptions»/«customer-order-push-notifications»; фактически политика отдаёт поля *Days.
        j.GetProperty("orderPushSubscriptionDays").GetInt32().Should().Be(7, "подписки заказа — 7 дней после конечного статуса");
        j.GetProperty("customerOrderPushNotificationDays").GetInt32().Should().Be(90, "очередь web-push покупателям — 90 дней");
    }
}
