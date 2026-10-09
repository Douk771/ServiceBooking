using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Baths;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Baths;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Цикл 42, «Бани»: после окончания триала или тарифа управление принятыми бронями доступно (Т42-10, CY42-60…63; ARCHITECTURE_CYCLE42.md §42.5.7,
/// API_CONTRACT_CYCLE42.md §42.32). Гейт проверяется только при публичном создании брони, расчёте и публикации; подтверждение оплаты, отмена, «День услуг»
/// и расписание банщика от тарифа не зависят, а публичное создание брони — закрыто.
/// <para>
/// ВНИМАНИЕ: сценарии идут через публичные маршруты брони (<c>/api/baths/public/…</c>, <c>/api/baths/service-orders/public/…</c>) и маршруты сеансов персонала
/// (<c>/api/baths/companies/{id}/service-sessions…</c>, <c>service-day</c>) — их реализует BE-42-4. На момент написания (ветка BE-42-I) этих контроллеров нет, поэтому
/// тесты помечены Skip и не выполнялись; снять Skip после влития BE-42-4 и прогнать (<see cref="SkipUntilBe424"/>).
/// </para>
/// </summary>
public class Cycle42AfterTrialTests(TestDatabaseFixture fixture) : Cycle39TestBase(fixture)
{
    private const string SkipUntilBe424 = "Нужны публичные маршруты брони и маршруты сеансов персонала «Бань» (BE-42-4): снять Skip после влития и прогнать.";

    private sealed record Scene(AuthResponseDto Owner, string OwnerToken, Guid CompanyId, Guid ServiceId);

    private async Task<Scene> AcceptingBathAsync()
    {
        var owner = await RegisterAsync();
        await MarkPhoneVerifiedAsync(owner.Phone, owner.UserId);
        var created = await (await AuthedClient(owner.Token).PostJsonAsync("/api/baths/companies", new
        {
            name = Unique("Баня "), slug = Unique("bn-").ToLowerInvariant(), cityId = await AnyCityIdAsync(), address = "Шерегеш, ул. Лесная, 5", phone = "+79001112233",
            description = (string?)null, ownerTermsVersion = CurrentOwnerTermsDto().Version, trialTermsVersion = BathsTrialTerms.Version
        })).Content.ReadJsonAsync<BathsCompanyCreatedDto>();
        created!.Trial!.Granted.Should().BeTrue(created.Trial.Message);
        var token = created.Token;
        var id = created.Company.Id;
        var client = AuthedClient(token);

        (await client.PutJsonAsync($"/api/baths/companies/{id}/provider",
            new ProviderInput(StayProviderStatus.SelfEmployed, "Иванов Иван Иванович", ValidPersonInn, null, "г. Новокузнецк, ул. Мира, 1"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutJsonAsync($"/api/baths/companies/{id}/payment-details", new PaymentDetailsDto(PaymentDetailsText, "Бронь бани"))).StatusCode.Should().Be(HttpStatusCode.OK);

        var baseUrl = $"/api/baths/companies/{id}/services";
        var svc = (await (await client.PostJsonAsync(baseUrl, new ServiceCreateInput("Русская баня"))).Content.ReadJsonAsync<ServiceManageDto>())!;
        (await client.PutJsonAsync($"{baseUrl}/{svc.Id}/setup", new ServiceSetupInput("Русская баня", Unique("res-").ToLowerInvariant(), 2, 6, 60, 30, false, 0, 30,
            StayServiceCancellationPolicy.NoDeductions, 12, false, Capacity: 6))).StatusCode.Should().Be(HttpStatusCode.OK);
        var days = Enumerable.Range(1, 7).Select(d => new WeeklyDayInput(d, [new ServiceWindowInput(DefaultWindow.Start, DefaultWindow.End)])).ToList();
        (await client.PutJsonAsync($"{baseUrl}/{svc.Id}/weekly-schedule", new WeeklyScheduleInput(days))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostJsonAsync($"{baseUrl}/{svc.Id}/price-rules", new PriceRuleInput(127, 6, 30, 2000))).StatusCode.Should().Be(HttpStatusCode.Created);
        var publish = await client.PostJsonAsync($"{baseUrl}/{svc.Id}/publish", new { });
        publish.StatusCode.Should().Be(HttpStatusCode.OK, await publish.Content.ReadAsStringAsync());
        return new Scene(owner, token, id, svc.Id);
    }

    private Task ExpireTrialAsync(Guid companyId) => WithDbAsync(async db =>
    {
        var accountId = await db.Companies.Where(c => c.Id == companyId).Select(c => c.BillingAccountId!.Value).SingleAsync();
        var sub = await db.BathsSubscriptions.SingleAsync(s => s.BillingAccountId == accountId);
        sub.PaidUntil = DateTime.UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync();
    });

    /// <summary>Бронь с предоплатой: расчёт → создание → чек. Статус после чека — AwaitingPaymentCheck.</summary>
    private async Task<(string Token, Guid SessionId)> BookedWithProofAsync(StaysTestFactory host, Scene s, DateOnly date, int startMinute = 720)
    {
        var guest = host.Client();
        var quote = await guest.PostJsonAsync($"/api/baths/public/services/{s.ServiceId}/quote", new { businessDate = date, startMinute, hours = 2, items = Array.Empty<object>() });
        quote.StatusCode.Should().Be(HttpStatusCode.OK, await quote.Content.ReadAsStringAsync());
        var total = (await J(quote)).GetProperty("totalRub").GetInt32();
        var created = await guest.PostJsonAsync($"/api/baths/public/services/{s.ServiceId}/orders", new
        {
            businessDate = date, startMinute, hours = 2, items = Array.Empty<object>(), guestsCount = 4, guestName = "Анна Гость", guestPhone = UniquePhone(), comment = (string?)null,
            notifyByMessenger = false, expectedTotalRub = total, idempotencyKey = Guid.NewGuid(), captchaToken = (string?)null
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var token = (await J(created)).GetProperty("token").GetString()!;
        var proof = await guest.PostAsync($"/api/baths/service-orders/public/{token}/payment-proofs", FileContent(TestImages.SolidJpeg(60, 40), "image/jpeg", "check.jpg"));
        proof.StatusCode.Should().Be(HttpStatusCode.Created, await proof.Content.ReadAsStringAsync());
        return (token, await SessionIdOfOrderAsync(token));
    }

    private async Task<JsonElement> SessionCardAsync(Scene s, Guid sessionId, string? token = null)
    {
        var r = await AuthedClient(token ?? s.OwnerToken).GetAsync($"/api/baths/companies/{s.CompanyId}/service-sessions/{sessionId}");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return await J(r);
    }

    private Task<HttpResponseMessage> ActionAsync(Scene s, Guid sessionId, string action, int version, string? reason = null, string? token = null) =>
        AuthedClient(token ?? s.OwnerToken).PostJsonAsync($"/api/baths/companies/{s.CompanyId}/service-sessions/{sessionId}/{action}",
            action == "confirm-payment" ? (object)new ExpectedVersionInput(version) : new ExpectedVersionReasonInput(version, reason));

    private async Task<string> AddStaffTokenAsync(Scene s, string position)
    {
        var user = await RegisterAsync();
        var r = await AuthedClient(s.OwnerToken).PostJsonAsync($"/api/Companies/{s.CompanyId}/members",
            new { phone = user.Phone, firstName = user.FirstName, lastName = user.LastName, role = "Master", bio = (string?)null, email = (string?)null, position });
        r.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created], await r.Content.ReadAsStringAsync());
        return (await LoginAsync(user.Phone, "Password123!")).Token;
    }

    // ── CY42-60: подтверждение оплаты после конца триала ─────────────────────────

    [Fact(Skip = SkipUntilBe424), TestCase("CY42-60")]
    public async Task AfterTrial_OwnerConfirmsPayment_OfAnAlreadyAcceptedBooking()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var s = await AcceptingBathAsync();
        var (token, sessionId) = await BookedWithProofAsync(host, s, InDays(9));
        var card = await SessionCardAsync(s, sessionId);
        card.GetProperty("orderStatus").GetString().Should().Be("AwaitingPaymentCheck");

        await ExpireTrialAsync(s.CompanyId);
        var company = await J(await AuthedClient(s.OwnerToken).GetAsync($"/api/baths/companies/{s.CompanyId}"));
        company.GetProperty("gate").GetProperty("accepting").GetBoolean().Should().BeFalse();
        company.GetProperty("plan").GetProperty("warningLevel").GetString().Should().Be("Expired");

        var ok = await ActionAsync(s, sessionId, "confirm-payment", card.GetProperty("version").GetInt32());
        ok.StatusCode.Should().Be(HttpStatusCode.OK, "подтверждение оплаты принятой брони от тарифа не зависит: " + await ok.Content.ReadAsStringAsync());
        (await J(ok)).GetProperty("orderStatus").GetString().Should().Be("Confirmed");
        var page = await host.Client().GetAsync($"/api/baths/service-orders/public/{token}");
        page.StatusCode.Should().Be(HttpStatusCode.OK, "страница брони по ссылке остаётся");
        (await J(page)).GetProperty("status").GetString().Should().Be("Confirmed");
    }

    // ── CY42-61: отмена и отклонение; администратор тоже ─────────────────────────

    [Fact(Skip = SkipUntilBe424), TestCase("CY42-61")]
    public async Task AfterTrial_OwnerCancels_AndAdministratorRejects_TimeIsFreed_RefundIsFull()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var s = await AcceptingBathAsync();
        var admin = await AddStaffTokenAsync(s, "Manager");
        var date = InDays(10);
        var first = await BookedWithProofAsync(host, s, date, 720);
        var second = await BookedWithProofAsync(host, s, date, 1080);
        await ExpireTrialAsync(s.CompanyId);

        var cardFirst = await SessionCardAsync(s, first.SessionId);
        var cancelled = await ActionAsync(s, first.SessionId, "cancel", cardFirst.GetProperty("version").GetInt32(), "Авария водоснабжения");
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        var after = await J(await host.Client().GetAsync($"/api/baths/service-orders/public/{first.Token}"));
        after.GetProperty("status").GetString().Should().Be("CancelledByOwner");
        after.GetProperty("statusReason").GetString().Should().Be("Авария водоснабжения");

        var cardSecond = await SessionCardAsync(s, second.SessionId, admin);
        cardSecond.GetProperty("availableActions").EnumerateArray().Select(a => a.GetString()).Should().Contain(["ConfirmPayment", "RejectPayment"]);
        var rejected = await ActionAsync(s, second.SessionId, "reject-payment", cardSecond.GetProperty("version").GetInt32(), "Чек не читается", admin);
        rejected.StatusCode.Should().Be(HttpStatusCode.OK, "администратор управляет бронями после конца тарифа: " + await rejected.Content.ReadAsStringAsync());

        (await ActiveSessionsAsync(s.ServiceId)).Should().NotContain(x => x.Id == first.SessionId);
    }

    // ── CY42-62: публичное создание брони закрыто ────────────────────────────────

    [Fact(Skip = SkipUntilBe424), TestCase("CY42-62")]
    public async Task AfterTrial_PublicBookingIsClosed_QuoteSaysNotAccepting_ButThePagesStay()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var s = await AcceptingBathAsync();
        var date = InDays(11);
        var guest = host.Client();
        var company = await J(await AuthedClient(s.OwnerToken).GetAsync($"/api/baths/companies/{s.CompanyId}"));
        var slug = company.GetProperty("slug").GetString()!;
        var open = await guest.PostJsonAsync($"/api/baths/public/services/{s.ServiceId}/quote", new { businessDate = date, startMinute = 720, hours = 2, items = Array.Empty<object>() });
        (await J(open)).GetProperty("acceptingBookings").GetBoolean().Should().BeTrue();
        var total = (await J(open)).GetProperty("totalRub").GetInt32();

        await ExpireTrialAsync(s.CompanyId);
        var closed = await guest.PostJsonAsync($"/api/baths/public/services/{s.ServiceId}/quote", new { businessDate = date, startMinute = 720, hours = 2, items = Array.Empty<object>() });
        (await J(closed)).GetProperty("acceptingBookings").GetBoolean().Should().BeFalse();

        var order = await guest.PostJsonAsync($"/api/baths/public/services/{s.ServiceId}/orders", new
        {
            businessDate = date, startMinute = 720, hours = 2, items = Array.Empty<object>(), guestsCount = 4, guestName = "Анна Гость", guestPhone = UniquePhone(), comment = (string?)null,
            notifyByMessenger = false, expectedTotalRub = total, idempotencyKey = Guid.NewGuid(), captchaToken = (string?)null
        });
        order.StatusCode.Should().Be(HttpStatusCode.Conflict, await order.Content.ReadAsStringAsync());
        (await J(order)).GetProperty("reasonCode").GetString().Should().Be("NoPlan");

        // страницы остаются видимыми
        (await guest.GetAsync($"/api/baths/public/companies/{slug}")).StatusCode.Should().Be(HttpStatusCode.OK);
        // публикация нового ресурса — по-прежнему 402
        var baseUrl = $"/api/baths/companies/{s.CompanyId}/services";
        var draft = (await (await AuthedClient(s.OwnerToken).PostJsonAsync(baseUrl, new ServiceCreateInput("Купель"))).Content.ReadJsonAsync<ServiceManageDto>())!;
        (await AuthedClient(s.OwnerToken).PostJsonAsync($"{baseUrl}/{draft.Id}/publish", new { })).StatusCode.Should().BeOneOf(HttpStatusCode.Conflict, (HttpStatusCode)402);
    }

    // ── CY42-63: «День услуг», расписание банщика и ревизия ──────────────────────

    [Fact(Skip = SkipUntilBe424), TestCase("CY42-63")]
    public async Task AfterTrial_ServiceDay_Schedule_AndRevision_StillShowTheAcceptedBooking()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var s = await AcceptingBathAsync();
        var housekeeper = await AddStaffTokenAsync(s, "Housekeeper");
        var date = InDays(12);
        var (_, sessionId) = await BookedWithProofAsync(host, s, date, 720);
        await ExpireTrialAsync(s.CompanyId);

        var owner = AuthedClient(s.OwnerToken);
        var day = await owner.GetAsync($"/api/baths/companies/{s.CompanyId}/service-day?date={date:yyyy-MM-dd}");
        day.StatusCode.Should().Be(HttpStatusCode.OK, await day.Content.ReadAsStringAsync());
        (await day.Content.ReadAsStringAsync()).Should().Contain(sessionId.ToString());

        var schedule = await AuthedClient(housekeeper).GetAsync($"/api/baths/companies/{s.CompanyId}/schedule?from={date:yyyy-MM-dd}&days=1");
        schedule.StatusCode.Should().Be(HttpStatusCode.OK, "расписание банщика от тарифа не зависит: " + await schedule.Content.ReadAsStringAsync());
        (await schedule.Content.ReadAsStringAsync()).Should().Contain("Русская баня");

        (await owner.GetAsync($"/api/baths/companies/{s.CompanyId}/revision")).StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await owner.GetAsync($"/api/baths/companies/{s.CompanyId}/service-sessions");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.Content.ReadAsStringAsync()).Should().Contain(sessionId.ToString());
        (await SessionCardAsync(s, sessionId)).GetProperty("id").GetGuid().Should().Be(sessionId);
    }
}
