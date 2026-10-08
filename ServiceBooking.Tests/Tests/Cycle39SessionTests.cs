using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 39, «Вызов 2»: сеанс услуги в брони дома (US-39-09, 10, 15, 18; ARCHITECTURE_CYCLE39.md §39.5.3, §39.7.2–§39.7.5): добавление гостем и персоналом (ЮР39-6), деньги брони
/// («оплата на месте», предоплата не меняется), границы проживания, лимит 5, каскад «бронь → сеансы», бесплатная отмена, бронь с услугами «или всё, или ничего».
/// </summary>
public class Cycle39SessionTests(TestDatabaseFixture fixture) : Cycle39TestBase(fixture)
{
    private sealed record Scene(StaysCtx Company, HouseCtx House, SvcCtx Svc, DateOnly CheckIn, DateOnly CheckOut, string Token, Guid BookingId);

    private async Task<Scene> SceneAsync(int nightPrice = 3000, int serviceMinLead = 0, int checkInShift = 10, int nights = 3, int? prepay = null, bool serviceForHouses = true,
        StaysTestFactory? host = null, bool publishService = true)
    {
        var company = await CreateStaysCompanyAsync(prepayPercent: prepay);
        var house = await CreateHouseAsync(company, price: nightPrice);
        var svc = await CreateServiceAsync(company, minLeadMinutes: serviceMinLead, forHouses: serviceForHouses, publish: publishService);
        var ci = InDays(checkInShift);
        var co = ci.AddDays(nights);
        var booked = await BookOkAsync(house.Id, ci, co, client: host?.Client());
        return new Scene(company, house, svc, ci, co, booked.Token, await BookingIdAsync(booked.Token));
    }

    // ── добавление гостем ────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-50")]
    public async Task GuestAddsSession_MoneyOnSite_PrepayUnchanged_LinesAndBlock()
    {
        var s = await SceneAsync(prepay: 30);
        var before = await GetPublicBookingAsync(s.Token);
        before.Sessions.Should().BeNullOrEmpty();
        before.ServicesBlock!.CanAdd.Should().BeTrue();
        var broom = await AddItemAsync(s.Company, s.Svc.Id, "Веник", 300, 5);

        var avail = await AnonymousClient().GetAsync($"/api/stays/bookings/public/{s.Token}/services");
        avail.StatusCode.Should().Be(HttpStatusCode.OK);
        var options = (await avail.Content.ReadJsonAsync<BookingServicesDto>())!;
        options.CanAdd.Should().BeTrue();
        options.Services.Should().ContainSingle(x => x.Name == "Баня");
        options.Services.Single().Dates.Should().NotBeEmpty().And.OnlyContain(d => d.BusinessDate >= s.CheckIn && d.BusinessDate < s.CheckOut.AddDays(1));

        var date = s.CheckIn.AddDays(1);
        var after = await AddSessionOkAsync(s.Token, s.Svc.Id, date, 1080, 2, [new ItemSelectionInput(broom.Id, 2)]);
        after.Sessions.Should().ContainSingle();
        var sess = after.Sessions!.Single();
        sess.State.Should().Be(StayServiceSessionState.Active);
        sess.TotalRub.Should().Be(4600);
        sess.CanCancel.Should().BeTrue();
        sess.AddedByStaff.Should().BeFalse();
        after.TotalRub.Should().Be(before.TotalRub + 4600, "услуга входит в сумму брони");
        after.PrepayRub.Should().Be(before.PrepayRub, "предоплата брони не меняется (Р39-8)");
        after.DueAtCheckInRub.Should().Be(before.DueAtCheckInRub + 4600, "«к оплате при заселении» растёт");
        after.Lines.Should().Contain(l => l.Label.Contains("Баня")).And.Contain(l => l.Label.Contains("Веник"));
        after.Lines.Sum(l => l.AmountRub).Should().Be(after.TotalRub, "Σ строк = итог");

        var charges = await WithDbAsync(db => db.StayBookingCharges.AsNoTracking().Where(c => c.StayBookingId == s.BookingId).ToListAsync());
        charges.Where(c => c.Kind is StayChargeKind.ServiceSlot or StayChargeKind.ServiceItem).Should().HaveCount(2).And.OnlyContain(c => !c.PrepayEligible);
        charges.Sum(c => c.AmountRub).Should().Be(after.TotalRub);
        (await WithDbAsync(db => db.StayBookings.AsNoTracking().SingleAsync(b => b.Id == s.BookingId))).Version.Should().BeGreaterThan(1, "сумма брони изменилась — версия растёт");
    }

