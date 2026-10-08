using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Retention;
using ServiceBooking.API.Services.Retention.Rules;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 37, «Вызов 2»: график уборок без лишнего, реквизиты не в публичных ответах, запрещённые слова, выгрузка и удаление аккаунта,
/// retention подтверждений оплаты и неоплаченных броней (US-37-08, 27, 32, ЮР-1, ЮР-3, ЮР-4, ЮР-5, ЮР-6; ARCHITECTURE_CYCLE37.md §37.13).
/// </summary>
public class Cycle37PrivacyTests(TestDatabaseFixture fixture) : Cycle37TestBase(fixture)
{
    private static readonly Regex Forbidden = new("задат|невозвратн|депозит", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private async Task<Guid> ConfirmedAsync(StaysCtx company, HouseCtx house, DateOnly ci, DateOnly co, string name, string phone, string? comment = null, bool awaitingOnly = false)
    {
        var booked = await BookOkAsync(house.Id, ci, co, phone: phone, name: name, comment: comment);
        var id = await BookingIdAsync(booked.Token);
        await AttachProofOkAsync(booked.Token);
        if (!awaitingOnly) await StaffActionOkAsync(company, id, "confirm-payment", (await StaffCardAsync(company, id)).Version);
        return id;
    }

    // ── график уборок ─────────────────────────────────────────────────────────────

    [Fact, TestCase("CY37-100")]
    public async Task Schedule_HasNoPhonesAmountsProofsOrRequisites_CommentHiddenForHousekeeperByDefault()
    {
        var company = await CreateStaysCompanyAsync();
        var houseA = await CreateHouseAsync(company, name: "Дом Альфа", price: 3000);
        var houseB = await CreateHouseAsync(company, name: "Дом Бета", price: 3000);
        var housekeeper = await AddStaffAsync(company, "Housekeeper");
        var manager = await AddStaffAsync(company, "Manager");

        await ConfirmedAsync(company, houseA, InDays(1), InDays(3), "Анна Первая", "+79051112233", "Комментарий-секрет про аллергию");
        await ConfirmedAsync(company, houseA, InDays(3), InDays(5), "Борис Второй", "+79052223344", awaitingOnly: true);
        await BookOkAsync(houseB.Id, InDays(6), InDays(7), name: "Held Невидимка", phone: "+79053334455"); // «Удержана» — в график не попадает
        await ConfirmedAsync(company, houseB, InDays(20), InDays(22), "Далёкий Гость", "+79054445566"); // за пределами 15 дней

        var hk = await ScheduleAsync(company, housekeeper.Token);
        hk.Days.Should().HaveCount(15, "сегодня, завтра и ещё 13 дней");
        hk.Days[0].Label.Should().Be("Сегодня");
        hk.Days[1].Label.Should().Be("Завтра");

        var raw = await AuthedClient(housekeeper.Token).GetStringAsync($"/api/stays/companies/{company.Id}/schedule");
        foreach (var forbidden in new[] { "9051112233", "9052223344", "111-22-33", "Rub", "prepay", "paymentDetails", "proof", "Proof", "Сбербанк", "2202", "Невидимка", "Далёкий", "reason", "phone" })
            raw.Should().NotContain(forbidden, "график горничной не содержит телефонов, сумм, чеков и реквизитов");

        var day1 = hk.Days[1];
        var anna = day1.Arrivals.Single();
        anna.GuestName.Should().Be("Анна Первая", "горничная видит имя гостя");
        anna.Adults.Should().Be(2);
        anna.HouseName.Should().Be("Дом Альфа");
        anna.CheckInTime.Should().Be("14:00");
        anna.Comment.Should().BeNull("ЮР-5: комментарий гостя горничной по умолчанию скрыт");
        anna.PaymentUnconfirmed.Should().BeFalse();

        // день смены гостей: выезд Анны и заезд Бориса в один день
        var day3 = hk.Days[3];
        day3.Departures.Single().SameDayTurnover.Should().BeTrue();
        day3.Departures.Single().CheckOutTime.Should().Be("12:00");
        var boris = day3.Arrivals.Single();
        boris.SameDayTurnover.Should().BeTrue();
        boris.PaymentUnconfirmed.Should().BeTrue("«Ожидает проверки оплаты» — с пометкой «оплата не подтверждена»");
        boris.TurnoverText.Should().Be("Выезд и заезд в один день — уборка 12:00–14:00");

        hk.Days.SelectMany(d => d.Arrivals.Select(a => a.GuestName)).Should().NotContain(["Held Невидимка", "Далёкий Гость"]);
        hk.Days.Where(d => d.Date == InDays(6)).Should().OnlyContain(d => d.Arrivals.Count == 0 && d.Departures.Count == 0);

        // управляющий видит комментарий; та же форма, без телефонов и сумм
        var mgrRaw = await AuthedClient(manager.Token).GetStringAsync($"/api/stays/companies/{company.Id}/schedule");
        mgrRaw.Should().Contain("Комментарий-секрет про аллергию").And.NotContain("9051112233").And.NotContain("Rub");

        // владелец включает настройку — горничная видит комментарий
        var settings = (await GetCompanyAsync(company)).Settings!;
        await PutSettingsAsync(company, settings with { HousekeeperSeesGuestComment = true });
        (await ScheduleAsync(company, housekeeper.Token)).Days[1].Arrivals.Single().Comment.Should().Be("Комментарий-секрет про аллергию");

        // окно и диапазон: days=1 — один день; days=99 — не больше 15
        (await ScheduleAsync(company, housekeeper.Token, days: 1)).Days.Should().HaveCount(1);
        (await ScheduleAsync(company, housekeeper.Token, days: 99)).Days.Should().HaveCount(15);
        (await ScheduleAsync(company, housekeeper.Token, from: InDays(18), days: 5)).Days.SelectMany(d => d.Arrivals).Select(a => a.GuestName).Should().Contain("Далёкий Гость");
    }

    // ── реквизиты и сведения об исполнителе ───────────────────────────────────────

    [Fact, TestCase("CY37-101")]
    public async Task PaymentDetails_NeverInPublicResponses_OnlyOnTheBookingPageOfThatBooking_AndToOwner()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 3000, hasCot: true, extraBedsMax: 1, extraBedPrice: 500);
        var manager = await AddStaffAsync(company, "Manager");
        var housekeeper = await AddStaffAsync(company, "Housekeeper");
        var ci = InDays(10);
        var co = InDays(12);
        InvalidateCatalog();

