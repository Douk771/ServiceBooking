using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Baths;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Baths;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Цикл 42, «Бани»: напоминание перед сеансом (US-42-23, CY42-90…93; ARCHITECTURE_CYCLE42.md §42.9.5, API_CONTRACT_CYCLE42.md §42.36.4) — третий проход задачи
/// <c>stays-scheduled-messages</c>: однократность, окно момента, «создан позже», выключенная настройка, push без ПДн, блок на странице брони; метка полосы «Дня»
/// и ручная бронь с <c>guestsCount</c> (§42.32). Часы — <see cref="StaysTestFactory.StaysClock"/>.
/// </summary>
public class Cycle42ReminderTests(TestDatabaseFixture fixture) : Cycle39TestBase(fixture)
{
    private sealed record Scene(AuthResponseDto Owner, string OwnerToken, Guid CompanyId, Guid ServiceId);

    private async Task<Scene> BathAsync()
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

    /// <summary>Бронь с предоплатой: создание → чек → подтверждение оплаты персоналом (часы — как выставлены у хоста). Возвращает токен брони.</summary>
    private async Task<string> ConfirmedBookingAsync(StaysTestFactory host, Scene s, DateOnly date, int startMinute, int hours = 2, string guestName = "Анна Гость")
    {
        var guest = host.Client();
        var quote = await guest.PostJsonAsync($"/api/baths/public/services/{s.ServiceId}/quote", new { businessDate = date, startMinute, hours, items = Array.Empty<object>() });
        quote.StatusCode.Should().Be(HttpStatusCode.OK, await quote.Content.ReadAsStringAsync());
        var total = (await J(quote)).GetProperty("totalRub").GetInt32();
        var created = await guest.PostJsonAsync($"/api/baths/public/services/{s.ServiceId}/orders", new
        {
            businessDate = date, startMinute, hours, items = Array.Empty<object>(), guestsCount = 4, guestName, guestPhone = UniquePhone(), comment = (string?)null,
            notifyByMessenger = false, expectedTotalRub = total, idempotencyKey = Guid.NewGuid(), captchaToken = (string?)null
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var token = (await J(created)).GetProperty("token").GetString()!;
        (await guest.PostJsonAsync($"/api/baths/service-orders/public/{token}/push-subscription",
            new PushSubscriptionInput($"https://push.example.test/cy42-guest/{Guid.NewGuid():N}", new PushKeysInput("k1", "k2"), null))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await guest.PostAsync($"/api/baths/service-orders/public/{token}/payment-proofs", FileContent(TestImages.SolidJpeg(60, 40), "image/jpeg", "check.jpg")))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var sessionId = await SessionIdOfOrderAsync(token);
        var card = await J(await AuthedClient(s.OwnerToken).GetAsync($"/api/baths/companies/{s.CompanyId}/service-sessions/{sessionId}"));
        var confirm = await host.Client(s.OwnerToken).PostJsonAsync($"/api/baths/companies/{s.CompanyId}/service-sessions/{sessionId}/confirm-payment",
            new ExpectedVersionInput(card.GetProperty("version").GetInt32()));
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());
        return token;
    }

    private Task SetReminderHoursAsync(Guid companyId, int? hours) => WithDbAsync(async db =>
    {
        var settings = await db.StaysSettings.SingleAsync(x => x.CompanyId == companyId);
        settings.ServiceReminderHours = hours;
        await db.SaveChangesAsync();
    });

    private async Task<(DateTime? MarkedAt, int Events, List<StayGuestPushNotification> Pushes)> StateAsync(string token)
    {
        var orderId = await OrderIdAsync(token);
        return await WithDbAsync(async db =>
        {
            var order = await db.StayServiceOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            var events = await db.StayServiceOrderEvents.AsNoTracking().CountAsync(e => e.StayServiceOrderId == orderId && e.Kind == StayServiceOrderEventKind.SessionReminderSent);
            var pushes = await db.StayGuestPushNotifications.AsNoTracking().Where(n => n.StayServiceOrderId == orderId && n.Type == NotificationType.ServiceGuestSessionReminder).ToListAsync();
            return (order.SessionReminderAtUtc, events, pushes);
        });
    }

    /// <summary>Начало сеанса по поясу компании: он определяется её городом, а не константой тестов.</summary>
    private async Task<DateTime> StartOfAsync(Scene s, DateOnly date, int minute)
    {
        var tz = await WithDbAsync(db => db.Companies.AsNoTracking().Where(c => c.Id == s.CompanyId).Select(c => c.TimeZoneId).SingleAsync());
        return BusinessClock.ToUtc(tz, date, minute);
    }

    private async Task<JsonElement> PageAsync(StaysTestFactory host, string token) => await J(await host.Client().GetAsync($"/api/baths/service-orders/public/{token}"));

    // ── CY42-90: отправка в срок, однократность, push без ПДн, блок на странице ───

    [Fact, TestCase("CY42-90")]
    public async Task Reminder_IsSentOnceAtTheMoment_PushWithoutPersonalData_BlockOnThePage()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var s = await BathAsync();
        var date = InDays(4);
        var token = await ConfirmedBookingAsync(host, s, date, 900); // старт 15:00, за 3 часа — 12:00
        (await PageAsync(host, token)).TryGetProperty("sessionReminder", out _).Should().BeFalse("до отправки блока нет");
        var start = await StartOfAsync(s, date, 900);

        host.StaysClock.Set(start.AddHours(-3).AddMinutes(1));
        await host.RunTaskAsync("stays-scheduled-messages");
        var first = await StateAsync(token);
        first.MarkedAt.Should().NotBeNull();
        first.Events.Should().Be(1);
        first.Pushes.Should().ContainSingle();
        var push = JsonDocument.Parse(first.Pushes[0].Payload).RootElement;
        push.GetProperty("title").GetString().Should().Be("EZBOOK Бани");
        push.GetProperty("body").GetString().Should().Be("Скоро ваш сеанс — откройте бронь");
        push.GetProperty("url").GetString().Should().Be($"/s/{token}");
        push.GetProperty("body").GetString().Should().NotContain("Анна").And.NotContain("http");

        var block = (await PageAsync(host, token)).GetProperty("sessionReminder");
        block.GetProperty("text").GetString().Should().Contain("напоминаем о брони").And.Contain("(время местное,").And.NotContain("http").And.NotContain("Анна");

        host.StaysClock.Set(start.AddHours(-2));
        await host.RunTaskAsync("stays-scheduled-messages");
        var second = await StateAsync(token);
        second.Events.Should().Be(1, "однократно");
        second.Pushes.Should().ContainSingle();
        second.MarkedAt.Should().Be(first.MarkedAt);
    }

