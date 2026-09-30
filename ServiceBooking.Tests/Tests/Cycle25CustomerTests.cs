using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Reports;
using ServiceBooking.API.Services.Retention;
using ServiceBooking.API.Services.Retention.Rules;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 25, «Вызов 2», блок E: карточка покупателя, заметка, ПДн (US-25-09, US-25-10, US-25-11).
/// API_CONTRACT_CYCLE25.md §529–§530, §535. Покупатель определяется телефоном ВНУТРИ магазина; в адресе — id заказа, не телефон.
/// </summary>
public class Cycle25CustomerTests(TestDatabaseFixture fixture) : Cycle25TestBase(fixture)
{
    // ── US-25-09: карточка ─────────────────────────────────────────────────────────

    [Fact, TestCase("CY25-30")]
    public async Task Card_MergesAccountAndGuestOrdersOfOneShopOnly_StatsNameVerifiedMark_PagedNewestFirst()
    {
        var shop = await CreateRoundClockShopAsync();
        var other = await CreateRoundClockShopAsync();
        var p = await CreateProductAsync(shop, "Шаурма", 250m);
        var po = await CreateProductAsync(other, "Пицца", 500m);

        var buyer = await RegisterAsync();
        await MarkPhoneVerifiedAsync(buyer.Phone, buyer.UserId);
        var phone = buyer.Phone;

        // заказ из аккаунта с подтверждённым номером, потом гостевой с тем же номером и другим именем, и заказ в чужом магазине
        var fromAccount = await PlaceStaffViewAsync(shop, [Line(p, 1)], phone: phone, name: "Пётр", buyerToken: buyer.Token);
        fromAccount.CustomerKind.Should().Be(OrderActorKind.Customer);
        var guest = await PlaceStaffViewAsync(shop, [Line(p, 2)], phone: phone, name: "Пётр Иванович");
        await PlaceStaffViewAsync(other, [Line(po, 1)], phone: phone, name: "Пётр в другом магазине");
        await DriveAsync(shop, fromAccount, "accept", "ready", "issue");
        var cancelled = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)], name: "Пётр Иванович", phone: phone));
        await CancelByCustomerAsync(cancelled.Order.Token);

        var card = await CardOkAsync(shop, guest.Id);
        card.CustomerRef.Should().Be(guest.Id);
        card.Phone.Should().Be("+" + phone.TrimStart('+'), "E.164 для ссылки tel:");
        card.PhoneDisplay.Should().MatchRegex(@"^\+7 9\d\d \d\d\d-\d\d-\d\d$");
        card.Name.Should().Be("Пётр Иванович", "имя — из последнего заказа");
        card.PhoneVerified.Should().BeTrue("есть заказ из аккаунта с подтверждённым номером");
        card.OrdersTotal.Should().Be(3, "заказы аккаунта и гостевые сведены; чужого магазина нет");
        card.IssuedCount.Should().Be(1);
        card.IssuedAmount.Should().Be(250m);
        card.CancelledByCustomer.Should().Be(1);
        card.NotPickedUp.Should().Be(0);
        card.FirstOrderDate.Should().Be((await WorkingDayAsync(shop)));
        card.LastOrderDate.Should().Be((await WorkingDayAsync(shop)));
        card.StatsText.Should().MatchRegex(@"^3 заказа · выдано 1 на 250\s₽ · отменено покупателем 1$", "нулевые счётчики в текст не входят");
        card.Orders.PageSize.Should().Be(20);
        card.Orders.TotalCount.Should().Be(3);
        card.Orders.Items.Should().OnlyContain(i => i.CustomerPhoneMasked == null, "в списке карточки телефона нет — он в шапке");
        card.Orders.Items.Select(i => i.OrderId).Should().BeEquivalentTo(new[] { fromAccount.Id, guest.Id, (await GetStaffOrderAsync(shop, cancelled.Order.Token)).Id });
        card.Orders.Items.Should().BeInDescendingOrder(i => i.PickupStartUtc, "новые сверху");
        card.Note.Should().BeNull();

        // адресуется id ЛЮБОГО заказа покупателя: карточка одна и та же
        var same = await CardOkAsync(shop, fromAccount.Id);
        same.OrdersTotal.Should().Be(3);
        same.Phone.Should().Be(card.Phone);

        // постраничность
        var page2 = await CardAsync(shop, guest.Id, query: "?page=2");
        page2.StatusCode.Should().Be(HttpStatusCode.OK);
        var p2 = (await page2.Content.ReadJsonAsync<ShopCustomerCardDto>())!;
        p2.Orders.Items.Should().BeEmpty();
        p2.Orders.TotalCount.Should().Be(3);
        (await CardAsync(shop, guest.Id, query: "?page=0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // «номер подтверждён» — только положительная отметка: у гостя без аккаунта её нет
        var stranger = await PlaceStaffViewAsync(shop, [Line(p, 1)], phone: UniquePhone(), name: "Гость");
        (await CardOkAsync(shop, stranger.Id)).PhoneVerified.Should().BeFalse();
    }

    [Fact, TestCase("CY25-31")]
    public async Task Note_CreateEditDelete_TrimAndLimit_LastAuthorShown_NotVisibleToCustomerNorOtherShop()
    {
        var a = await CreateRoundClockShopAsync();
        var b = await CreateRoundClockShopAsync();
        var staff = await AddShopStaffAsync(a);
        var pa = await CreateProductAsync(a);
        var pb = await CreateProductAsync(b);
        var phone = UniquePhone();
        var orderA = await PlaceStaffViewAsync(a, [Line(pa, 1)], phone: phone);
        var orderB = await PlaceStaffViewAsync(b, [Line(pb, 1)], phone: phone);
        (await GetNoteOkAsync(a, orderA.Id)).Note.Should().BeNull();

        // владелец пишет; сотрудник видит, правит; видно, кто и когда изменил последним
        var put = await PutNoteAsync(a, orderA.Id, "  всегда без лука  ");
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var note = (await put.Content.ReadJsonAsync<ShopCustomerNoteStateDto>())!.Note!;
        note.Text.Should().Be("всегда без лука", "текст обрезается по краям");
        note.UpdatedByName.Should().Be("Test User");
        note.UpdatedText.Should().StartWith("Изменено: Test User, ");
        note.UpdatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        (await GetNoteOkAsync(a, orderA.Id, staff.Token)).Note!.Text.Should().Be("всегда без лука");
        var byStaff = await PutNoteAsync(a, orderA.Id, "звонить перед выдачей", staff.Token);
        byStaff.StatusCode.Should().Be(HttpStatusCode.OK);
        var edited = (await CardOkAsync(a, orderA.Id)).Note!;
        edited.Text.Should().Be("звонить перед выдачей", "одна заметка на покупателя: правка заменяет");
        edited.UpdatedByName.Should().Be($"{staff.FirstName} {staff.LastName}".Trim());
        (await WithDbAsync(db => db.ShopCustomerNotes.CountAsync(n => n.CompanyId == a.Id))).Should().Be(1);

        // заметку магазина А не видит магазин Б по тому же телефону
        (await GetNoteOkAsync(b, orderB.Id)).Note.Should().BeNull();
        (await CardOkAsync(b, orderB.Id)).Note.Should().BeNull();
        (await WithDbAsync(db => db.ShopCustomerNotes.CountAsync(n => n.CompanyId == b.Id))).Should().Be(0);

        // покупатель заметку не видит нигде
        var placed = await PlaceOrderAsync(a.Slug, Guest([Line(pa, 1)], phone: phone));
        var pub = await AnonymousClient().GetAsync($"/api/orders/public/{placed.Order.Token}");
        (await pub.Content.ReadAsStringAsync()).Should().NotContain("звонить перед выдачей");
        var buyer = await RegisterAsync(phone: phone);
        var mine = await AuthedClient(buyer.Token).GetAsync("/api/orders/my");
        (await mine.Content.ReadAsStringAsync()).Should().NotContain("звонить перед выдачей");
        var export = await AuthedClient(buyer.Token).GetAsync("/api/profile/export");
        (await export.Content.ReadAsStringAsync()).Should().NotContain("звонить перед выдачей", "в выгрузке текста заметки нет никогда");

        // предел 1000 символов (после обрезки пробелов)
        var maxNote = new string('я', 1000);
        (await PutNoteAsync(a, orderA.Id, maxNote)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PutNoteAsync(a, orderA.Id, "  " + maxNote + "   ")).StatusCode.Should().Be(HttpStatusCode.OK, "пробелы по краям в лимит не входят");
        var tooLong = await PutNoteAsync(a, orderA.Id, maxNote + "я");
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await tooLong.Content.ReadAsStringAsync()).Should().Contain("Заметка — не длиннее 1000 символов");
        (await GetNoteOkAsync(a, orderA.Id)).Note!.Text.Should().Be(maxNote, "неудачная запись заметку не меняет");

        // стереть текст — значит удалить заметку
        foreach (var empty in new string?[] { "", "   ", null })
        {
            (await PutNoteAsync(a, orderA.Id, "снова")).StatusCode.Should().Be(HttpStatusCode.OK);
            var cleared = await PutNoteAsync(a, orderA.Id, empty);
            cleared.StatusCode.Should().Be(HttpStatusCode.OK);
            (await cleared.Content.ReadJsonAsync<ShopCustomerNoteStateDto>())!.Note.Should().BeNull();
            (await WithDbAsync(db => db.ShopCustomerNotes.CountAsync(n => n.CompanyId == a.Id))).Should().Be(0);
        }
        (await PutNoteAsync(a, orderA.Id, null)).StatusCode.Should().Be(HttpStatusCode.OK, "удаление отсутствующей заметки — не ошибка");
    }

    [Fact, TestCase("CY25-32")]
    public async Task Note_TwoStaffCreateTheFirstNoteAtOnce_BothSucceed_ExactlyOneNoteRemains()
    {
        var shop = await CreateRoundClockShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var p = await CreateProductAsync(shop);
        var order = await PlaceStaffViewAsync(shop, [Line(p, 1)]);

        var results = await Task.WhenAll(
            PutNoteAsync(shop, order.Id, "от владельца", shop.OwnerToken),
            PutNoteAsync(shop, order.Id, "от сотрудника", staff.Token),
            PutNoteAsync(shop, order.Id, "от владельца, вторая", shop.OwnerToken));
        results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK, "последняя запись побеждает, ошибки нет");
        (await WithDbAsync(db => db.ShopCustomerNotes.CountAsync(n => n.CompanyId == shop.Id))).Should().Be(1);
        (await GetNoteOkAsync(shop, order.Id)).Note!.Text.Should().BeOneOf("от владельца", "от сотрудника", "от владельца, вторая");
    }

    // ── US-25-11: ПДн ──────────────────────────────────────────────────────────────

    [Fact, TestCase("CY25-33")]
    public async Task Phone_NeverAppearsInAnyRequestPathOfTheNewRoutes_LogsIncluded()
    {
        var shop = await CreateRoundClockShopAsync();
        var p = await CreateProductAsync(shop);
        var phone = UniquePhone();
        var digits = phone.TrimStart('+');
        var order = await PlaceStaffViewAsync(shop, [Line(p, 1)], phone: phone, name: "Марина");

        // поиск по телефону — только в теле POST; карточка и заметка — по id заказа
        (await HistoryOkAsync(shop, new { customer = digits[^7..] })).TotalCount.Should().Be(1);
        (await HistoryOkAsync(shop, new { customer = phone })).TotalCount.Should().Be(1);
        (await CardAsync(shop, order.Id)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PutNoteAsync(shop, order.Id, "заметка")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AuthedClient(shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/customers/{order.Id}/note")).StatusCode.Should().Be(HttpStatusCode.OK);

        // телефон в пути или в query «карточки» невозможен: маршрут принимает только Guid (проба — с чужими цифрами, чтобы не пачкать проверяемый журнал)
        (await AuthedClient(shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/customers/89990000000")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AuthedClient(shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/order-history?customer=89990000000")).StatusCode
            .Should().BeOneOf([HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed]);

        // журналы доступа и приложения: ни одной строки запроса, где в пути оказались бы цифры этого телефона
        await Task.Delay(300);
        var lines = ReadLogLines().Where(l => l.Contains("/api/shops/") && l.Contains("RequestPath")).ToList();
        lines.Should().NotBeEmpty("журнал запросов ведётся");
        lines.Where(l => l.Contains(digits[^9..])).Should().BeEmpty("телефон не попадает в адреса и логи");
    }

    private IEnumerable<string> ReadLogLines()
    {
        var directory = Factory.Identity.LogDirectory;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line) yield return line;
        }
    }

    [Fact, TestCase("CY25-34")]
    public async Task Export_HasNoteFactWithoutText_OnlyForVerifiedPhone_AndMaxLinkWithoutChatId()
    {
        var shopA = await CreateRoundClockShopAsync();
        var shopB = await CreateRoundClockShopAsync();
        var pa = await CreateProductAsync(shopA);
        var pb = await CreateProductAsync(shopB);

        var verified = await RegisterAsync();
        await MarkPhoneVerifiedAsync(verified.Phone, verified.UserId);
        var oa = await PlaceStaffViewAsync(shopA, [Line(pa, 1)], phone: verified.Phone);
        var ob = await PlaceStaffViewAsync(shopB, [Line(pb, 1)], phone: verified.Phone);
        (await PutNoteAsync(shopA, oa.Id, "секрет-А: аллергия на орехи")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PutNoteAsync(shopB, ob.Id, "секрет-Б")).StatusCode.Should().Be(HttpStatusCode.OK);

        var unverified = await RegisterAsync();
        var oc = await PlaceStaffViewAsync(shopA, [Line(pa, 1)], phone: unverified.Phone);
        (await PutNoteAsync(shopA, oc.Id, "секрет-В")).StatusCode.Should().Be(HttpStatusCode.OK);

        var export = await J(await AuthedClient(verified.Token).GetAsync("/api/profile/export"));
        var notes = export.GetProperty("shopCustomerNotes").EnumerateArray().ToList();
        notes.Select(n => n.GetProperty("shopName").GetString()).Should().BeEquivalentTo(new[] { shopA.Shop.Name, shopB.Shop.Name });
        notes.All(n => n.EnumerateObject().Select(x => x.Name).OrderBy(x => x).SequenceEqual(new[] { "shopName", "updatedAtUtc" })).Should().BeTrue("факт заметки: магазин и дата, без текста");
        export.GetRawText().Should().NotContain("секрет-").And.NotContain("аллергия");
        export.GetProperty("staffMaxLink").ValueKind.Should().Be(JsonValueKind.Null, "привязки MAX нет");

        // неподтверждённый номер: заметок в выгрузке нет (не оракул по чужому номеру)
        var exportUnverified = await J(await AuthedClient(unverified.Token).GetAsync("/api/profile/export"));
        exportUnverified.GetProperty("shopCustomerNotes").GetArrayLength().Should().Be(0);
        exportUnverified.GetRawText().Should().NotContain("секрет-В");

        // привязка MAX персонала: статус и даты, без идентификатора чата
        await using var mx = new StaffMaxTestFactory(ConnectionString);
        mx.EnsureWebhookSubscribed();
        var ownerClient = ClientOn(mx, shopA.OwnerToken);
        var session = (await (await ownerClient.PostAsync("/api/staff-max/link-sessions", null)).Content.ReadJsonAsync<ServiceBooking.API.DTOs.StaffMax.StaffMaxLinkSessionDto>())!;
        var payload = System.Web.HttpUtility.ParseQueryString(new Uri(session.DeepLink).Query)["start"];
        var chat = "chat25-export-" + Guid.NewGuid().ToString("N")[..10];
        await ownerClient.PostAsJsonAsync($"/api/phone-verification/max/webhook/{StaffMaxTestFactory.WebhookToken}",
            new { update_type = "bot_started", payload, user = new { user_id = "export-user" }, chat = new { chat_id = chat } });

        var ownerExport = await J(await AuthedClient(shopA.OwnerToken).GetAsync("/api/profile/export"));
        var link = ownerExport.GetProperty("staffMaxLink");
        link.GetProperty("status").GetString().Should().Be("Active");
        link.TryGetProperty("linkedAtUtc", out var linkedAt).Should().BeTrue();
        linkedAt.ValueKind.Should().Be(JsonValueKind.String);
        link.TryGetProperty("stoppedAtUtc", out _).Should().BeTrue();
        ownerExport.GetRawText().Should().NotContain(chat).And.NotContainEquivalentOf("chatKey").And.NotContainEquivalentOf("chatIdCiphertext");
        link.EnumerateObject().Select(x => x.Name).Should().BeEquivalentTo(new[] { "status", "linkedAtUtc", "stoppedAtUtc" });
    }

    [Fact, TestCase("CY25-35")]
    public async Task DeleteAccount_RemovesNotesOnlyForAVerifiedPhone_KeepsForUnverified_AuthorBecomesDeletedUser_RemovesMaxLink()
    {
        var shop = await CreateRoundClockShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var p = await CreateProductAsync(shop);

        // подтверждённый номер — заметки о нём удаляются вместе с аккаунтом
        var verified = await RegisterAsync();
        await MarkPhoneVerifiedAsync(verified.Phone, verified.UserId);
        var ov = await PlaceStaffViewAsync(shop, [Line(p, 1)], phone: verified.Phone);
        await PutNoteAsync(shop, ov.Id, "о подтверждённом");

        // неподтверждённый номер — заметку нельзя стирать «по слову» аккаунта: она может относиться к другому человеку
        var unverified = await RegisterAsync();
        var ou = await PlaceStaffViewAsync(shop, [Line(p, 1)], phone: unverified.Phone);
        await PutNoteAsync(shop, ou.Id, "о неподтверждённом");

        // заметку о постороннем пишет сотрудник, который потом удаляет свой аккаунт
        var third = await PlaceStaffViewAsync(shop, [Line(p, 1)], phone: UniquePhone());
        await PutNoteAsync(shop, third.Id, "автор — сотрудник", staff.Token);
        (await GetNoteOkAsync(shop, third.Id)).Note!.UpdatedByName.Should().Be($"{staff.FirstName} {staff.LastName}".Trim());

        (await WithDbAsync(db => db.ShopCustomerNotes.CountAsync(n => n.CompanyId == shop.Id))).Should().Be(3);

        var delVerified = await AuthedClient(verified.Token).PostAsJsonAsync("/api/profile/delete-account", new { currentPassword = "Password123!" });
        delVerified.StatusCode.Should().Be(HttpStatusCode.NoContent, await delVerified.Content.ReadAsStringAsync());
        var delUnverified = await AuthedClient(unverified.Token).PostAsJsonAsync("/api/profile/delete-account", new { currentPassword = "Password123!" });
        delUnverified.StatusCode.Should().Be(HttpStatusCode.NoContent, await delUnverified.Content.ReadAsStringAsync());

        var left = await WithDbAsync(db => db.ShopCustomerNotes.AsNoTracking().Where(n => n.CompanyId == shop.Id).ToListAsync());
        left.Select(n => n.Text).Should().BeEquivalentTo(new[] { "о неподтверждённом", "автор — сотрудник" },
            "заметка о подтверждённом номере удалена; о неподтверждённом — осталась");

        // автор заметки удаляет аккаунт: текст остаётся у магазина, имя автора — «Удалённый пользователь»
        var delStaff = await AuthedClient(staff.Token).PostAsJsonAsync("/api/profile/delete-account", new { currentPassword = "Password123!" });
        delStaff.StatusCode.Should().Be(HttpStatusCode.NoContent, await delStaff.Content.ReadAsStringAsync());
        var authored = (await GetNoteOkAsync(shop, third.Id)).Note!;
        authored.Text.Should().Be("автор — сотрудник");
        authored.UpdatedByName.Should().Be("Удалённый пользователь");
        authored.UpdatedText.Should().StartWith("Изменено: Удалённый пользователь, ");

        // заказы аккаунтов обезличены и выпали из карточки
        (await CardAsync(shop, ov.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("CY25-36")]
    public async Task DeleteAccount_RemovesTheStaffMaxLinkAndItsSessions()
    {
        var shop = await CreateRoundClockShopAsync();
        var p = await CreateProductAsync(shop);
        await using var mx = new StaffMaxTestFactory(ConnectionString);
        mx.EnsureWebhookSubscribed();
        var member = await AddShopStaffAsync(shop);
        var client = ClientOn(mx, member.Token);
        var session = (await (await client.PostAsync("/api/staff-max/link-sessions", null)).Content.ReadJsonAsync<ServiceBooking.API.DTOs.StaffMax.StaffMaxLinkSessionDto>())!;
        var payload = System.Web.HttpUtility.ParseQueryString(new Uri(session.DeepLink).Query)["start"];
        await client.PostAsJsonAsync($"/api/phone-verification/max/webhook/{StaffMaxTestFactory.WebhookToken}",
            new { update_type = "bot_started", payload, user = new { user_id = "del-user" }, chat = new { chat_id = "chat25-del-" + Guid.NewGuid().ToString("N")[..8] } });
        await client.PostAsync("/api/staff-max/link-sessions", null); // и незавершённая сессия
        (await WithDbAsync(db => db.StaffMaxLinks.CountAsync(l => l.UserId == member.UserId))).Should().Be(1);
        (await WithDbAsync(db => db.StaffMaxLinkSessions.CountAsync(s => s.UserId == member.UserId))).Should().BeGreaterThan(0);

        var del = await AuthedClient(member.Token).PostAsJsonAsync("/api/profile/delete-account", new { currentPassword = "Password123!" });
        del.StatusCode.Should().Be(HttpStatusCode.NoContent, await del.Content.ReadAsStringAsync());
        (await WithDbAsync(db => db.StaffMaxLinks.CountAsync(l => l.UserId == member.UserId))).Should().Be(0);
        (await WithDbAsync(db => db.StaffMaxLinkSessions.CountAsync(s => s.UserId == member.UserId))).Should().Be(0);

        // после удаления ни одно новое событие не ставит сообщение в его чат
        (await ClientOn(mx).PostJsonAsync($"/api/storefront/{shop.Slug}/orders", Guest([Line(p, 1)]))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await WithDbAsync(db => db.StaffMaxMessages.CountAsync(m => m.CompanyId == shop.Id))).Should().Be(0);
    }

    [Fact, TestCase("CY25-37")]
    public async Task Retention_NoteIsDeletedWhenTheShopHasNoNonErasedOrderWithThePhone_Otherwise_Stays()
    {
        var shop = await CreateRoundClockShopAsync();
        var p = await CreateProductAsync(shop);
        var goneNumber = UniquePhone();
        var keptNumber = UniquePhone();
        var gone = await PlaceStaffViewAsync(shop, [Line(p, 1)], phone: goneNumber);
        var kept = await PlaceStaffViewAsync(shop, [Line(p, 1)], phone: keptNumber);
        var keptToo = await PlaceStaffViewAsync(shop, [Line(p, 1)], phone: goneNumber);
        await PutNoteAsync(shop, gone.Id, "о goneNumber");
        await PutNoteAsync(shop, kept.Id, "о keptNumber");

        Task<int> Notes() => WithDbAsync(db => db.ShopCustomerNotes.CountAsync(n => n.CompanyId == shop.Id));
        async Task RunRuleAsync()
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await new ShopCustomerNoteRule(db).ApplyAsync(new RetentionContext(DateTime.UtcNow, new RetentionPeriods(), 100, false), CancellationToken.None);
        }

        // пока есть необезличенный заказ с номером — заметка на месте (и правило инертно, пока никто не обезличен)
        await RunRuleAsync();
        (await Notes()).Should().Be(2);

        // обезличен один из двух заказов номера goneNumber — второй ещё держит заметку
        await WithDbAsync(async db =>
        {
            var o = await db.Orders.SingleAsync(x => x.Id == gone.Id);
            o.PersonalDataErased = true; o.CustomerPhone = null; o.CustomerName = null;
            await db.SaveChangesAsync();
        });
        await RunRuleAsync();
        (await Notes()).Should().Be(2, "у номера остался необезличенный заказ");

        await WithDbAsync(async db =>
        {
            var o = await db.Orders.SingleAsync(x => x.Id == keptToo.Id);
            o.PersonalDataErased = true; o.CustomerPhone = null; o.CustomerName = null;
            await db.SaveChangesAsync();
        });
        await RunRuleAsync();
        (await Notes()).Should().Be(1, "необезличенных заказов с номером не осталось — заметка удалена");
        (await GetNoteOkAsync(shop, kept.Id)).Note!.Text.Should().Be("о keptNumber");
    }
}