        var publicTexts = new List<string>
        {
            await AnonymousClient().GetStringAsync("/api/stays/public/amenities"),
            await AnonymousClient().GetStringAsync("/api/stays/public/catalog?pageSize=50"),
            await AnonymousClient().GetStringAsync($"/api/stays/public/catalog?pageSize=50&checkIn={D(ci)}&checkOut={D(co)}"),
            await AnonymousClient().GetStringAsync($"/api/stays/public/companies/{company.Slug}"),
            await AnonymousClient().GetStringAsync($"/api/stays/public/companies/{company.Slug}?checkIn={D(ci)}&checkOut={D(co)}"),
            await AnonymousClient().GetStringAsync($"/api/stays/public/companies/{company.Slug}/houses/{house.Slug}"),
            await AnonymousClient().GetStringAsync($"/api/stays/public/houses/{house.Id}/calendar"),
            await (await AnonymousClient().PostJsonAsync($"/api/stays/public/houses/{house.Id}/quote", new StayQuoteInput(ci, co, 2, 0, 0, false))).Content.ReadAsStringAsync(),
        };
        // каждый публичный ответ — и чужие реквизиты не раскрывает, и ФИО физлица/адрес для претензий (ЮР-3) скрыты
        foreach (var text in publicTexts)
        {
            text.Should().NotContain("Сбербанк").And.NotContain("2202").And.NotContain("111-22-33").And.NotContain(PaymentPurposeText).And.NotContain("paymentDetails");
            text.Should().NotContain("Иванов Иван Иванович").And.NotContain("ул. Мира");
        }

        // сотрудники без права «Настройки компании» реквизитов в карточке не видят
        (await GetCompanyAsync(company, manager.Token)).PaymentDetails.Should().BeNull();
        (await GetCompanyAsync(company, housekeeper.Token)).PaymentDetails.Should().BeNull();
        (await GetCompanyAsync(company)).PaymentDetails!.PaymentDetails.Should().Be(PaymentDetailsText);

