using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 25, «Вызов 2»: политики частоты новых маршрутов (API_CONTRACT_CYCLE25.md §521, §537): <c>shop-reports</c> — на пользователя,
/// <c>goods-catalog</c> — на адрес, <c>staff-max-link</c> — на пользователя. Обычный тестовый хост поднимает все лимиты до 10000 в минуту, поэтому
/// здесь отдельный хост с урезанными значениями (как <see cref="RateLimitTestFactory"/> для остальных политик).
/// </summary>
public class Cycle25RateLimitTests(TestDatabaseFixture fixture) : Cycle25TestBase(fixture)
{
    private sealed class TightHost(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            TestHostSettings.Apply(builder, "api", connectionString);
            builder.UseSetting("RateLimits:shop-reports:PermitLimit", "3");
            builder.UseSetting("RateLimits:goods-catalog:PermitLimit", "3");
            builder.UseSetting("RateLimits:staff-max-link:PermitLimit", "2");
        }
    }

    [Fact, TestCase("CY25-70")]
    public async Task NewRoutes_AnswerWith429AndTheContractString_LimitIsPerUser_ForReportsAndLink_PerAddress_ForCatalog()
    {
        var shop = await CreateRoundClockShopAsync();
        var staff = await AddShopStaffAsync(shop);
        await using var host = new TightHost(ConnectionString);

        async Task<HttpResponseMessage> Report(string token) => await ClientOn(host, token).PostJsonAsync($"/api/shops/{shop.Id}/order-history", new { });
        for (var i = 0; i < 3; i++) (await Report(shop.OwnerToken)).StatusCode.Should().Be(HttpStatusCode.OK, $"запрос {i + 1} из 3 допустим");
        var limited = await Report(shop.OwnerToken);
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await limited.Content.ReadAsStringAsync()).Should().Contain("Слишком много запросов — подождите минуту");
        (await Report(staff.Token)).StatusCode.Should().Be(HttpStatusCode.OK, "счётчик — на пользователя: коллега не заблокирован");
        // общий счётчик на все маршруты отчётов пользователя
        (await ClientOn(host, shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/picklist")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // ссылка подключения MAX: 2 запроса, третий — 429 (платформа выключена — 409, но лимит срабатывает раньше)
        var link = ClientOn(host, shop.OwnerToken);
        (await link.PostAsync("/api/staff-max/link-sessions", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await link.PostAsync("/api/staff-max/link-sessions", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var third = await link.PostAsync("/api/staff-max/link-sessions", null);
        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await third.Content.ReadAsStringAsync()).Should().Contain("Слишком много запросов — подождите минуту");
        (await ClientOn(host, staff.Token).PostAsync("/api/staff-max/link-sessions", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // каталог: анонимно, лимит на адрес
        var anon = host.CreateClient();
        for (var i = 0; i < 3; i++) (await anon.GetAsync("/api/goods/catalog")).StatusCode.Should().Be(HttpStatusCode.OK);
        var catalog = await anon.GetAsync("/api/goods/catalog");
        catalog.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await catalog.Content.ReadAsStringAsync()).Should().Contain("Слишком много запросов — подождите минуту");
    }
}
