using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 24, «Вызов 2»: доступность товаров по дням недели, меню на дату, «закончилось» со сроком (US-24-10…13, Q-24-5, Q-24-6).
/// Магазин с часами 09:00–12:00 — «сегодня» и «завтра» считаются по календарю магазина без «хвоста» после полуночи.
/// </summary>
public class Cycle24AvailabilityTests(TestDatabaseFixture fixture) : Cycle24TestBase(fixture)
{
    private static DayOfWeek Next(DayOfWeek d, int n = 1) => (DayOfWeek)(((int)d + n) % 7);

    // ── US-24-10: дни недели ─────────────────────────────────────────────────────

    [Fact, TestCase("CY24-40")]
    public async Task ProductWeekdays_LabelDefaultsAndValidation()
    {
        var shop = await CreateScheduledShopAsync();
        var c = AuthedClient(shop.OwnerToken);
        var all = await CreateProductAsync(shop, "Хлеб");
        all.AvailableWeekdays.Should().HaveCount(7, "по умолчанию включены все дни — поведение цикла 23");
        all.WeekdaysLabel.Should().BeNull();

        await PutWeekdaysAsync(shop, all, DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday);
        var one = await GetProductAsync(shop, all.Id);
        one.AvailableWeekdays.Should().BeEquivalentTo(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday });
        one.WeekdaysLabel.Should().Be("пн, ср, пт");

        await PutWeekdaysAsync(shop, all);
        var none = await GetProductAsync(shop, all.Id);
        none.AvailableWeekdays.Should().BeEmpty("пустой список допустим: товар только по меню на дату");
        none.WeekdaysLabel.Should().Be("только по меню");

        var dup = await c.PutJsonAsync($"/api/shops/{shop.Id}/products/{all.Id}", new ProductInput(
            all.CategoryId, all.Name, null, ProductUnit.Piece, all.Price, null, null, null, true, null, [DayOfWeek.Monday, DayOfWeek.Monday]));
        dup.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // T-25-03 (API_CONTRACT_CYCLE25.md §534): a repeated day now has its own text.
        (await dup.Content.ReadAsStringAsync()).Should().Contain("День недели указан дважды");

        // создание с днями
        var created = await c.PostJsonAsync($"/api/shops/{shop.Id}/products", new ProductInput(
            null, "Борщ", null, ProductUnit.Piece, 180m, null, null, null, true, null, [DayOfWeek.Tuesday]));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        (await created.Content.ReadJsonAsync<ProductDto>())!.WeekdaysLabel.Should().Be("вт");