        // гость видит реквизиты только своей брони: по ссылке; конечная бронь уже без них
        var mine = await BookOkAsync(house.Id, ci, co);
        var page = await AnonymousClient().GetStringAsync($"/api/stays/bookings/public/{mine.Token}");
        page.Should().Contain("Сбербанк");
        var other = await BookOkAsync(house.Id, InDays(20), InDays(21));
        var otherPage = await AnonymousClient().GetStringAsync($"/api/stays/bookings/public/{other.Token}");
        otherPage.Should().Contain("Сбербанк", "реквизиты показываются тому, у кого есть ссылка на бронь");
        (await AnonymousClient().PostJsonAsync($"/api/stays/bookings/public/{mine.Token}/cancel", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AnonymousClient().GetStringAsync($"/api/stays/bookings/public/{mine.Token}")).Should().NotContain("Сбербанк", "в конечных статусах реквизитов нет");

        // реквизиты в булавках журнала/уведомлений персоналу не попадают
        var staffRows = await WithDbAsync(db => db.StaffPushNotifications.AsNoTracking().Where(n => n.CompanyId == company.Id).Select(n => n.Payload).ToListAsync());
        staffRows.Should().NotContain(p => p.Contains("Сбербанк") || p.Contains("9001112233") || p.Contains("Пётр"));
    }

    [Fact, TestCase("CY37-102")]
    public async Task CheckInInfo_NotInPublicOrBeforeRelease_ReleasedOnArrivalDayAtConfiguredTime()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var client = host.Client();
        var company = await CreateStaysCompanyAsync(settings: s => s with { CheckInInfoText = "Wi-Fi: секретный-вайфай-пароль", CheckInInfoSendTime = "09:00" });
        var house = await CreateHouseAsync(company, price: 3000);
        await AuthedClient(company.OwnerToken).PutJsonAsync($"/api/stays/companies/{company.Id}/houses/{house.Id}/content",
            new HouseContentInput("Описание", [], "Шерегеш, Лесная 5", null, null, "Код ключницы 7788"));

        var arrival = InDays(5);
        var booked = await BookOkAsync(house.Id, arrival, arrival.AddDays(2), client: client);
        var id = await BookingIdAsync(booked.Token);
        await AttachProofOkAsync(booked.Token, client);

        // публично и до подтверждения кодов нет
        (await AnonymousClient().GetStringAsync($"/api/stays/public/companies/{company.Slug}/houses/{house.Slug}")).Should().NotContain("7788").And.NotContain("вайфай");
        (await GetPublicBookingAsync(booked.Token, client)).CheckInInfo.Should().BeNull();

        await StaffActionOkAsync(company, id, "confirm-payment", (await StaffCardAsync(company, id)).Version);
        (await GetPublicBookingAsync(booked.Token, client)).CheckInInfo.Should().BeNull("до дня заезда информации нет");