    [Fact, TestCase("CY39-51")]
    public async Task Session_MustFitInsideStay_RealTime_LastNightPastMidnightAllowed()
    {
        var s = await SceneAsync(nights: 2);
        var settings = (await GetCompanyAsync(s.Company)).Settings!;
        var inMinute = TimeOnly.Parse(settings.CheckInTime).Hour * 60 + TimeOnly.Parse(settings.CheckInTime).Minute;
        var outMinute = TimeOnly.Parse(settings.CheckOutTime).Hour * 60 + TimeOnly.Parse(settings.CheckOutTime).Minute;
        async Task<HttpResponseMessage> Add(DateOnly d, int start, int hours) =>
            await AddSessionAsync(s.Token, SessionInput(s.Svc.Id, d, start, hours, 2000 * hours));
        async Task<string> RefusedCode(DateOnly d, int start, int hours)
        {
            var r = await Add(d, start, hours);
            r.StatusCode.Should().Be(HttpStatusCode.Conflict, await r.Content.ReadAsStringAsync());
            return await Code(r);
        }
        // до заселения в день заезда (время заезда 14:00): сеанс 08:00–10:00
        (await RefusedCode(s.CheckIn, 480, 2)).Should().Be("OutsideStay");
        // за день до заезда
        (await RefusedCode(s.CheckIn.AddDays(-1), 1080, 2)).Should().Be("OutsideStay");
        // после выезда в день выезда (бизнес-дата выезда, вечер)
        (await RefusedCode(s.CheckOut, Math.Max(outMinute + 60, 900) / 60 * 60, 2)).Should().Be("OutsideStay");
        // сеанс через выезд: последняя ночь, 22:00–01:00 следующего дня — закончится ДО выезда → допустим
        var last = s.CheckOut.AddDays(-1);
        var ok = await Add(last, 1320, 3);
        ok.StatusCode.Should().Be(HttpStatusCode.Created, await ok.Content.ReadAsStringAsync());
        // сеанс, который начинается до выезда и заканчивается после него — нельзя: бизнес-день выезда 08:00..., сеанс до 12:00+ (если окно позволяет)
        var crossing = Math.Max(outMinute - 60, 480) / 60 * 60;
        if (crossing + 120 > outMinute && crossing >= 480)
            (await RefusedCode(s.CheckOut, crossing, 2)).Should().Be("OutsideStay", "сеанс не должен выходить за момент выезда");
        _ = inMinute;
    }

    [Fact, TestCase("CY39-52")]
    public async Task Session_LimitFivePerBooking_CancelFreesOne_Idempotency_PriceChanged()
    {
        var s = await SceneAsync(nights: 4);
        var date = s.CheckIn.AddDays(1);
        var key = Guid.NewGuid();
        var first = await AddSessionAsync(s.Token, SessionInput(s.Svc.Id, date, 600, 2, 4000, key: key));
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var repeat = await AddSessionAsync(s.Token, SessionInput(s.Svc.Id, date, 600, 2, 4000, key: key));
        repeat.StatusCode.Should().Be(HttpStatusCode.OK, "повтор с тем же ключом — существующий сеанс");
        (await GetPublicBookingAsync(s.Token)).Sessions.Should().ContainSingle();

        var wrong = await AddSessionAsync(s.Token, SessionInput(s.Svc.Id, date, 900, 2, 3999));
        wrong.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(wrong)).Should().Be("PriceChanged");