    // ── CY42-91: не раньше момента, не после начала, выключено ────────────────────

    [Fact, TestCase("CY42-91")]
    public async Task Reminder_IsNotSent_BeforeTheMoment_AfterTheStart_OrWhenSwitchedOff()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var s = await BathAsync();
        var date = InDays(4);
        var early = await ConfirmedBookingAsync(host, s, date, 900);
        var late = await ConfirmedBookingAsync(host, s, date, 1140);
        var start = await StartOfAsync(s, date, 900);

        host.StaysClock.Set(start.AddHours(-4));
        await host.RunTaskAsync("stays-scheduled-messages");
        (await StateAsync(early)).MarkedAt.Should().BeNull("момент ещё не наступил");

        host.StaysClock.Set(start.AddMinutes(10));
        await host.RunTaskAsync("stays-scheduled-messages");
        (await StateAsync(early)).MarkedAt.Should().BeNull("сеанс уже начался");

        await SetReminderHoursAsync(s.CompanyId, null);
        host.StaysClock.Set((await StartOfAsync(s, date, 1140)).AddHours(-3).AddMinutes(1));
        await host.RunTaskAsync("stays-scheduled-messages");
        var off = await StateAsync(late);
        off.MarkedAt.Should().BeNull("у компании напоминание выключено");
        off.Events.Should().Be(0);
        (await PageAsync(host, late)).TryGetProperty("sessionReminder", out _).Should().BeFalse();
    }

    // ── CY42-92: бронь, созданная позже момента ───────────────────────────────────

    [Fact, TestCase("CY42-92")]
    public async Task Reminder_IsNotSent_ForABookingCreatedAfterTheMoment()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var s = await BathAsync();
        var date = InDays(4);
        var start = await StartOfAsync(s, date, 900);
        host.StaysClock.Set(start.AddHours(-2)); // момент 12:00, бронь создаётся в 13:00
        var token = await ConfirmedBookingAsync(host, s, date, 900);

        host.StaysClock.Set(start.AddHours(-1));
        await host.RunTaskAsync("stays-scheduled-messages");
        var state = await StateAsync(token);
        state.MarkedAt.Should().BeNull();
        state.Events.Should().Be(0);
        state.Pushes.Should().BeEmpty();
    }

    // ── CY42-93: метка полосы «Дня» и ручная бронь с числом гостей ────────────────

    [Fact, TestCase("CY42-93")]
    public async Task ServiceDay_LabelsABarWithNameAndGuests_AndManualBookingTakesGuestsCount()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var s = await BathAsync();
        var date = InDays(5);
        var token = await ConfirmedBookingAsync(host, s, date, 720, guestName: "Анна Гость");
        var client = AuthedClient(s.OwnerToken);

        var day = await J(await client.GetAsync($"/api/baths/companies/{s.CompanyId}/service-day?date={date:yyyy-MM-dd}"));
        var labels = day.GetProperty("services").EnumerateArray().SelectMany(x => x.GetProperty("bars").EnumerateArray())
            .Where(b => b.GetProperty("kind").GetString() == "Session").Select(b => b.GetProperty("label").GetString()).ToList();
        labels.Should().Contain("Анна Гость, 4 чел.");

        object Manual(int? guests, int minute) => new
        {
            serviceId = s.ServiceId, businessDate = date, startMinute = minute, hours = 2, items = Array.Empty<object>(), guestName = "Пётр", guestPhone = (string?)null, comment = (string?)null,
            requestBasis = "Phone", idempotencyKey = Guid.NewGuid(), guestsCount = guests
        };
        var url = $"/api/baths/companies/{s.CompanyId}/service-sessions";
        var tooMany = await client.PostJsonAsync(url, Manual(7, 1020));
        tooMany.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await tooMany.Content.ReadAsStringAsync()).Should().Contain("от 1 до 6");

        var ok = await client.PostJsonAsync(url, Manual(3, 1020));
        ok.StatusCode.Should().Be(HttpStatusCode.Created, await ok.Content.ReadAsStringAsync());
        var card = await J(ok);
        card.GetProperty("guestsCount").GetInt32().Should().Be(3);
        card.GetProperty("isManual").GetBoolean().Should().BeTrue();

        var none = await client.PostJsonAsync(url, Manual(null, 1260));
        none.StatusCode.Should().Be(HttpStatusCode.Created, await none.Content.ReadAsStringAsync());
        (await J(none)).TryGetProperty("guestsCount", out _).Should().BeFalse("необязателен для персонала");
        _ = token;
    }
}