        await PutWeekdaysAsync(shop, all, Enum.GetValues<DayOfWeek>());
        (await GetProductAsync(shop, all.Id)).WeekdaysLabel.Should().BeNull("все семь дней — метка пропадает");
    }

    [Fact, TestCase("CY24-41")]
    public async Task Storefront_HidesProductsNotSoldOnTheDate_ByWeekday()
    {
        var shop = await CreateScheduledShopAsync();
        var today = ShopToday(shop);
        var tomorrow = today.AddDays(1);
        var everyday = await CreateProductAsync(shop, "Напиток");
        var onlyToday = await CreateProductAsync(shop, "Сегодняшний суп");
        var onlyTomorrow = await CreateProductAsync(shop, "Завтрашний борщ");
        await PutWeekdaysAsync(shop, onlyToday, today.DayOfWeek);
        await PutWeekdaysAsync(shop, onlyTomorrow, tomorrow.DayOfWeek);

        var sfToday = await GetStorefrontAsync(shop.Slug);
        AllProducts(sfToday).Select(x => x.Name).Should().BeEquivalentTo("Напиток", "Сегодняшний суп");
        var sfTomorrow = await GetStorefrontAsync(shop.Slug, tomorrow);
        sfTomorrow.Date.Should().Be(tomorrow);
        AllProducts(sfTomorrow).Select(x => x.Name).Should().BeEquivalentTo("Напиток", "Завтрашний борщ");
        _ = everyday;
    }

    [Fact, TestCase("CY24-42")]
    public async Task QuoteAndOrder_ForUnavailableDate_NotAvailableOnDate_NothingCreated_RightDateWorks()
    {
        var shop = await CreateScheduledShopAsync();
        var tomorrow = ShopToday(shop).AddDays(1);
        var soup = await CreateProductAsync(shop, "Суп");
        var tea = await CreateProductAsync(shop, "Чай");
        await PutWeekdaysAsync(shop, soup, Next(tomorrow.DayOfWeek, 3));
        var slot = await SlotAsync(shop.Slug, tomorrow);

        var q = (await (await AnonymousClient().PostJsonAsync($"/api/storefront/{shop.Slug}/quote",
            new QuoteInput([new(soup.Id, 1), new(tea.Id, 1)], slot))).Content.ReadJsonAsync<QuoteDto>())!;
        q.HasProblems.Should().BeTrue("оформить нельзя, пока покупатель не уберёт позицию или не сменит дату");
        q.Lines.Single(l => l.ProductId == soup.Id).Problem!.Reason.Should().Be(OrderProblemReason.NotAvailableOnDate);
        q.Lines.Single(l => l.ProductId == soup.Id).Problem!.Message.Should().Be("В этот день не продаётся");
        q.Lines.Single(l => l.ProductId == tea.Id).Problem.Should().BeNull();

        var r = await PostOrderAsync(shop.Slug, GuestAt([Line(soup, 1), Line(tea, 1)], slot));
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await J(r);
        body.GetProperty("code").GetString().Should().Be("ItemsUnavailable");
        body.GetProperty("problems").EnumerateArray().Should().ContainSingle(x => x.GetProperty("reason").GetString() == "NotAvailableOnDate");
        (await GetBoardAsync(shop)).NewOrders.Should().BeNullOrEmpty();

        (await PostOrderAsync(shop.Slug, GuestAt([Line(tea, 1)], slot))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact, TestCase("CY24-43")]
    public async Task CategoryWeekdays_AppliesToAllProductsOfCategory_OwnerOnly()
    {
        var shop = await CreateScheduledShopAsync();
        var cat = await CreateCategoryAsync(shop, "Первое");
        var a = await CreateProductAsync(shop, "Борщ", categoryId: cat.Id);
        var b = await CreateProductAsync(shop, "Щи", categoryId: cat.Id);
        var other = await CreateProductAsync(shop, "Кофе");
        var staff = await AddShopStaffAsync(shop);

        var url = $"/api/shops/{shop.Id}/categories/{cat.Id}/weekdays";
        (await AuthedClient(staff.Token).PutJsonAsync(url, new CategoryWeekdaysInput([DayOfWeek.Monday]))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var r = await AuthedClient(shop.OwnerToken).PutJsonAsync(url, new CategoryWeekdaysInput([DayOfWeek.Monday, DayOfWeek.Thursday]));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var list = (await r.Content.ReadJsonAsync<List<ProductDto>>())!;
        list.Should().HaveCount(2).And.OnlyContain(x => x.WeekdaysLabel == "пн, чт");
        (await GetProductAsync(shop, other.Id)).WeekdaysLabel.Should().BeNull("продукты вне категории не затронуты");
        _ = a; _ = b;
    }

    // ── US-24-11: меню на дату ───────────────────────────────────────────────────

    [Fact, TestCase("CY24-44")]
    public async Task DailyMenu_NewMenuIsPrefilledByWeekdays_PutOverridesWeekdays_DeleteReturnsToRule()
    {
        var shop = await CreateScheduledShopAsync();
        var tomorrow = ShopToday(shop).AddDays(1);
        var c = AuthedClient(shop.OwnerToken);
        var bread = await CreateProductAsync(shop, "Хлеб");
        var soup = await CreateProductAsync(shop, "Суп дня");
        var cake = await CreateProductAsync(shop, "Торт");
        await PutWeekdaysAsync(shop, cake, Next(tomorrow.DayOfWeek, 2)); // завтра по дням недели не продаётся
        var url = $"/api/shops/{shop.Id}/daily-menus/{D(tomorrow)}";

        var fresh = (await (await c.GetAsync(url)).Content.ReadJsonAsync<DailyMenuDto>())!;
        fresh.Exists.Should().BeFalse();
        fresh.ProductIds.Should().BeEquivalentTo(new[] { bread.Id, soup.Id }, "меню заполнено заранее товарами дня недели (Q-24-6)");
        fresh.Products.Single(x => x.ProductId == cake.Id).AllowedByWeekdays.Should().BeFalse();
        fresh.Products.Single(x => x.ProductId == cake.Id).InMenu.Should().BeFalse();

        // меню переопределяет дни недели: торт — да, суп — нет
        var put = await c.PutJsonAsync(url, new DailyMenuInput([bread.Id, cake.Id]));
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var saved = (await put.Content.ReadJsonAsync<DailyMenuDto>())!;
        saved.Exists.Should().BeTrue();
        saved.ProductIds.Should().BeEquivalentTo(new[] { bread.Id, cake.Id });

        AllProducts(await GetStorefrontAsync(shop.Slug, tomorrow)).Select(x => x.Name).Should().BeEquivalentTo("Хлеб", "Торт");
        AllProducts(await GetStorefrontAsync(shop.Slug)).Select(x => x.Name).Should().BeEquivalentTo(new[] { "Хлеб", "Суп дня" }, "на сегодня меню нет — действуют дни недели");

        var cal = (await (await c.GetAsync($"/api/shops/{shop.Id}/daily-menus")).Content.ReadJsonAsync<DailyMenuCalendarDto>())!;
        cal.Days.Single(d => d.Date == tomorrow).HasMenu.Should().BeTrue();
        cal.Days.Single(d => d.Date == tomorrow).ProductCount.Should().Be(2);
        cal.Days.Count(d => d.HasMenu).Should().Be(1);

        // заказ на дату по меню: суп не в меню
        var slot = await SlotAsync(shop.Slug, tomorrow);
        var refused = await PostOrderAsync(shop.Slug, GuestAt([Line(soup, 1)], slot));
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await J(refused)).GetProperty("problems")[0].GetProperty("reason").GetString().Should().Be("NotAvailableOnDate");
        (await PostOrderAsync(shop.Slug, GuestAt([Line(cake, 1)], slot))).StatusCode.Should().Be(HttpStatusCode.Created);

        // удаление возвращает к правилу дней недели; повтор — 204
        (await c.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        AllProducts(await GetStorefrontAsync(shop.Slug, tomorrow)).Select(x => x.Name).Should().BeEquivalentTo("Хлеб", "Суп дня");
        (await (await c.GetAsync(url)).Content.ReadJsonAsync<DailyMenuDto>())!.Exists.Should().BeFalse();
    }

    [Fact, TestCase("CY24-45")]
    public async Task DailyMenu_Validation_Range_Permissions()
    {
        var shop = await CreateScheduledShopAsync(preorderDays: 3);
        var today = ShopToday(shop);
        var c = AuthedClient(shop.OwnerToken);
        var staff = await AddShopStaffAsync(shop);
        var stranger = await RegisterAsync();
        var p = await CreateProductAsync(shop);
        var gone = await CreateProductAsync(shop, "Удалённый");
        (await c.DeleteAsync($"/api/shops/{shop.Id}/products/{gone.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        string Url(DateOnly d) => $"/api/shops/{shop.Id}/daily-menus/{D(d)}";

        var past = await c.PutJsonAsync(Url(today.AddDays(-1)), new DailyMenuInput([p.Id]));
        past.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await past.Content.ReadAsStringAsync()).Should().Contain("Дата — от сегодня до");
        // горизонт календаря = max(предзаказ, 7) = 7 дней
        (await c.PutJsonAsync(Url(today.AddDays(7)), new DailyMenuInput([p.Id]))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.PutJsonAsync(Url(today.AddDays(8)), new DailyMenuInput([p.Id]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var dup = await c.PutJsonAsync(Url(today), new DailyMenuInput([p.Id, p.Id]));
        dup.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await dup.Content.ReadAsStringAsync()).Should().Contain("Товар в меню повторяется");
        var unknown = await c.PutJsonAsync(Url(today), new DailyMenuInput([Guid.NewGuid()]));
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync()).Should().Contain("Товар не найден");
        (await (await c.PutJsonAsync(Url(today), new DailyMenuInput([gone.Id]))).Content.ReadAsStringAsync()).Should().Contain("Товар не найден", "удалённый товар в меню не попадает");

        // чужой товар из другого магазина
        var other = await CreateShopAsync();
        var foreign = await CreateProductAsync(other);
        (await c.PutJsonAsync(Url(today), new DailyMenuInput([foreign.Id]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // пустое меню допустимо: в этот день ничего не продаётся
        (await c.PutJsonAsync(Url(today.AddDays(1)), new DailyMenuInput([]))).StatusCode.Should().Be(HttpStatusCode.OK);
        AllProducts(await GetStorefrontAsync(shop.Slug, today.AddDays(1))).Should().BeEmpty();

        // сотрудник правит меню, чужой — нет
        (await AuthedClient(staff.Token).PutJsonAsync(Url(today.AddDays(2)), new DailyMenuInput([p.Id]))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AuthedClient(stranger.Token).PutJsonAsync(Url(today.AddDays(2)), new DailyMenuInput([p.Id]))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AnonymousClient().GetAsync(Url(today))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("CY24-46")]
    public async Task DailyMenu_Copy_FromDateWithMenu_And_WithoutMenu400()
    {
        var shop = await CreateScheduledShopAsync();
        var today = ShopToday(shop);
        var c = AuthedClient(shop.OwnerToken);
        var a = await CreateProductAsync(shop, "А");
        var b = await CreateProductAsync(shop, "Б");
        var src = $"/api/shops/{shop.Id}/daily-menus/{D(today.AddDays(1))}";
        var dst = $"/api/shops/{shop.Id}/daily-menus/{D(today.AddDays(2))}";

        var none = await c.PostJsonAsync(dst + "/copy", new DailyMenuCopyInput(today.AddDays(1)));
        none.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await none.Content.ReadAsStringAsync()).Should().Contain("На эту дату меню нет");

        (await c.PutJsonAsync(src, new DailyMenuInput([a.Id]))).StatusCode.Should().Be(HttpStatusCode.OK);
        var copied = await c.PostJsonAsync(dst + "/copy", new DailyMenuCopyInput(today.AddDays(1)));
        copied.StatusCode.Should().Be(HttpStatusCode.OK, await copied.Content.ReadAsStringAsync());
        var menu = (await copied.Content.ReadJsonAsync<DailyMenuDto>())!;
        menu.Exists.Should().BeTrue();
        menu.ProductIds.Should().BeEquivalentTo(new[] { a.Id });
        _ = b;
    }

    // ── US-24-13: «закончилось» со сроком ────────────────────────────────────────

    [Fact, TestCase("CY24-47")]
    public async Task SoldOutToday_BlocksOnlyToday_FutureDatesStayAvailable_OrdersRespectIt()
    {
        var shop = await CreateScheduledShopAsync();
        await OpenShopAllDayAsync(shop);                         // «сегодня» — рабочий день; ниже даты считаются по нему же
        await SetPickupAsync(shop, true, true, 30, 3, 0);
        var today = ShopToday(shop);
        var tomorrow = today.AddDays(1);
        var borsch = await CreateProductAsync(shop, "Борщ");
        var tea = await CreateProductAsync(shop, "Чай");
        await SetSoldOutAsync(shop, borsch.Id, true, "Today");

        var dto = await GetProductAsync(shop, borsch.Id);
        dto.IsSoldOut.Should().BeTrue();
        dto.SoldOut!.Scope.Should().Be(SoldOutScope.Today);
        dto.SoldOut.Text.Should().Be("нет на сегодня");

        var sfToday = await GetStorefrontAsync(shop.Slug);
        var b = AllProducts(sfToday).Single(x => x.Name == "Борщ");
        b.Available.Should().BeFalse();
        b.UnavailableReason.Should().Be(StorefrontUnavailableReason.SoldOut, "серым, как в цикле 23");
        var bt = AllProducts(await GetStorefrontAsync(shop.Slug, tomorrow)).Single(x => x.Name == "Борщ");
        bt.Available.Should().BeTrue("отметка «на сегодня» предзаказы на другие дни не блокирует");

        // «как можно скорее» сегодня — отказ, слот завтра — принят
        var asap = await PostOrderAsync(shop.Slug, GuestAt([Line(borsch, 1), Line(tea, 1)], null));
        asap.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(asap)).Should().Be("ItemsUnavailable");
        var slot = await SlotAsync(shop.Slug, tomorrow);
        (await PostOrderAsync(shop.Slug, GuestAt([Line(borsch, 1)], slot))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact, TestCase("CY24-48")]
    public async Task SoldOutUntilCancelled_DefaultScope_AllDates_ChangeScopeAndRemove_StaffMayDoIt()
    {
        var shop = await CreateScheduledShopAsync();
        var tomorrow = ShopToday(shop).AddDays(1);
        var staff = await AddShopStaffAsync(shop);
        var p = await CreateProductAsync(shop, "Плов");

        // без scope — «до отмены» (совместимость с циклом 23); ставит сотрудник
        await SetSoldOutAsync(shop, p.Id, true, null, staff.Token);
        var dto = await GetProductAsync(shop, p.Id);
        dto.SoldOut!.Scope.Should().Be(SoldOutScope.UntilCancelled);
        dto.SoldOut.Text.Should().Be("нет до отмены");
        foreach (var d in new DateOnly?[] { null, tomorrow })
            AllProducts(await GetStorefrontAsync(shop.Slug, d)).Single().Available.Should().BeFalse("на все даты");

        // повторный вызов меняет срок
        await SetSoldOutAsync(shop, p.Id, true, "Today");
        (await GetProductAsync(shop, p.Id)).SoldOut!.Scope.Should().Be(SoldOutScope.Today);
        await SetSoldOutAsync(shop, p.Id, true, "UntilCancelled");
        (await GetProductAsync(shop, p.Id)).SoldOut!.Text.Should().Be("нет до отмены");

        // снятие
        await SetSoldOutAsync(shop, p.Id, false, null, staff.Token);
        var cleared = await GetProductAsync(shop, p.Id);
        cleared.IsSoldOut.Should().BeFalse();
        cleared.SoldOut.Should().BeNull();
        AllProducts(await GetStorefrontAsync(shop.Slug, tomorrow)).Single().Available.Should().BeTrue();

        (await AuthedClient((await RegisterAsync()).Token).PutJsonAsync($"/api/shops/{shop.Id}/products/{p.Id}/sold-out", new { isSoldOut = true }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("CY24-49")]
    public async Task SoldOutToday_ExpiresByItself_WhenShopDayPasses()
    {
        var shop = await CreateScheduledShopAsync();
        var p = await CreateProductAsync(shop, "Суп");
        await SetSoldOutAsync(shop, p.Id, true, "Today");
        (await GetProductAsync(shop, p.Id)).IsSoldOut.Should().BeTrue();

        // «Начался следующий день магазина»: отметка датирована вчерашним днём (без фоновой задачи, SPEC §6 — не позже 5 минут)
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Products.SingleAsync(x => x.Id == p.Id);
            row.SoldOutForDate = ShopToday(shop).AddDays(-1);
            await db.SaveChangesAsync();
        }

        var after = await GetProductAsync(shop, p.Id);
        after.IsSoldOut.Should().BeFalse("истёкшая «на сегодня» даёт isSoldOut=false");
        after.SoldOut.Should().BeNull();
        AllProducts(await GetStorefrontAsync(shop.Slug)).Single().Available.Should().BeTrue();
    }

    [Fact, TestCase("CY24-50")]
    public async Task SoldOutMark_DoesNotChangeAlreadyCreatedOrders()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop);
        var order = await PlaceAndLoadAsync(shop, [Line(p, 2)]);
        await SetSoldOutAsync(shop, p.Id, true, "UntilCancelled");
        var accepted = await ActOkAsync(shop, order, "accept");
        accepted.Status.Should().Be(OrderStatus.Accepted);
        accepted.Items.Single().QuantityOrdered.Should().Be(2);
    }
}