        var slots = new[] { (1, 900), (2, 600), (2, 900), (3, 600) };
        for (var i = 0; i < slots.Length; i++)
            (await AddSessionAsync(s.Token, SessionInput(s.Svc.Id, s.CheckIn.AddDays(slots[i].Item1), slots[i].Item2, 2, 4000))).StatusCode.Should().Be(HttpStatusCode.Created, $"сеанс {i + 2}");
        var sixth = await AddSessionAsync(s.Token, SessionInput(s.Svc.Id, s.CheckIn.AddDays(3), 900, 2, 4000));
        sixth.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(sixth)).Should().Be("TooManySessions");
        (await sixth.Content.ReadAsStringAsync()).Should().Contain("не больше 5");

        var page = await GetPublicBookingAsync(s.Token);
        var cancel = await AnonymousClient().PostJsonAsync($"/api/stays/bookings/public/{s.Token}/sessions/{page.Sessions!.First().Id}/cancel", new { });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        (await AddSessionAsync(s.Token, SessionInput(s.Svc.Id, s.CheckIn.AddDays(3), 900, 2, 4000))).StatusCode.Should().Be(HttpStatusCode.Created, "отмена освободила место в лимите");
    }

    [Fact, TestCase("CY39-53")]
    public async Task Session_ServiceNotAllowed_Unpublished_OtherCompany_Foreign_Booking()
    {
        var s = await SceneAsync(serviceForHouses: false);
        var date = s.CheckIn.AddDays(1);
        var notForHouses = await AddSessionAsync(s.Token, SessionInput(s.Svc.Id, date, 600, 2, 4000));
        notForHouses.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(notForHouses)).Should().Be("ServiceNotAvailableForStays");
        (await AnonymousClient().GetAsync($"/api/stays/bookings/public/{s.Token}/services")).Content.ReadJsonAsync<BookingServicesDto>().Result!.Services.Should().BeEmpty();

        var other = await CreateStaysCompanyAsync();
        var foreign = await CreateServiceAsync(other);
        (await AddSessionAsync(s.Token, SessionInput(foreign.Id, date, 600, 2, 4000))).StatusCode.Should().Be(HttpStatusCode.NotFound, "услуга другой компании");
        (await AddSessionAsync("no-such-token-0000000000000000000000000", SessionInput(s.Svc.Id, date, 600, 2, 4000))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // не опубликованная услуга гостю недоступна; архивная — тоже
        var c = AuthedClient(s.Company.OwnerToken);
        var open = await CreateServiceAsync(s.Company, "Чан");
        (await c.PostJsonAsync($"/api/stays/companies/{s.Company.Id}/services/{open.Id}/unpublish", new EmptyInput())).StatusCode.Should().Be(HttpStatusCode.OK);
        var unpub = await AddSessionAsync(s.Token, SessionInput(open.Id, date, 600, 2, 4000));
        unpub.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(unpub)).Should().Be("ServiceNotAvailableForStays");

        // чужой токен брони: сеанс другой брони не отменить
        var s2 = await SceneAsync();
        var a = await AddSessionOkAsync(s2.Token, s2.Svc.Id, s2.CheckIn.AddDays(1), 600, 2);
        var cross = await AnonymousClient().PostJsonAsync($"/api/stays/bookings/public/{s.Token}/sessions/{a.Sessions!.Single().Id}/cancel", new { });
        cross.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Conflict);
        (await GetPublicBookingAsync(s2.Token)).Sessions!.Single().State.Should().Be(StayServiceSessionState.Active, "чужая ссылка не отменяет сеанс");
    }

    [Fact, TestCase("CY39-54")]
    public async Task GuestCancelsSession_FreeAndImmediately_TotalsRollBack_AfterStart409()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var s = await SceneAsync(host: host);
        var before = await GetPublicBookingAsync(s.Token);
        var date = s.CheckIn.AddDays(1);
        var added = await AddSessionOkAsync(s.Token, s.Svc.Id, date, 1080, 2);
        var id = added.Sessions!.Single().Id;
        var client = host.Client();
        var cancel = await client.PostJsonAsync($"/api/stays/bookings/public/{s.Token}/sessions/{id}/cancel", new { });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());
        var after = (await cancel.Content.ReadJsonAsync<PublicStayBookingDto>())!;
        after.TotalRub.Should().Be(before.TotalRub);
        after.DueAtCheckInRub.Should().Be(before.DueAtCheckInRub);
        after.Lines.Should().NotContain(l => l.Label.Contains("Баня"));
        after.Sessions!.Single().State.Should().Be(StayServiceSessionState.CancelledByGuest);
        (await ActiveSessionsAsync(s.Svc.Id)).Should().BeEmpty();
        (await StartsAsync(s.Svc.Id, date)).Starts.Select(x => x.StartMinute).Should().Contain(1080);
        var again = await client.PostJsonAsync($"/api/stays/bookings/public/{s.Token}/sessions/{id}/cancel", new { });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(again)).Should().Be("CancelNotAllowed");

        // после старта сеанс не отменить
        var second = await AddSessionOkAsync(s.Token, s.Svc.Id, date, 1080, 2);
        host.StaysClock.Set(StartUtc(date, 1080).AddMinutes(1));
        var late = await client.PostJsonAsync($"/api/stays/bookings/public/{s.Token}/sessions/{second.Sessions!.Single(x => x.State == StayServiceSessionState.Active).Id}/cancel", new { });
        late.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(late)).Should().Be("CancelNotAllowed");
        (await ActiveSessionsAsync(s.Svc.Id)).Should().HaveCount(1);
    }

    [Fact, TestCase("CY39-55")]
    public async Task AddSession_ToInactiveBooking_409_BookingNotActive_IncludingExpiredHold()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var s = await SceneAsync(prepay: 30, host: host);
        var date = s.CheckIn.AddDays(1);
        var q = 4000;

        // 1) истёкшее, но ещё не снятое задачей удержание — не активная бронь
        var held = await GetPublicBookingAsync(s.Token, host.Client());
        held.Status.Should().Be(StayBookingStatus.Held);
        host.StaysClock.Set(held.HoldExpiresAtUtc!.Value.AddSeconds(2));
        var expiredAdd = await host.Client().PostJsonAsync($"/api/stays/bookings/public/{s.Token}/sessions", SessionInput(s.Svc.Id, date, 600, 2, q));
        expiredAdd.StatusCode.Should().Be(HttpStatusCode.Conflict, await expiredAdd.Content.ReadAsStringAsync());
        (await Code(expiredAdd)).Should().Be("BookingNotActive");
        (await ActiveSessionsAsync(s.Svc.Id)).Should().BeEmpty("активного сеанса у неактивной брони нет");
        var blocked = await GetPublicBookingAsync(s.Token, host.Client());
        blocked.ServicesBlock!.CanAdd.Should().BeFalse("кнопка «Добавить услугу» недоступна для истёкшего удержания");

        // 2) отменённая бронь
        var s2 = await SceneAsync(host: host);
        (await host.Client().PostJsonAsync($"/api/stays/bookings/public/{s2.Token}/cancel", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelledAdd = await host.Client().PostJsonAsync($"/api/stays/bookings/public/{s2.Token}/sessions", SessionInput(s2.Svc.Id, s2.CheckIn.AddDays(1), 600, 2, q));
        cancelledAdd.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(cancelledAdd)).Should().Be("BookingNotActive");

        // 3) время выезда наступило
        var s3 = await SceneAsync(host: host);
        host.StaysClock.Set(StartUtc(s3.CheckOut, 12 * 60).AddMinutes(30));
        var late = await host.Client().PostJsonAsync($"/api/stays/bookings/public/{s3.Token}/sessions", SessionInput(s3.Svc.Id, s3.CheckOut, 600, 2, q));
        late.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(late)).Should().BeOneOf("BookingNotActive", "DateInPast", "OutsideStay");
    }

    // ── каскад: бронь → сеансы ───────────────────────────────────────────────────

    [Fact, TestCase("CY39-56")]
    public async Task HeldBooking_SessionRelativeToBooking_ReleasedAtTimerWithBooking_SameTransaction()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var s = await SceneAsync(prepay: 30, host: host);
        var date = s.CheckIn.AddDays(1);
        var added = await AddSessionOkAsync(s.Token, s.Svc.Id, date, 600, 2);
        added.ServicesBlock!.Hint.Should().Be("Сеанс сохранится, если бронь будет оплачена", "подсказка для удержанной брони");

        var holdUntil = (await GetPublicBookingAsync(s.Token, host.Client())).HoldExpiresAtUtc!.Value;
        host.StaysClock.Set(holdUntil.AddSeconds(1));
        await host.RunTaskAsync("stays-hold-expiry");
        var page = await GetPublicBookingAsync(s.Token, host.Client());
        page.Status.Should().Be(StayBookingStatus.ExpiredUnpaid);
        var all = await AllSessionsAsync(s.Svc.Id);
        all.Should().ContainSingle().Which.State.Should().Be(StayServiceSessionState.ReleasedWithBooking);
        all.Single().ReleasedAtUtc.Should().NotBeNull();
        (await ActiveSessionsAsync(s.Svc.Id)).Should().BeEmpty();
        (await WithDbAsync(db => db.StayBookingEvents.AsNoTracking().Where(e => e.StayBookingId == s.BookingId).Select(e => e.Kind.ToString()).ToListAsync()))
            .Should().Contain("ServiceSessionsReleased");
        (await StartsAsync(s.Svc.Id, date)).Starts.Select(x => x.StartMinute).Should().Contain(600, "время освободилось вместе с бронью");
    }

    [Theory, TestCase("CY39-57")]
    [InlineData("guest-cancel")]
    [InlineData("staff-reject")]
    [InlineData("staff-cancel")]
    public async Task EveryTerminalPathOfBooking_ReleasesItsSessions(string path)
    {
        var s = await SceneAsync(prepay: 30);
        var date = s.CheckIn.AddDays(1);
        await AddSessionOkAsync(s.Token, s.Svc.Id, date, 600, 2);
        await AddSessionOkAsync(s.Token, s.Svc.Id, date, 1080, 2);
        (await ActiveSessionsAsync(s.Svc.Id)).Should().HaveCount(2);
        switch (path)
        {
            case "guest-cancel":
                (await AnonymousClient().PostJsonAsync($"/api/stays/bookings/public/{s.Token}/cancel", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
                break;
            case "staff-reject":
                await AttachProofOkAsync(s.Token);
                await StaffActionOkAsync(s.Company, s.BookingId, "reject-payment", (await StaffCardAsync(s.Company, s.BookingId)).Version, "Оплата не пришла");
                break;
            default:
                await StaffActionOkAsync(s.Company, s.BookingId, "cancel", (await StaffCardAsync(s.Company, s.BookingId)).Version, "Авария");
                break;
        }
        (await ActiveSessionsAsync(s.Svc.Id)).Should().BeEmpty("конечный статус брони освобождает сеансы в той же транзакции");
        (await AllSessionsAsync(s.Svc.Id)).Should().OnlyContain(x => x.State == StayServiceSessionState.ReleasedWithBooking && x.ReleasedAtUtc != null);
        var nights = await WithDbAsync(db => db.HouseOccupancies.AsNoTracking().CountAsync(o => o.HouseId == s.House.Id && o.ReleasedAtUtc == null));
        nights.Should().Be(0, "ночи освобождены тем же путём");
    }

    // ── персонал ─────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-58")]
    public async Task StaffAddsSession_BasisRequired_NoMinLead_Unpublished_GuestSeesNoteAndCancelsFree()
    {
        var s = await SceneAsync(serviceMinLead: 2880, checkInShift: 1, nights: 3, publishService: true);
        var c = AuthedClient(s.Company.OwnerToken);
        var date = s.CheckIn;
        var start = 20 * 60; // 20:00 бизнес-дня заезда — меньше чем через 48 часов от «сейчас»
        // гостю: минимальное время до начала 48 ч → отказ
        var guest = await AddSessionAsync(s.Token, SessionInput(s.Svc.Id, date, start, 2, 4000));
        guest.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(guest)).Should().Be("TooEarly");

        StaffAddSessionInput Staff(StayServiceRequestBasis? basis, Guid? key = null) => new(s.Svc.Id, date, start, 2, [], basis, key ?? Guid.NewGuid());
        var noBasis = await c.PostJsonAsync($"/api/stays/companies/{s.Company.Id}/bookings/{s.BookingId}/sessions", Staff(null));
        noBasis.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noBasis.Content.ReadAsStringAsync()).Should().Contain("как гость попросил услугу");

        var key = Guid.NewGuid();
        var ok = await c.PostJsonAsync($"/api/stays/companies/{s.Company.Id}/bookings/{s.BookingId}/sessions", Staff(StayServiceRequestBasis.Phone, key));
        ok.StatusCode.Should().Be(HttpStatusCode.Created, await ok.Content.ReadAsStringAsync());
        var card = (await ok.Content.ReadJsonAsync<StaffStayBookingCardDto>())!;
        card.Sessions.Should().ContainSingle().Which.AddedByText.Should().NotBeNullOrWhiteSpace();
        (await c.PostJsonAsync($"/api/stays/companies/{s.Company.Id}/bookings/{s.BookingId}/sessions", Staff(StayServiceRequestBasis.Phone, key))).StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await GetPublicBookingAsync(s.Token);
        var sess = page.Sessions!.Single();
        sess.AddedByStaff.Should().BeTrue();
        sess.AddedByStaffText.Should().ContainEquivalentOf("по вашей просьбе").And.Contain("отмен");
        sess.CanCancel.Should().BeTrue("добавленный персоналом сеанс гость отменяет бесплатно");

        // события в журнале брони с автором и основанием
        var events = await WithDbAsync(db => db.StayBookingEvents.AsNoTracking().Where(e => e.StayBookingId == s.BookingId && e.ServiceSessionId != null).ToListAsync());
        events.Should().NotBeEmpty();
        var stored = (await AllSessionsAsync(s.Svc.Id)).Single();
        stored.RequestBasis.Should().Be(StayServiceRequestBasis.Phone);
        stored.AddedByKind.Should().Be(StayActorKind.Staff);

        // персонал отменяет сеанс: причина обязательна, гость её видит, итоги откатываются
        var sessionRow = card.Sessions!.Single();
        var cancel = await c.PostJsonAsync($"/api/stays/companies/{s.Company.Id}/service-sessions/{sessionRow.Id}/cancel", new ExpectedVersionReasonInput(sessionRow.Version, ""));
        cancel.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var done = await c.PostJsonAsync($"/api/stays/companies/{s.Company.Id}/service-sessions/{sessionRow.Id}/cancel", new ExpectedVersionReasonInput(sessionRow.Version, "Баня сломалась"));
        done.StatusCode.Should().Be(HttpStatusCode.OK, await done.Content.ReadAsStringAsync());
        var after = await GetPublicBookingAsync(s.Token);
        after.Sessions!.Single().State.Should().Be(StayServiceSessionState.CancelledByOwner);
        after.Sessions!.Single().StatusReason.Should().Be("Баня сломалась");
        after.TotalRub.Should().BeLessThan(page.TotalRub);
        (await c.PostJsonAsync($"/api/stays/companies/{s.Company.Id}/service-sessions/{sessionRow.Id}/cancel", new ExpectedVersionReasonInput(sessionRow.Version, "ещё раз")))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("CY39-59")]
    public async Task StaffAddSession_Permissions_ManagerAllowed_HousekeeperForbidden_ArchivedAnd404()
    {
        var s = await SceneAsync();
        var manager = await AddStaffAsync(s.Company, "Manager");
        var housekeeper = await AddStaffAsync(s.Company, "Housekeeper");
        var date = s.CheckIn.AddDays(1);
        StaffAddSessionInput Body(int start) => new(s.Svc.Id, date, start, 2, [], StayServiceRequestBasis.InPerson, Guid.NewGuid());
        var url = $"/api/stays/companies/{s.Company.Id}/bookings/{s.BookingId}/sessions";
        (await AuthedClient(housekeeper.Token).PostJsonAsync(url, Body(600))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AnonymousClient().PostJsonAsync(url, Body(600))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AuthedClient(manager.Token).PostJsonAsync(url, Body(600))).StatusCode.Should().Be(HttpStatusCode.Created);
        var stranger = await RegisterAsync();
        (await AuthedClient(stranger.Token).PostJsonAsync(url, Body(900))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // чужая бронь в своей компании
        var other = await SceneAsync();
        var foreignUrl = $"/api/stays/companies/{s.Company.Id}/bookings/{other.BookingId}/sessions";
        (await AuthedClient(s.Company.OwnerToken).PostJsonAsync(foreignUrl, Body(900))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // архивная услуга
        var arch = await CreateServiceAsync(s.Company, "Чан");
        await AuthedClient(s.Company.OwnerToken).PostJsonAsync($"/api/stays/companies/{s.Company.Id}/services/{arch.Id}/archive", new EmptyInput());
        (await AuthedClient(s.Company.OwnerToken).PostJsonAsync(url, Body(900) with { ServiceId = arch.Id })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── бронь дома с услугами (форма, US-39-10) ──────────────────────────────────

    [Fact, TestCase("CY39-60")]
    public async Task BookingForm_NothingPreselected_ServicesQuoted_And_CreatedAtomically()
    {
        var company = await CreateStaysCompanyAsync(prepayPercent: 30);
        var house = await CreateHouseAsync(company, price: 3000);
        var svc = await CreateServiceAsync(company);
        var ci = InDays(12);
        var co = ci.AddDays(3);

        // по умолчанию услуг нет: расчёт без services[] — ответ как в цикле 37
        var plain = await QuoteAsync(house.Id, ci, co);
        (plain.Services ?? []).Should().BeEmpty();

        var sel = new StayServiceSelectionInput(svc.Id, ci.AddDays(1), 1080, 2, []);
        var q = await AnonymousClient().PostJsonAsync($"/api/stays/public/houses/{house.Id}/quote", new StayQuoteInput(ci, co, 2, 0, 0, false, [sel]));
        q.StatusCode.Should().Be(HttpStatusCode.OK, await q.Content.ReadAsStringAsync());
        var quote = (await q.Content.ReadJsonAsync<StayQuoteDto>())!;
        quote.Services.Should().ContainSingle().Which.Quote.TotalRub.Should().Be(4000);
        quote.TotalRub.Should().Be(plain.TotalRub + 4000, "в итог входят услуги");
        quote.PrepayRub.Should().Be(plain.PrepayRub, "предоплата — только дом");
        quote.DueAtCheckInRub.Should().Be(plain.DueAtCheckInRub + 4000);

        var create = await PostBookingAsync(house.Id, Booking(ci, co, quote.TotalRub) with { Services = [sel] });
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var booked = (await create.Content.ReadJsonAsync<CreateStayBookingResponse>())!;
        booked.Booking.Sessions.Should().ContainSingle();
        booked.Booking.TotalRub.Should().Be(quote.TotalRub);
        (await ActiveSessionsAsync(svc.Id)).Should().ContainSingle();
    }

    [Fact, TestCase("CY39-61")]
    public async Task BookingForm_TakenSlot_RefusesWholeBooking_NoBookingNoNights_ServiceIndex()
    {
        var company = await CreateStaysCompanyAsync(prepayPercent: 30);
        var house = await CreateHouseAsync(company, price: 3000);
        var svc = await CreateServiceAsync(company);
        var ci = InDays(12);
        var co = ci.AddDays(3);
        await EnableOrdersWithoutStayAsync(company);
        await OrderOkAsync(svc.Id, ci.AddDays(1), 1080, 2); // время занято чужим заказом

        var free = new StayServiceSelectionInput(svc.Id, ci.AddDays(1), 600, 2, []);
        var taken = new StayServiceSelectionInput(svc.Id, ci.AddDays(1), 1080, 2, []);
        var plain = await QuoteAsync(house.Id, ci, co);
        var r = await PostBookingAsync(house.Id, Booking(ci, co, plain.TotalRub + 8000) with { Services = [free, taken] });
        r.StatusCode.Should().Be(HttpStatusCode.Conflict, await r.Content.ReadAsStringAsync());
        var body = await J(r);
        body.GetProperty("code").GetString().Should().Be("ServiceSlotUnavailable");
        body.GetProperty("serviceIndex").GetInt32().Should().Be(1);
        (await WithDbAsync(db => db.StayBookings.CountAsync(b => b.HouseId == house.Id))).Should().Be(0, "бронь без заявленного сеанса не создаётся");
        (await WithDbAsync(db => db.HouseOccupancies.CountAsync(o => o.HouseId == house.Id))).Should().Be(0);
        (await ActiveSessionsAsync(svc.Id)).Should().HaveCount(1, "только чужой заказ; сеанс «free» тоже не создан");

        // «бронь без услуги» после отказа создаётся
        (await PostBookingAsync(house.Id, Booking(ci, co, plain.TotalRub))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact, TestCase("CY39-62")]
    public async Task BookingForm_MoreThanThree_OutsideStay_SameSlotTwice_Refused()
    {
        var company = await CreateStaysCompanyAsync(prepayPercent: 30);
        var house = await CreateHouseAsync(company, price: 3000);
        var svc = await CreateServiceAsync(company);
        var ci = InDays(12);
        var co = ci.AddDays(3);
        var plain = await QuoteAsync(house.Id, ci, co);
        StayServiceSelectionInput Sel(int dayShift, int start) => new(svc.Id, ci.AddDays(dayShift), start, 2, []);
        async Task<(HttpStatusCode, string)> Try(params StayServiceSelectionInput[] services)
        {
            var r = await PostBookingAsync(house.Id, Booking(ci, co, plain.TotalRub + 4000 * services.Length) with { Services = services.ToList() });
            return (r.StatusCode, await r.Content.ReadAsStringAsync());
        }
        var four = await Try(Sel(1, 480), Sel(1, 720), Sel(1, 960), Sel(1, 1200));
        four.Item1.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
        four.Item2.Should().Contain("3");
        var outside = await Try(Sel(-1, 480));
        outside.Item1.Should().Be(HttpStatusCode.Conflict);
        var duplicate = await Try(Sel(1, 600), Sel(1, 600));
        duplicate.Item1.Should().Be(HttpStatusCode.Conflict, "два одинаковых времени в одном запросе — второе занято первым");
        (await WithDbAsync(db => db.StayBookings.CountAsync(b => b.HouseId == house.Id))).Should().Be(0);
        (await ActiveSessionsAsync(svc.Id)).Should().BeEmpty();
    }

    // ── шахматка, День услуг, график ─────────────────────────────────────────────

    [Fact, TestCase("CY39-63")]
    public async Task Board_ServiceGroup_ServiceDay_And_Schedule_HousekeeperSeesNoPhoneNoMoney()
    {
        var s = await SceneAsync(prepay: null);
        await EnableOrdersWithoutStayAsync(s.Company);
        var broom = await AddItemAsync(s.Company, s.Svc.Id, "Веник берёзовый", 300, 5);
        var housekeeper = await AddStaffAsync(s.Company, "Housekeeper");
        var date = s.CheckIn.AddDays(1);
        await AddSessionOkAsync(s.Token, s.Svc.Id, date, 1320, 3, [new ItemSelectionInput(broom.Id, 2)]); // 22:00 – 01:00, ночь через полночь
        await AttachProofOkAsync(s.Token);
        await StaffActionOkAsync(s.Company, s.BookingId, "confirm-payment", (await StaffCardAsync(s.Company, s.BookingId)).Version); // «Удержан» в график не попадает
        var standalone = await OrderOkAsync(s.Svc.Id, date, 600, 2, name: "Ольга Ночная");
        var c = AuthedClient(s.Company.OwnerToken);

        var board = (await (await c.GetAsync($"/api/stays/companies/{s.Company.Id}/board?from={D(s.CheckIn)}&days=7")).Content.ReadJsonAsync<StaysBoardDto>())!;
        board.Services.Should().ContainSingle(x => x.Id == s.Svc.Id);
        var cell = board.ServiceCells!.Single(x => x.ServiceId == s.Svc.Id && x.BusinessDate == date);
        cell.Count.Should().Be(2);
        cell.FirstStartLabel.Should().Contain("10:00");
        cell.CrossesMidnightLabel.Should().NotBeNull("сеанс через полночь стоит в ячейке дня начала с пометкой «до 01:00»").And.Contain("01:00");

        var dayResp = await c.GetAsync($"/api/stays/companies/{s.Company.Id}/service-day?date={D(date)}");
        dayResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var day = (await dayResp.Content.ReadJsonAsync<ServiceDayDto>())!;
        day.Axis.MidnightMinute.Should().Be(1440);
        day.Axis.FromMinute.Should().BeGreaterOrEqualTo(360).And.BeLessThanOrEqualTo(600);
        var line = day.Services.Single(x => x.Id == s.Svc.Id);
        line.Bars.Should().Contain(b => b.Kind == ServiceDayBarKind.Session && b.StartMinute == 1320 && b.EndMinute == 1500);
        line.Bars.Should().Contain(b => b.Kind == ServiceDayBarKind.Buffer && b.StartMinute == 1500 && b.EndMinute == 1530, "зазор — отдельная полоса «подготовка»");
        line.Bars.Where(b => b.Kind == ServiceDayBarKind.Session).Should().OnlyContain(b => b.Label.Length > 0);
        line.Bars.Where(b => b.Kind == ServiceDayBarKind.Session && b.StartMinute == 600).Should().ContainSingle().Which.Label.Should().Contain("без проживания");

        // график: горничная — без телефона, сумм, оплат
        var hk = AuthedClient(housekeeper.Token);
        var sched = await hk.GetAsync($"/api/stays/companies/{s.Company.Id}/schedule?from={D(s.CheckIn)}&days=7");
        sched.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await sched.Content.ReadAsStringAsync();
        var schedule = System.Text.Json.JsonSerializer.Deserialize<StaysScheduleDto>(raw, JsonHelpers.Options)!;
        var sessions = schedule.Days.Single(d => d.Date == date).Sessions!;
        sessions.Should().HaveCount(2);
        var night = sessions.Single(x => x.HouseName != null);
        night.ServiceName.Should().Be("Баня");
        night.TimeLabel.Should().Contain("22:00").And.Contain("01:00");
        night.PreparedUntilLabel.Should().Contain("01:30");
        night.Items.Should().ContainSingle(i => i.Name == "Веник берёзовый" && i.Quantity == 2);
        sessions.Single(x => x.HouseName == null).GuestName.Should().Be("Ольга Ночная");
        raw.Should().NotContain("+7900").And.NotContain("guestPhone").And.NotContain("totalRub").And.NotContain("prepay").And.NotContain("paymentProofs");
        night.Comment.Should().BeNull("горничная не видит комментарий без настройки HousekeeperSeesGuestComment");
        _ = standalone;
    }

    [Fact, TestCase("CY39-64")]
    public async Task Schedule_HeldStandaloneSessions_NotInScheduleUntilPaid_NoDuplicatesOnStaffSide()
    {
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company, prepay: 30);
        var date = InDays(9);
        var order = await OrderOkAsync(svc.Id, date, 720, 2);
        var sched = await ScheduleAsync(company, from: date, days: 3);
        (sched.Days.Single(d => d.Date == date).Sessions ?? []).Should().BeEmpty("«Удержан» в график не попадает (как брони домов)");
        await AttachOrderProofAsync(order.Token);
        var sessionId = await SessionIdOfOrderAsync(order.Token);
        var card = await SessionCardAsync(company, sessionId);
        await SessionActionAsync(company, sessionId, "confirm-payment", card.Version);
        sched = await ScheduleAsync(company, from: date, days: 3);
        sched.Days.Single(d => d.Date == date).Sessions.Should().ContainSingle();
    }
}