        // накануне — всё ещё нет; в день заезда в 09:00 по времени компании — выпущена
        host.StaysClock.Set(LocalToUtc(arrival, 8, 59));
        await host.RunTaskAsync("stays-scheduled-messages");
        (await GetPublicBookingAsync(booked.Token, client)).CheckInInfo.Should().BeNull("раньше времени отправки");
        host.StaysClock.Set(LocalToUtc(arrival, 9, 0));
        await host.RunTaskAsync("stays-scheduled-messages");
        var released = (await GetPublicBookingAsync(booked.Token, client)).CheckInInfo;
        released.Should().NotBeNull();
        released!.CompanyText.Should().Be("Wi-Fi: секретный-вайфай-пароль");
        released.HouseText.Should().Be("Код ключницы 7788");
        var events = await WithDbAsync(db => db.StayBookingEvents.AsNoTracking().Where(e => e.StayBookingId == id).Select(e => e.Kind).ToListAsync());
        events.Count(k => k == StayBookingEventKind.CheckInInfoReleased).Should().Be(1);
        await host.RunTaskAsync("stays-scheduled-messages");
        (await WithDbAsync(db => db.StayBookingEvents.CountAsync(e => e.StayBookingId == id && e.Kind == StayBookingEventKind.CheckInInfoReleased)))
            .Should().Be(1, "однократно: повторный проход не выпускает второй раз");
    }

    // ── запрещённые слова ─────────────────────────────────────────────────────────

    [Fact, TestCase("CY37-103")]
    public async Task ForbiddenWords_ZadatokNevozvratnyDepozit_NeverInServerTexts_ForAllTemplatesAndStatuses()
    {
        var texts = new List<string>();
        foreach (var policy in Enum.GetValues<StayCancellationPolicy>())
        {
            var company = await CreateStaysCompanyAsync(policy: policy);
            var house = await CreateHouseAsync(company, price: 2000);
            texts.Add(await AnonymousClient().GetStringAsync($"/api/stays/public/companies/{company.Slug}/houses/{house.Slug}"));
            texts.Add(await (await AnonymousClient().PostJsonAsync($"/api/stays/public/houses/{house.Id}/quote", new StayQuoteInput(InDays(10), InDays(12), 2, 0, 0, false))).Content.ReadAsStringAsync());
            texts.Add(JsonSerializer.Serialize(await GetCompanyAsync(company)));

            // «Ожидает проверки оплаты» — отказ владельца, отмена владельцем, отказ по оплате, отмена гостем, истечение
            async Task<(string Token, Guid Id)> NewBooking(int shift, bool proof)
            {
                var b = await BookOkAsync(house.Id, InDays(20 + shift), InDays(22 + shift));
                var id = await BookingIdAsync(b.Token);
                if (proof) await AttachProofOkAsync(b.Token);
                return (b.Token, id);
            }
            var rejected = await NewBooking(0, true);
            await StaffActionOkAsync(company, rejected.Id, "reject-payment", (await StaffCardAsync(company, rejected.Id)).Version, "Оплата не поступила");
            var ownerCancelled = await NewBooking(3, true);
            texts.Add(JsonSerializer.Serialize(await StaffCardAsync(company, ownerCancelled.Id)));
            await StaffActionOkAsync(company, ownerCancelled.Id, "cancel", (await StaffCardAsync(company, ownerCancelled.Id)).Version, "Авария на доме");
            var guestCancelled = await NewBooking(6, true);
            texts.Add(await AnonymousClient().GetStringAsync($"/api/stays/bookings/public/{guestCancelled.Token}"));
            await AnonymousClient().PostJsonAsync($"/api/stays/bookings/public/{guestCancelled.Token}/cancel", new { });
            var held = await NewBooking(9, false);
            texts.Add(await AnonymousClient().GetStringAsync($"/api/stays/bookings/public/{held.Token}"));
            var confirmed = await NewBooking(12, true);
            await StaffActionOkAsync(company, confirmed.Id, "confirm-payment", (await StaffCardAsync(company, confirmed.Id)).Version);
            texts.Add(await AnonymousClient().GetStringAsync($"/api/stays/bookings/public/{confirmed.Token}"));
            foreach (var t in new[] { rejected.Token, ownerCancelled.Token, guestCancelled.Token })
                texts.Add(await AnonymousClient().GetStringAsync($"/api/stays/bookings/public/{t}"));
            texts.Add(JsonSerializer.Serialize(await StaffCardAsync(company, confirmed.Id)));
        }
        var trial = await AuthedClient((await RegisterAsync()).Token).GetStringAsync("/api/stays/trial");
        texts.Add(trial);
        texts.Add(await (await AnonymousClient().PostJsonAsync($"/api/stays/public/houses/{Guid.NewGuid()}/quote", new StayQuoteInput(InDays(1), InDays(2), 1, 0, 0, false))).Content.ReadAsStringAsync());

        foreach (var text in texts)
            Forbidden.IsMatch(text).Should().BeFalse("ЮР-1: слова «задаток», «невозвратный», «депозит» не используются — найдено в: " + Forbidden.Match(text).Value + " ← " +
                (Forbidden.IsMatch(text) ? text[Math.Max(0, Forbidden.Match(text).Index - 80)..Math.Min(text.Length, Forbidden.Match(text).Index + 80)] : ""));
        // тексты очередей уведомлений персоналу
        var rows = await WithDbAsync(db => db.StaffPushNotifications.AsNoTracking().Select(n => n.Payload).ToListAsync());
        rows.Should().NotContain(p => Forbidden.IsMatch(p));
    }

    // ── выгрузка и удаление аккаунта ──────────────────────────────────────────────

    [Fact, TestCase("CY37-104")]
    public async Task Export_ContainsAccountBookings_GuestBookingsOnlyForVerifiedPhone_NoFilesNoStaffNames()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        var guest = await RegisterAsync();
        var phone = guest.Phone;

        // бронь аккаунта (вошедший гость) и гостевая бронь на тот же номер
        var asCustomer = await BookOkAsync(house.Id, InDays(10), InDays(12), client: AuthedClient(guest.Token), comment: "Бронь аккаунта");
        var asGuest = await BookOkAsync(house.Id, InDays(20), InDays(22), phone: phone, comment: "Гостевая бронь");
        await AttachProofOkAsync(asCustomer.Token);
        var customerBookingId = await BookingIdAsync(asCustomer.Token);
        await StaffActionOkAsync(company, customerBookingId, "confirm-payment", (await StaffCardAsync(company, customerBookingId)).Version);

        var before = await J(await AuthedClient(guest.Token).GetAsync("/api/profile/export"));
        var beforeBookings = before.GetProperty("stayBookings").EnumerateArray().ToList();
        beforeBookings.Should().ContainSingle("гостевая бронь без подтверждённого номера не подтягивается (SUBJECT-PHONE-GATE)");
        beforeBookings[0].GetProperty("comment").GetString().Should().Be("Бронь аккаунта");

        await MarkPhoneVerifiedAsync(phone, guest.UserId);
        var export = await J(await AuthedClient(guest.Token).GetAsync("/api/profile/export"));
        var bookings = export.GetProperty("stayBookings").EnumerateArray().ToList();
        bookings.Should().HaveCount(2);
        var mine = bookings.Single(b => b.GetProperty("comment").GetString() == "Бронь аккаунта");
        mine.GetProperty("companyName").GetString().Should().Be(company.Company.Name);
        mine.GetProperty("houseName").GetString().Should().Be(house.House.Name);
        mine.GetProperty("checkInDate").GetString().Should().Be(D(InDays(10)));
        mine.GetProperty("status").GetString().Should().Be("Confirmed");
        mine.GetProperty("guestName").GetString().Should().Be("Пётр Гость");
        mine.GetProperty("guestPhone").GetString().Should().NotBeNullOrEmpty();
        mine.GetProperty("totalRub").GetInt32().Should().Be(4000);
        mine.GetProperty("prepayRub").GetInt32().Should().Be(1200);
        mine.GetProperty("bookingUrl").GetString().Should().StartWith("https://dom.ezbook.ru/b/");
        var proofs = mine.GetProperty("paymentProofs").EnumerateArray().ToList();
        proofs.Should().ContainSingle();
        proofs[0].GetProperty("contentType").GetString().Should().Be("image/jpeg");
        proofs[0].GetProperty("purged").GetBoolean().Should().BeFalse();
        mine.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("text").GetString()).Should().Contain("Бронь создана");
        var raw = export.GetProperty("stayBookings").GetRawText();
        raw.Should().NotContain("storageKey").And.NotContain("StorageKey").And.NotContain(company.Owner.FirstName + " " + company.Owner.LastName);
    }

    [Fact, TestCase("CY37-105")]
    public async Task DeleteAccount_ErasesGuestBookings_KeepsCalendarStatusAndAmounts_RemovesProofFilesAndPush()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        var guest = await RegisterAsync(firstName: "Ольга", lastName: "Гостева");
        var booked = await BookOkAsync(house.Id, InDays(10), InDays(12), client: AuthedClient(guest.Token), comment: "Личный комментарий", name: "Ольга Гостева");
        var id = await BookingIdAsync(booked.Token);
        await AttachProofOkAsync(booked.Token);
        await StaffActionOkAsync(company, id, "confirm-payment", (await StaffCardAsync(company, id)).Version);
        var storageKey = await WithDbAsync(db => db.StayPaymentProofs.Where(p => p.StayBookingId == id).Select(p => p.StorageKey).SingleAsync());
        storageKey.Should().NotBeNull();

        var preview = await J(await AuthedClient(guest.Token).GetAsync("/api/profile/delete-account/preview"));
        preview.GetProperty("stayBookings").GetInt32().Should().Be(1);

        var del = await AuthedClient(guest.Token).PostJsonAsync("/api/profile/delete-account", new { currentPassword = "Password123!" });
        del.StatusCode.Should().Be(HttpStatusCode.NoContent, await del.Content.ReadAsStringAsync());

        var card = await StaffCardAsync(company, id);
        card.GuestName.Should().BeNull();
        card.GuestPhone.Should().BeNull();
        card.Comment.Should().BeNull();
        card.ArrivalTime.Should().BeNull();
        card.Status.Should().Be(StayBookingStatus.Confirmed, "активные брони не отменяются");
        card.TotalRub.Should().Be(4000);
        card.PaymentConfirmed.Should().NotBeNull("факт оплаты остаётся");
        card.PaymentProofs.Should().OnlyContain(p => p.Purged, "файлы подтверждений удаляются сразу");
        card.Events.Should().Contain(e => e.Kind == nameof(StayBookingEventKind.PersonalDataErased));
        card.Events.Where(e => e.ActorText.Contains("Ольга") || e.ActorText.Contains("Гостева")).Should().BeEmpty("имя гостя в журнале заменено");
        (await CalendarAsync(house.Id, InDays(9), InDays(13))).Days.Single(d => d.Date == InDays(10)).State.Should().Be(CalendarDayState.Occupied, "календарь владельца не ломается");
        var db1 = await WithDbAsync(db => db.StayBookings.AsNoTracking().SingleAsync(b => b.Id == id));
        db1.PersonalDataErased.Should().BeTrue();
        db1.GuestUserId.Should().BeNull();
        using var scope = Factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<FileStorage>();
        var open = () => storage.OpenPrivate(storageKey!);
        open.Should().Throw<FileNotFoundException>("файл удалён с диска");
        (await AnonymousClient().GetAsync($"/api/stays/bookings/public/{booked.Token}/payment-proofs/{card.PaymentProofs[0].Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── retention ────────────────────────────────────────────────────────────────

    private async Task<(Guid Id, string? StorageKey)> BookingWithProofAsync(StaysCtx company, HouseCtx house, int shift, bool confirm = true)
    {
        var booked = await BookOkAsync(house.Id, InDays(10 + shift), InDays(12 + shift), name: "Retention Гость");
        var id = await BookingIdAsync(booked.Token);
        await AttachProofOkAsync(booked.Token);
        if (confirm) await StaffActionOkAsync(company, id, "confirm-payment", (await StaffCardAsync(company, id)).Version);
        var key = await WithDbAsync(db => db.StayPaymentProofs.Where(p => p.StayBookingId == id).Select(p => p.StorageKey).SingleAsync());
        return (id, key);
    }

    private async Task MoveAsync(Guid bookingId, int checkOutDaysAgo, int? terminalDaysAgo = null)
    {
        await WithDbAsync(async db =>
        {
            var b = await db.StayBookings.SingleAsync(x => x.Id == bookingId);
            var checkOut = InDays(-checkOutDaysAgo);
            b.CheckOutDate = checkOut;
            b.CheckInDate = checkOut.AddDays(-2);
            if (terminalDaysAgo is { } t) b.TerminalAtUtc = DateTime.UtcNow.AddDays(-t);
            await db.SaveChangesAsync();
        });
    }

    private async Task<RetentionOutcome> RunRuleAsync<T>(Func<AppDbContext, FileStorage, T> create, bool dryRun, DateTime? now = null) where T : IRetentionRule
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<FileStorage>();
        var periods = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RetentionPeriods>>().Value;
        return await create(db, storage).ApplyAsync(new RetentionContext(now ?? DateTime.UtcNow, periods, 100, dryRun), CancellationToken.None);
    }

    [Fact, TestCase("CY37-106")]
    public async Task Retention_PaymentProofs_DeletedAfter90DaysFromLaterOfCheckOutAndTerminalStatus_DryRunChangesNothing()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        var old = await BookingWithProofAsync(company, house, 0);          // выезд 91 день назад — удаляем
        var fresh = await BookingWithProofAsync(company, house, 5);        // выезд 89 дней назад — рано
        var cancelledRecently = await BookingWithProofAsync(company, house, 10); // выезд 100 дней назад, но отменена 10 дней назад — рано (позднейшая из дат)
        await MoveAsync(old.Id, 91);
        await MoveAsync(fresh.Id, 89);
        await MoveAsync(cancelledRecently.Id, 100, terminalDaysAgo: 10);
        await WithDbAsync(async db =>
        {
            var b = await db.StayBookings.SingleAsync(x => x.Id == cancelledRecently.Id);
            b.Status = StayBookingStatus.CancelledByOwner;
            await db.SaveChangesAsync();
        });

        // сухой прогон: считает, но ничего не меняет
        var dry = await RunRuleAsync((db, st) => new StayPaymentProofRule(db, st), dryRun: true);
        dry.Affected.Should().BeGreaterThanOrEqualTo(1);
        (await WithDbAsync(db => db.StayPaymentProofs.Where(p => p.StayBookingId == old.Id).Select(p => p.StorageKey).SingleAsync())).Should().NotBeNull("сухой прогон ничего не удаляет");

        var live = await RunRuleAsync((db, st) => new StayPaymentProofRule(db, st), dryRun: false);
        live.Affected.Should().BeGreaterThanOrEqualTo(1);

        var oldProof = await WithDbAsync(db => db.StayPaymentProofs.AsNoTracking().SingleAsync(p => p.StayBookingId == old.Id));
        oldProof.StorageKey.Should().BeNull();
        oldProof.PurgedAtUtc.Should().NotBeNull();
        var oldBooking = await StaffCardAsync(company, old.Id);
        oldBooking.PaymentProofsPurgedAtUtc.Should().NotBeNull("в брони остаётся отметка «подтверждение удалено по сроку хранения»");
        oldBooking.PaymentConfirmed.Should().NotBeNull("факт оплаты (сумма, кто и когда) остаётся");
        oldBooking.TotalRub.Should().Be(4000);
        oldBooking.PaymentProofs.Should().OnlyContain(p => p.Purged);
        oldBooking.Events.Should().Contain(e => e.Kind == nameof(StayBookingEventKind.PaymentProofsPurged));
        using (var scope = Factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<FileStorage>();
            var open = () => storage.OpenPrivate(old.StorageKey!);
            open.Should().Throw<FileNotFoundException>("файл удалён с диска");
        }
        (await WithDbAsync(db => db.StayPaymentProofs.AsNoTracking().SingleAsync(p => p.StayBookingId == fresh.Id))).StorageKey.Should().NotBeNull("89 дней — ещё рано");
        (await WithDbAsync(db => db.StayPaymentProofs.AsNoTracking().SingleAsync(p => p.StayBookingId == cancelledRecently.Id))).StorageKey.Should().NotBeNull("срок — от более поздней даты (конечный статус 10 дней назад)");

        // повторный прогон — ничего нового для той же брони
        await RunRuleAsync((db, st) => new StayPaymentProofRule(db, st), dryRun: false);
        (await StaffCardAsync(company, old.Id)).Events.Count(e => e.Kind == nameof(StayBookingEventKind.PaymentProofsPurged)).Should().Be(1);
    }

    [Fact, TestCase("CY37-106b")]
    public async Task Retention_PaymentProofs_NotDeletedHoursBeforeTheExactCheckOutMoment_WhenTerminalStatusIsOld()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        var b = await BookingWithProofAsync(company, house, 20);
        var checkOutUtc = await WithDbAsync(async db =>
        {
            var row = await db.StayBookings.SingleAsync(x => x.Id == b.Id);
            row.CheckOutDate = InDays(-90);
            row.CheckInDate = row.CheckOutDate.AddDays(-2);
            row.CheckOutTimeSnapshot = new TimeOnly(12, 0);
            row.TerminalAtUtc = DateTime.UtcNow.AddDays(-200); // the final status is long ago: only the check-out moment decides
            await db.SaveChangesAsync();
            return ServiceBooking.API.Services.Stays.StayTime.ToUtc(row.TimeZoneIdSnapshot, row.CheckOutDate, row.CheckOutTimeSnapshot);
        });

        await RunRuleAsync((db, st) => new StayPaymentProofRule(db, st), dryRun: false, now: checkOutUtc.AddDays(90).AddHours(-2));
        (await WithDbAsync(db => db.StayPaymentProofs.AsNoTracking().SingleAsync(p => p.StayBookingId == b.Id))).StorageKey.Should().NotBeNull("до срока ещё два часа");

        await RunRuleAsync((db, st) => new StayPaymentProofRule(db, st), dryRun: false, now: checkOutUtc.AddDays(90).AddHours(1));
        (await WithDbAsync(db => db.StayPaymentProofs.AsNoTracking().SingleAsync(p => p.StayBookingId == b.Id))).StorageKey.Should().BeNull("срок прошёл");
    }

    [Fact, TestCase("CY37-107")]
    public async Task Retention_UnpaidReleasedBookings_AreDepersonalisedAfter30Days_OthersUntouched()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        async Task<Guid> Unpaid(int shift, int removedDaysAgo, StayBookingStatus status = StayBookingStatus.ExpiredUnpaid)
        {
            var b = await BookOkAsync(house.Id, InDays(100 + shift * 3), InDays(102 + shift * 3), name: "Неоплатил Гость", comment: "ПДн-комментарий");
            var id = await BookingIdAsync(b.Token);
            await WithDbAsync(async db =>
            {
                var row = await db.StayBookings.SingleAsync(x => x.Id == id);
                row.Status = status;
                row.TerminalAtUtc = status == StayBookingStatus.Held ? null : DateTime.UtcNow.AddDays(-removedDaysAgo);
                row.HoldExpiresAtUtc = status == StayBookingStatus.Held ? row.HoldExpiresAtUtc : null;
                foreach (var o in db.HouseOccupancies.Where(o => o.StayBookingId == id)) { o.ReleasedAtUtc = status == StayBookingStatus.Held ? null : DateTime.UtcNow; o.HoldExpiresAtUtc = null; }
                await db.SaveChangesAsync();
            });
            return id;
        }
        var old = await Unpaid(0, 31);
        var recent = await Unpaid(1, 29);
        var held = await Unpaid(2, 60, StayBookingStatus.Held);
        var cancelled = await Unpaid(3, 60, StayBookingStatus.CancelledByGuest);

        var dry = await RunRuleAsync((db, _) => new StayUnpaidPersonalizationRule(db), dryRun: true);
        dry.Affected.Should().BeGreaterThanOrEqualTo(1);
        (await WithDbAsync(db => db.StayBookings.AsNoTracking().SingleAsync(b => b.Id == old))).GuestName.Should().NotBeNull("сухой прогон ничего не меняет");

        await RunRuleAsync((db, _) => new StayUnpaidPersonalizationRule(db), dryRun: false);
        var erased = await WithDbAsync(db => db.StayBookings.AsNoTracking().SingleAsync(b => b.Id == old));
        erased.PersonalDataErased.Should().BeTrue();
        erased.GuestName.Should().BeNull();
        erased.GuestPhone.Should().BeNull();
        erased.Comment.Should().BeNull();
        erased.TotalRub.Should().Be(4000, "даты и суммы остаются");
        (await WithDbAsync(db => db.StayBookings.AsNoTracking().SingleAsync(b => b.Id == recent))).GuestName.Should().NotBeNull("29 дней — ещё рано");
        (await WithDbAsync(db => db.StayBookings.AsNoTracking().SingleAsync(b => b.Id == held))).GuestName.Should().NotBeNull("удержанная бронь не трогается");
        (await WithDbAsync(db => db.StayBookings.AsNoTracking().SingleAsync(b => b.Id == cancelled))).GuestName.Should().NotBeNull("правило 30 дней — только для «Снята: не оплачена»");
    }

    [Fact, TestCase("CY37-108")]
    public async Task Retention_AllSixStayRulesAreRegistered_AndPeriodsComeFromConfiguration()
    {
        using var scope = Factory.Services.CreateScope();
        var names = scope.ServiceProvider.GetServices<IRetentionRule>().Select(r => r.Name).ToList();
        names.Should().Contain([
            "stay-payment-proofs", "stay-unpaid-personalization", "stay-booking-personalization", "stay-booking-events",
            "stay-guest-push-subscriptions", "stay-guest-push-notifications"]);
        var periods = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RetentionPeriods>>().Value;
        periods.StayPaymentProofDays.Should().Be(90);
        periods.StayUnpaidBookingDays.Should().Be(30);
        periods.StayBookingPersonalDataDays.Should().Be(1095);
        periods.StayBookingEventDays.Should().Be(1095);
        periods.StayGuestPushSubscriptionDays.Should().Be(7);
        periods.StayGuestPushNotificationDays.Should().Be(90);
        // общий сухой прогон по умолчанию включён в репозитории: на бою — решение оператора
        var admin = await LoginAsSuperAdminAsync();
        var policy = await J(await AuthedClient(admin.Token).GetAsync("/api/admin/retention/policy"));
        policy.GetProperty("dryRun").GetBoolean().Should().BeTrue();
    }
}
