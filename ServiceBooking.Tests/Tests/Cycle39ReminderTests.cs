using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 39, «Вызов 2»: напоминание накануне заезда (US-39-19, 20; ARCHITECTURE_CYCLE39.md §39.11; ЮР39-3/4/5). Время (08:00–22:00, шаг 30 минут), шаблон с подстановками,
/// запрещённые слова («задать вопрос» проходит, «задаток» нет), диалог 409 ReminderConfirmationRequired, жёсткий фильтр push, снимок на странице брони без каналов, дефолт.
/// </summary>
public class Cycle39ReminderTests(TestDatabaseFixture fixture) : Cycle39TestBase(fixture)
{
    private static string Url(StaysCtx c, string tail = "") => $"/api/stays/companies/{c.Id}/arrival-reminder{tail}";

    private static ArrivalReminderInput Input(string? time = "18:00", string? template = null, bool push = false, bool confirm = false, string? pushNotice = null) =>
        new(time, template, push, confirm, "v-test", pushNotice);

    private async Task<ArrivalReminderSettingsDto> GetAsync(StaysCtx c)
    {
        var r = await AuthedClient(c.OwnerToken).GetAsync(Url(c));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<ArrivalReminderSettingsDto>())!;
    }

    private Task<HttpResponseMessage> PutAsync(StaysCtx c, ArrivalReminderInput input) => AuthedClient(c.OwnerToken).PutJsonAsync(Url(c), input);

    private async Task<ArrivalReminderPreviewDto> PreviewAsync(StaysCtx c, string? template, bool push = true, Guid? bookingId = null)
    {
        var r = await AuthedClient(c.OwnerToken).PostJsonAsync(Url(c, "/preview"), new ArrivalReminderPreviewInput(template, push, bookingId));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<ArrivalReminderPreviewDto>())!;
    }

    // ── настройки ────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-110")]
    public async Task Defaults_Time1800_DefaultTemplate_PushOff_PlaceholderTable()
    {
        var company = await CreateStaysCompanyAsync();
        var s = await GetAsync(company);
        s.Time.Should().Be("18:00");
        s.Template.Should().BeNull();
        s.IsDefault.Should().BeTrue();
        s.EffectiveTemplate.Should().Be(s.DefaultTemplate);
        s.PushTextEnabled.Should().BeFalse("переключатель push по умолчанию выключен (ЮР39-3)");
        s.Limits.TemplateMaxLength.Should().Be(700);
        s.Limits.MessengerMaxLength.Should().Be(1000);
        s.Limits.PushMaxLength.Should().Be(180);
        // таблица подстановок и колонка «в push»
        var byToken = s.Placeholders.ToDictionary(p => p.Token);
        foreach (var t in new[] { "{Компания}", "{Дом}", "{ДатаЗаезда}", "{ДатаВыезда}", "{Ночей}", "{ВремяЗаезда}", "{ВремяВыезда}", "{Услуги}" })
            byToken[t].InPush.Should().BeTrue(t);
        foreach (var t in new[] { "{ИмяГостя}", "{Адрес}", "{ТелефонКомпании}", "{КОплатеПриЗаселении}", "{СсылкаНаБронь}" })
            byToken[t].InPush.Should().BeFalse($"{t} в push запрещена (Т37-08)");
        byToken["{СсылкаНаБронь}"].OnPage.Should().BeFalse("на странице брони гость уже на ней");
        s.OwnerNotice.Key.Should().NotBeNullOrWhiteSpace();
        s.DefaultTemplate.Should().Contain("{Компания}").And.Contain("{КОплатеПриЗаселении}");
        // хозяева компаний цикла 37: поведение не меняется
        (await GetCompanyAsync(company)).Settings!.ArrivalReminderEnabled.Should().BeTrue();
    }

    [Theory, TestCase("CY39-111")]
    [InlineData("08:00", true)]
    [InlineData("22:00", true)]
    [InlineData("12:30", true)]
    [InlineData("07:30", false)]
    [InlineData("22:30", false)]
    [InlineData("18:15", false)]
    [InlineData("24:00", false)]
    [InlineData("25:99", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public async Task Time_Between0800And2200_HalfHourStep(string? time, bool ok)
    {
        var company = await CreateStaysCompanyAsync();
        var r = await PutAsync(company, Input(time));
        if (ok)
        {
            r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
            (await GetAsync(company)).Time.Should().Be(time);
        }
        else
        {
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await r.Content.ReadAsStringAsync()).Should().Contain("с 08:00 до 22:00 с шагом 30 минут");
            (await GetAsync(company)).Time.Should().Be("18:00", "неудачное сохранение ничего не меняет");
        }
    }

    [Fact, TestCase("CY39-112")]
    public async Task Template_Validation_UnknownPlaceholder_Length_ForbiddenWords_AllowedPhrases()
    {
        var company = await CreateStaysCompanyAsync();
        async Task<(HttpStatusCode, string)> Try(string t) { var r = await PutAsync(company, Input(template: t, confirm: true)); return (r.StatusCode, await r.Content.ReadAsStringAsync()); }

        var unknown = await Try("Здравствуйте, {Имя}!");
        unknown.Item1.Should().Be(HttpStatusCode.BadRequest);
        unknown.Item2.Should().Contain("{Имя}").And.Contain("{ИмяГостя}", "перечень допустимых подстановок");
        (await Try("{Дом}: {дом}")).Item1.Should().Be(HttpStatusCode.BadRequest, "регистр подстановки важен");
        (await Try(new string('а', 701))).Item1.Should().Be(HttpStatusCode.BadRequest);
        (await Try(new string('а', 700))).Item1.Should().Be(HttpStatusCode.OK);
        foreach (var word in new[] { "задаток", "Задаток", "ЗАДАТОК", "задатка нет", "невозвратный платёж", "Невозвратная сумма", "депозит", "ДЕПОЗИТ" })
        {
            var r = await Try($"Привет! {word}.");
            r.Item1.Should().Be(HttpStatusCode.BadRequest, word);
            r.Item2.Should().Contain("задаток");
        }
        foreach (var ok in new[] { "Если хотите задать вопрос — пишите", "Задайте вопрос заранее", "Предоплата уже внесена", "Хорошей дороги!" })
            (await Try(ok)).Item1.Should().Be(HttpStatusCode.OK, ok);
        (await GetAsync(company)).Template.Should().Be("Хорошей дороги!");
        // шаблон равен дефолту → хранится как «не менялся»
        var s = await GetAsync(company);
        (await PutAsync(company, Input(template: s.DefaultTemplate))).StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await GetAsync(company);
        after.Template.Should().BeNull();
        after.IsDefault.Should().BeTrue();
        // пустой текст = вернуть дефолт
        await PutAsync(company, Input(template: "Привет"));
        (await PutAsync(company, Input(template: "   "))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetAsync(company)).IsDefault.Should().BeTrue();
    }

    [Fact, TestCase("CY39-113")]
    public async Task CodeMarkers_AskForConfirmation_409_ThenSavedWithHistoryMark()
    {
        var company = await CreateStaysCompanyAsync();
        var risky = new[] { "Код от калитки 1234", "Пароль от Wi-Fi: dom12", "Позвоните по домофону", "Ключница у входа", "Сейф в шкафу", "Номер брони 4567" };
        foreach (var line in risky)
        {
            var r = await PutAsync(company, Input(template: line));
            r.StatusCode.Should().Be(HttpStatusCode.Conflict, line);
            var body = (await r.Content.ReadJsonAsync<StaysServiceConflictDto>())!;
            body.Code.Should().Be("ReminderConfirmationRequired");
            body.Markers.Should().NotBeNullOrEmpty();
            body.NoticeText.Should().NotBeNullOrWhiteSpace("диалог показывает предупреждение");
            (await GetAsync(company)).Template.Should().BeNull($"«{line}»: без подтверждения ничего не сохраняется");
        }
        var template = "Код от калитки 1234. Ждём вас!";
        (await PutAsync(company, Input(template: template, confirm: true))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetAsync(company)).Template.Should().Be(template);
        // повторное сохранение того же текста (например, сменили только время) диалога не требует
        (await PutAsync(company, Input(time: "19:00", template: template))).StatusCode.Should().Be(HttpStatusCode.OK);

        var history = await AuthedClient(company.OwnerToken).GetAsync(Url(company, "/history"));
        history.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = (await history.Content.ReadJsonAsync<List<ArrivalReminderChangeDto>>())!;
        rows.Should().HaveCount(2);
        rows.Last().CodeMarkersConfirmed.Should().BeTrue();
        rows.Last().CodeMarkersHit.Should().Contain("1234");
        rows.Last().NewTemplate.Should().Be(template);
        rows.Last().PreviousTemplate.Should().BeNull();
        rows.Last().ChangedByName.Should().NotBeNullOrWhiteSpace("кто");
        rows.First().NewTime.Should().Be("19:00");
        rows.First().PreviousTime.Should().Be("18:00");
        rows.First().CodeMarkersConfirmed.Should().BeFalse();
    }

    [Fact, TestCase("CY39-114")]
    public async Task SoftWarnings_PassportCardCancellationTerms_AreWarningsNotErrors()
    {
        var company = await CreateStaysCompanyAsync();
        var p = await PreviewAsync(company, "Вернём без штрафа. Возьмите паспорт. Карта 2202 2000 1111 2222. Паспорт 1234 567890.");
        p.Warnings.Should().Contain([ReminderWarning.CancellationTermsInText, ReminderWarning.Passport, ReminderWarning.CardNumber, ReminderWarning.PassportNumber]);
        p.Errors.Should().BeEmpty("мягкие предупреждения не запрещают");
        p.ConfirmationRequired.Should().BeTrue("цифры похожи на код — диалог подтверждения");
        var save = await PutAsync(company, Input(template: "Возьмите паспорт и вернём без штрафа", confirm: false));
        save.StatusCode.Should().Be(HttpStatusCode.OK, "только предупреждения (паспорт/штраф), цифр нет — сохраняется без диалога");
        (await GetAsync(company)).Warnings.Should().Contain([ReminderWarning.Passport, ReminderWarning.CancellationTermsInText]);
    }

    // ── предпросмотр: три канала, фильтр push ────────────────────────────────────

    [Fact, TestCase("CY39-115")]
    public async Task Preview_ThreeChannels_Lengths_LinkLineOnlyInMessenger_AppendedWhenMissing()
    {
        var company = await CreateStaysCompanyAsync();
        var template = "Завтра заезд в «{Дом}».\nЗдравствуйте, {ИмяГостя}!\nАдрес: {Адрес}\nСвязь: {ТелефонКомпании}\nК оплате: {КОплатеПриЗаселении}\nСсылка: {СсылкаНаБронь}";
        var p = await PreviewAsync(company, template, push: true);
        p.Messenger.Text.Should().Contain("Анна").And.Contain("Дом у леса").And.Contain("ул. Лесная").And.Contain("12 500").And.Contain("https://dom.ezbook.ru/b/");
        p.Messenger.Length.Should().Be(p.Messenger.Text.Length);
        p.Page.Text.Should().Contain("Анна").And.NotContain("Ссылка:", "строка с {СсылкаНаБронь} на странице брони не выводится");
        p.Page.Text.Should().NotContain("https://");
        p.Push.UsesFixedText.Should().BeFalse();
        p.Push.Text.Should().Contain("Завтра заезд в «Дом у леса»").And.NotContain("Анна").And.NotContain("Лесная").And.NotContain("12 500").And.NotContain("+7").And.NotContain("https");
        p.Push.Dropped.Select(d => d.Reason).Should().OnlyContain(r => r == ReminderDropReason.ForbiddenPlaceholder);
        p.Push.Dropped.Should().HaveCount(5, "имя, адрес, телефон, сумма, ссылка — строки выпадают целиком");

        // в мессенджере без {СсылкаНаБронь} сервер добавляет строку «Бронь: …»
        var noLink = await PreviewAsync(company, "Завтра заезд!");
        noLink.Messenger.Text.Should().Contain("Бронь: https://");
        noLink.Page.Text.Should().Be("Завтра заезд!");
        // переключатель push выключен → прежний фиксированный push
        var off = await PreviewAsync(company, "Завтра заезд в «{Дом}»", push: false);
        off.Push.Text.Should().Be("Завтра заезд — откройте бронь");
        off.Push.UsesFixedText.Should().BeTrue();
        // шаблон не менялся → push прежний, даже при включённом переключателе
        var def = await PreviewAsync(company, null, push: true);
        def.Push.Text.Should().Be("Завтра заезд — откройте бронь");
        def.Push.UsesFixedText.Should().BeTrue();
        def.Messenger.Text.Should().Contain("завтра заезд").And.Contain("12 500");
    }

    [Theory, TestCase("CY39-116")]
    [InlineData("Позвоните по номеру 8 900 111-22-33", ReminderDropReason.Digits)]
    [InlineData("Позвоните +7 (900) 111-22-33", ReminderDropReason.Digits)]
    [InlineData("Номер 1234", ReminderDropReason.Digits)]
    [InlineData("Год 2027", ReminderDropReason.Digits)]
    [InlineData("Пишите в телеграм t.me/ownerdom", ReminderDropReason.Link)]
    [InlineData("Пишите в ватсап wa.me/79001112233", ReminderDropReason.Digits)]
    [InlineData("Заходите на bit.ly/abc", ReminderDropReason.Link)]
    [InlineData("Сайт https://example.com/x", ReminderDropReason.Link)]
    [InlineData("Сайт www.example.com", ReminderDropReason.Link)]
    [InlineData("Сайт dom-v-lesu.ru", ReminderDropReason.Link)]
    [InlineData("Пишите owner@example.com", ReminderDropReason.Email)]
    [InlineData("Позвоните 8 9", ReminderDropReason.Phone)]
    [InlineData("Код калитки у нас", ReminderDropReason.CodeWord)]
    [InlineData("Пароль на холодильнике", ReminderDropReason.CodeWord)]
    [InlineData("Wi-Fi в гостиной", ReminderDropReason.CodeWord)]
    [InlineData("WiFi в гостиной", ReminderDropReason.CodeWord)]
    [InlineData("Wi Fi в гостиной", ReminderDropReason.CodeWord)]
    [InlineData("Вай-фай бесплатный", ReminderDropReason.CodeWord)]
    [InlineData("Ключница слева", ReminderDropReason.CodeWord)]
    [InlineData("Сейф в шкафу", ReminderDropReason.CodeWord)]
    [InlineData("Домофон не работает", ReminderDropReason.CodeWord)]
    public async Task PushFilter_DropsRiskyOwnerLines_ButKeepsTheRest(string risky, ReminderDropReason expected)
    {
        var company = await CreateStaysCompanyAsync();
        var template = $"Ждём вас в «{{Дом}}»!\n{risky}\nХорошей дороги";
        var p = await PreviewAsync(company, template, push: true);
        p.Push.UsesFixedText.Should().BeFalse();
        p.Push.Text.Should().Contain("Дом у леса").And.Contain("Хорошей дороги").And.NotContain(risky.Split(' ')[^1], because: "рискованная строка выпала целиком");
        var dropped = p.Push.Dropped.Single();
        dropped.Text.Should().Be(risky);
        dropped.Line.Should().Be(2);
        dropped.Reason.Should().Be(expected, risky);
    }

    [Theory, TestCase("CY39-117")]
    [InlineData("Если хотите задать вопрос — пишите")]
    [InlineData("Заезд в 14:00, выезд в 12:00")]
    [InlineData("Ждём вас 15 янв")]
    [InlineData("Номер 123 — три цифры подряд допустимы")]
    [InlineData("Хорошего вечера!")]
    public async Task PushFilter_KeepsHarmlessLines(string line)
    {
        var company = await CreateStaysCompanyAsync();
        var p = await PreviewAsync(company, $"{line}\nДо встречи", push: true);
        p.Push.Dropped.Should().BeEmpty(line);
        p.Push.Text.Should().Contain(line);
    }

    [Theory, TestCase("CY39-126")]
    [InlineData("Звоните 8–900–111–22–33")]            // тире U+2013
    [InlineData("Звоните +7‑900‑111‑22‑33")]           // неразрывный дефис U+2011
    [InlineData("Звоните 8.900.111.22.33")]            // точки
    [InlineData("Звоните 8/900/111/22/33")]            // косые
    [InlineData("Звоните +7(900)111-22-33")]           // скобки без пробелов
    [InlineData("Мой телеграм tg://resolve?domain=kedr")] // схема tg://
    public async Task PushFilter_PhoneAndLinkVariants_DoNotReachTheLockScreen(string risky)
    {
        var company = await CreateStaysCompanyAsync();
        var p = await PreviewAsync(company, $"Ждём вас в «{{Дом}}»!\n{risky}", push: true);
        p.Push.Text.Should().NotContain(risky.Split(' ')[^1], "ЮР39-3: телефон и ссылка не должны попадать в видимый текст push");
        p.Push.Dropped.Should().ContainSingle(d => d.Text == risky);
    }

    [Fact, TestCase("CY39-118")]
    public async Task Push_CutTo180_WithEllipsis_NoSurrogateSplit_EmptyAfterFilter_FallsBackToFixed()
    {
        var company = await CreateStaysCompanyAsync();
        var long1 = "Ждём вас в доме у леса и надеемся, что дорога будет лёгкой, а погода тёплой. " + string.Concat(Enumerable.Repeat("Очень длинный текст приветствия 😀 ", 8));
        var p = await PreviewAsync(company, long1, push: true);
        p.Push.Text.Length.Should().BeLessThanOrEqualTo(180);
        p.Push.Text.Should().EndWith("…");
        char.IsHighSurrogate(p.Push.Text[^2]).Should().BeFalse("суррогатная пара не режется");
        for (var i = 0; i < p.Push.Text.Length; i++)
            if (char.IsHighSurrogate(p.Push.Text[i])) char.IsLowSurrogate(p.Push.Text[i + 1]).Should().BeTrue();
        // после фильтра ничего не осталось → прежний фиксированный push
        var empty = await PreviewAsync(company, "{ИмяГостя}\n{Адрес}\nКод 1234", push: true);
        empty.Push.UsesFixedText.Should().BeTrue();
        empty.Push.Text.Should().Be("Завтра заезд — откройте бронь");
    }

    [Fact, TestCase("CY39-119")]
    public async Task Preview_OnRealBooking_UsesItsFacts_ForeignBooking404_ServicesPlaceholder()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, name: "Дом Берёзка", price: 3000);
        var svc = await CreateServiceAsync(company);
        var ci = InDays(10);
        var booked = await BookOkAsync(house.Id, ci, ci.AddDays(3), name: "Мария Реальная");
        await AddSessionOkAsync(booked.Token, svc.Id, ci.AddDays(1), 1320, 3);
        var id = await BookingIdAsync(booked.Token);
        var p = await PreviewAsync(company, "{ИмяГостя}, {Дом}, {Ночей}, {ВремяЗаезда}\nУслуги: {Услуги}", push: true, bookingId: id);
        p.Messenger.Text.Should().Contain("Мария Реальная").And.Contain("Дом Берёзка").And.Contain("3 ночи").And.Contain("14:00");
        p.Messenger.Text.Should().MatchRegex(@"Услуги: Баня, \p{L}{2} \d{1,2} \p{L}+, 22:00 — \p{L}{2} \d{1,2} \p{L}+, 01:00");
        p.Push.Text.Should().Contain("Услуги:").And.Contain("Баня").And.NotContain("Мария");
        var other = await CreateStaysCompanyAsync();
        var r = await AuthedClient(other.OwnerToken).PostJsonAsync(Url(other, "/preview"), new ArrivalReminderPreviewInput("x", true, id));
        r.StatusCode.Should().Be(HttpStatusCode.NotFound, "чужая бронь");
        // без услуг строка с {Услуги} выпадает целиком
        var house2 = await CreateHouseAsync(company, price: 3000);
        var plain = await BookOkAsync(house2.Id, ci, ci.AddDays(2));
        var p2 = await PreviewAsync(company, "Привет\nУслуги: {Услуги}", push: true, bookingId: await BookingIdAsync(plain.Token));
        p2.Messenger.Text.Should().NotContain("Услуги:");
    }

    // ── права ────────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-120")]
    public async Task OnlyOwner_ChangesReminder_PushToggleNeedsNoticeVersion_And_IsRecorded()
    {
        var company = await CreateStaysCompanyAsync();
        var manager = await AddStaffAsync(company, "Manager");
        (await AuthedClient(manager.Token).PutJsonAsync(Url(company), Input(template: "Привет"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AuthedClient(manager.Token).PostJsonAsync(Url(company, "/preview"), new ArrivalReminderPreviewInput("x", false, null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var noNotice = await PutAsync(company, Input(template: "Привет, ждём вас!", push: true));
        noNotice.StatusCode.Should().Be(HttpStatusCode.BadRequest, "включение push с текстом — только с подтверждением предупреждения");
        (await GetAsync(company)).PushTextEnabled.Should().BeFalse();
        var ok = await PutAsync(company, Input(template: "Привет, ждём вас!", push: true, pushNotice: "v-push-1"));
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        (await GetAsync(company)).PushTextEnabled.Should().BeTrue();
        var rows = await WithDbAsync(db => db.StaysReminderTemplateChanges.AsNoTracking().Where(c => c.CompanyId == company.Id).ToListAsync());
        rows.Should().ContainSingle().Which.PushNoticeVersion.Should().Be("v-push-1");
        // выключение — без подтверждения; повторное сохранение при включённом — тоже
        (await PutAsync(company, Input(template: "Привет, ждём вас!", push: true))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PutAsync(company, Input(template: "Привет, ждём вас!", push: false))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── отправка: время, шаблон, снимок, push ────────────────────────────────────

    private async Task<(StaysTestFactory Host, StaysCtx Company, string Token, Guid BookingId, DateOnly Arrival)> ConfirmedBookingAsync(
        Action<StaysTestFactory>? _ = null, string name = "Иван Получатель", bool subscribePush = true, string? template = null, bool pushText = false, string time = "18:00", StaysTestFactory? existing = null)
    {
        var host = existing ?? new StaysTestFactory(ConnectionString);
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, name: "Дом Ёлка", price: 3000);
        if (template is not null || time != "18:00")
        {
            var put = await PutAsync(company, Input(time, template, pushText, confirm: true, pushNotice: pushText ? "v-push" : null));
            put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        }
        var arrival = InDays(5);
        var booked = await BookOkAsync(house.Id, arrival, arrival.AddDays(2), name: name, client: host.Client());
        var id = await BookingIdAsync(booked.Token);
        if (subscribePush)
            (await host.Client().PostJsonAsync($"/api/stays/bookings/public/{booked.Token}/push-subscription",
                new PushSubscriptionInput($"https://push.example.test/cy39-rem/{Guid.NewGuid():N}", new PushKeysInput("k1", "k2"), null))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await AttachProofOkAsync(booked.Token, host.Client());
        await StaffActionOkAsync(company, id, "confirm-payment", (await StaffCardAsync(company, id)).Version);
        return (host, company, booked.Token, id, arrival);
    }

    private Task<List<string>> PushBodiesAsync(Guid bookingId) =>
        WithDbAsync(async db => (await db.StayGuestPushNotifications.AsNoTracking().Where(n => n.StayBookingId == bookingId).Select(n => n.Payload).ToListAsync())
            .Select(p => JsonDocument.Parse(p).RootElement.GetProperty("body").GetString()!).ToList());

    [Fact, TestCase("CY39-121")]
    public async Task Send_AtCompanyTime_PageSnapshotWithoutChannels_PushWithoutPersonalData_OnceOnly()
    {
        var template = "{ИмяГостя}, ждём вас в «{Дом}» {ДатаЗаезда}!\nЕсли хотите задать вопрос — пишите\nАдрес: {Адрес}\nКод домофона 4455";
        var (host, company, token, id, arrival) = await ConfirmedBookingAsync(template: template, pushText: true, time: "20:00", name: "Иван Получатель");
        await using var _ = host;
        // 19:59 — рано; 20:00 — отправляется
        host.StaysClock.Set(LocalToUtc(arrival.AddDays(-1), 19, 59));
        await host.RunTaskAsync("stays-scheduled-messages");
        (await GetPublicBookingAsync(token, host.Client())).ArrivalReminder.Should().BeNull();
        (await PushBodiesAsync(id)).Should().NotContain(b => b.Contains("ждём вас"));
        host.StaysClock.Set(LocalToUtc(arrival.AddDays(-1), 20, 0));
        await host.RunTaskAsync("stays-scheduled-messages");
        await host.RunTaskAsync("stays-scheduled-messages");

        var page = (await GetPublicBookingAsync(token, host.Client())).ArrivalReminder;
        page.Should().NotBeNull("снимок на странице брони ставится и тогда, когда каналов (мессенджера) нет");
        page!.Text.Should().Contain("Иван Получатель").And.Contain("Дом Ёлка").And.Contain("Код домофона 4455", "на странице действуют все подстановки и весь текст владельца").And.NotContain("https://");
        page.SentAtUtc.Should().BeCloseTo(LocalToUtc(arrival.AddDays(-1), 20, 0), TimeSpan.FromMinutes(1));

        var bodies = await PushBodiesAsync(id);
        var reminder = bodies.Where(b => b.Contains("задать вопрос") || b.Contains("Завтра заезд")).ToList();
        reminder.Should().ContainSingle("однократно");
        reminder.Single().Should().Be("Если хотите задать вопрос — пишите", "строки с именем, адресом и кодом выпали целиком; остался свободный текст владельца");
        reminder.Single().Should().NotContain("Иван").And.NotContain("Адрес").And.NotContain("4455").And.NotContain("домофон", "имя, адрес и код не попадают в push");
        (await WithDbAsync(db => db.StayGuestPushNotifications.AsNoTracking().Where(n => n.StayBookingId == id).Select(n => n.Payload).ToListAsync()))
            .Should().OnlyContain(p => !p.Contains("Иван") && !p.Contains("Лесная"), "в видимой части push персональных данных нет");
        (await WithDbAsync(db => db.StayBookingEvents.AsNoTracking().CountAsync(e => e.StayBookingId == id && e.Kind == StayBookingEventKind.ArrivalReminderSent))).Should().Be(1);

        // правка шаблона после постановки снимок не меняет
        await PutAsync(company, Input("20:00", "Совсем другой текст", true, true, "v-push"));
        host.StaysClock.Set(LocalToUtc(arrival.AddDays(-1), 21, 0));
        await host.RunTaskAsync("stays-scheduled-messages");
        (await GetPublicBookingAsync(token, host.Client())).ArrivalReminder!.Text.Should().Be(page.Text);
    }

    [Fact, TestCase("CY39-122")]
    public async Task Send_DefaultTemplate_PushIsOldFixedText_PageHasDefaultWithoutLink()
    {
        var (host, _, token, id, arrival) = await ConfirmedBookingAsync();
        await using var _ = host;
        host.StaysClock.Set(LocalToUtc(arrival.AddDays(-1), 18, 0));
        await host.RunTaskAsync("stays-scheduled-messages");
        (await PushBodiesAsync(id)).Count(b => b == "Завтра заезд — откройте бронь").Should().Be(1, "байт-в-байт прежний push");
        var page = (await GetPublicBookingAsync(token, host.Client())).ArrivalReminder!;
        page.Text.Should().Contain("завтра заезд в «Дом Ёлка»").And.Contain("К оплате при заселении").And.NotContain("Бронь:").And.NotContain("https://");
    }

    [Fact, TestCase("CY39-123")]
    public async Task Send_NewTimeAppliesToUnsentBookings_OnceOnly_LateCreatedGoesImmediately()
    {
        var (host, company, token, id, arrival) = await ConfirmedBookingAsync();
        await using var _ = host;
        // время сдвинули на 21:00 до отправки — в 18:05 ещё не уходит
        (await PutAsync(company, Input("21:00"))).StatusCode.Should().Be(HttpStatusCode.OK);
        host.StaysClock.Set(LocalToUtc(arrival.AddDays(-1), 18, 5));
        await host.RunTaskAsync("stays-scheduled-messages");
        (await GetPublicBookingAsync(token, host.Client())).ArrivalReminder.Should().BeNull();
        host.StaysClock.Set(LocalToUtc(arrival.AddDays(-1), 21, 0));
        await host.RunTaskAsync("stays-scheduled-messages");
        (await GetPublicBookingAsync(token, host.Client())).ArrivalReminder.Should().NotBeNull();
        // смена времени не даёт второго напоминания
        await PutAsync(company, Input("08:00"));
        host.StaysClock.Set(LocalToUtc(arrival.AddDays(-1), 22, 0));
        await host.RunTaskAsync("stays-scheduled-messages");
        (await WithDbAsync(db => db.StayBookingEvents.AsNoTracking().CountAsync(e => e.StayBookingId == id && e.Kind == StayBookingEventKind.ArrivalReminderSent))).Should().Be(1);

        // бронь, подтверждённая после времени напоминания, но до заезда, получает его сразу
        var house = await CreateHouseAsync(company, price: 1000);
        var late = await BookOkAsync(house.Id, arrival, arrival.AddDays(2), client: host.Client());
        var lateId = await BookingIdAsync(late.Token);
        await AttachProofOkAsync(late.Token, host.Client());
        await StaffActionOkAsync(company, lateId, "confirm-payment", (await StaffCardAsync(company, lateId)).Version);
        host.StaysClock.Set(LocalToUtc(arrival.AddDays(-1), 23, 0));
        await host.RunTaskAsync("stays-scheduled-messages");
        (await GetPublicBookingAsync(late.Token, host.Client())).ArrivalReminder.Should().NotBeNull();
    }

    [Fact, TestCase("CY39-124")]
    public async Task Send_ReminderOff_NoSnapshot_NoMessage_ButMarkedOnce()
    {
        var company = await CreateStaysCompanyAsync(settings: s => s with { ArrivalReminderEnabled = false });
        var house = await CreateHouseAsync(company, price: 1000);
        await using var host = new StaysTestFactory(ConnectionString);
        var arrival = InDays(5);
        var booked = await BookOkAsync(house.Id, arrival, arrival.AddDays(2), client: host.Client());
        var id = await BookingIdAsync(booked.Token);
        await AttachProofOkAsync(booked.Token, host.Client());
        await StaffActionOkAsync(company, id, "confirm-payment", (await StaffCardAsync(company, id)).Version);
        host.StaysClock.Set(LocalToUtc(arrival.AddDays(-1), 18, 30));
        await host.RunTaskAsync("stays-scheduled-messages");
        (await GetPublicBookingAsync(booked.Token, host.Client())).ArrivalReminder.Should().BeNull();
    }
}
