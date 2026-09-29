using FluentAssertions;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.API.Services.Retention;
using ServiceBooking.API.Services.Retention.Rules;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.API.Startup;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using Microsoft.Extensions.Options;

namespace ServiceBooking.UnitTests;

/// <summary>Cycle 23: catalog ordering, the shop clock, the order mapper, the change journal, depersonalization, path masking.</summary>
public class OrderDomainTests
{
    // ── CatalogOrdering (§393.1) ────────────────────────────────────────────────────────────────────

    private static List<ProductCategory> Categories(int n) =>
        Enumerable.Range(0, n).Select(i => new ProductCategory { Id = Guid.NewGuid(), Position = i, Name = $"c{i}" }).ToList();

    [Fact]
    public void ApplyCategoryOrder_FullPermutation_SetsPositions()
    {
        var cats = Categories(3);
        CatalogOrdering.ApplyCategoryOrder(cats, [cats[2].Id, cats[0].Id, cats[1].Id]);
        cats[2].Position.Should().Be(0);
        cats[0].Position.Should().Be(1);
        cats[1].Position.Should().Be(2);
    }

    [Fact]
    public void ApplyCategoryOrder_Partial_Unknown_Duplicate_AreRefused()
    {
        var cats = Categories(3);
        var partial = () => CatalogOrdering.ApplyCategoryOrder(cats, [cats[0].Id, cats[1].Id]);
        var unknown = () => CatalogOrdering.ApplyCategoryOrder(cats, [cats[0].Id, cats[1].Id, Guid.NewGuid()]);
        var duplicate = () => CatalogOrdering.ApplyCategoryOrder(cats, [cats[0].Id, cats[0].Id, cats[1].Id]);
        partial.Should().Throw<InvalidCatalogReorderException>().WithMessage("Список категорий устарел — обновите страницу");
        unknown.Should().Throw<InvalidCatalogReorderException>();
        duplicate.Should().Throw<InvalidCatalogReorderException>();
    }

    [Fact]
    public void ApplyProductOrder_Stale_UsesTheProductsMessage()
    {
        var products = new List<Product> { new() { Id = Guid.NewGuid() }, new() { Id = Guid.NewGuid() } };
        var act = () => CatalogOrdering.ApplyProductOrder(products, [products[0].Id]);
        act.Should().Throw<InvalidCatalogReorderException>().WithMessage("Список товаров устарел — обновите страницу");
    }

    [Fact]
    public void Compact_RenumbersToContiguous_KeepingOrder()
    {
        var cats = Categories(4);
        cats[0].Position = 0; cats[1].Position = 5; cats[2].Position = 9; cats[3].Position = 2;
        CatalogOrdering.Compact(cats);
        cats.OrderBy(c => c.Position).Select(c => c.Name).Should().Equal("c0", "c3", "c1", "c2");
        cats.Select(c => c.Position).OrderBy(p => p).Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public void NextPosition_IsEndOfList()
    {
        CatalogOrdering.NextPosition([]).Should().Be(0);
        CatalogOrdering.NextPosition([0, 4, 2]).Should().Be(5);
    }

    // ── ShopClock (§396.5) ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BusinessDate_UsesTheShopTimeZone()
    {
        // 2026-10-05 22:30 UTC is 05:30 on the 6th in Barnaul (UTC+7) and 01:30 on the 6th in Moscow (UTC+3), but still the 5th in UTC.
        var utc = new DateTime(2026, 10, 5, 22, 30, 0, DateTimeKind.Utc);
        ShopClock.BusinessDate("Asia/Barnaul", utc).Should().Be(new DateOnly(2026, 10, 6));
        ShopClock.BusinessDate("Europe/Moscow", utc).Should().Be(new DateOnly(2026, 10, 6));
        ShopClock.BusinessDate("UTC", utc).Should().Be(new DateOnly(2026, 10, 5));
    }

    [Fact]
    public void DayBoundsUtc_IsTheLocalMidnightToMidnight()
    {
        var (start, end) = ShopClock.DayBoundsUtc("Asia/Barnaul", new DateOnly(2026, 10, 6));
        start.Should().Be(new DateTime(2026, 10, 5, 17, 0, 0, DateTimeKind.Utc));   // 00:00 +07:00
        end.Should().Be(new DateTime(2026, 10, 6, 17, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void DayBoundsUtc_DstDay_Is23Hours()
    {
        // Europe/Berlin springs forward on 2026-03-29: the local day is 23 hours long.
        var (start, end) = ShopClock.DayBoundsUtc("Europe/Berlin", new DateOnly(2026, 3, 29));
        (end - start).Should().Be(TimeSpan.FromHours(23));
    }

    // ── OrderQuantityRules.AvailableQuantity (§394, §413.3) ─────────────────────────────────────────

    [Theory]
    [InlineData(ProductUnit.Piece, 2, null, null, 2)]
    [InlineData(ProductUnit.Piece, 500, null, null, 99)]
    [InlineData(ProductUnit.Piece, 0, null, null, 0)]
    [InlineData(ProductUnit.Piece, -4, null, null, 0)]
    [InlineData(ProductUnit.Weight, 800, 100, 100, 800)]
    [InlineData(ProductUnit.Weight, 850, 100, 100, 800)]      // floored to the step
    [InlineData(ProductUnit.Weight, 250, 100, 300, 0)]         // below the product minimum
    [InlineData(ProductUnit.Weight, 50000, 100, 100, 10000)]
    public void AvailableQuantity(ProductUnit unit, int free, int? step, int? min, int expected) =>
        OrderQuantityRules.AvailableQuantity(unit, free, step, min).Should().Be(expected);

    // ── OrderDtoMapper (§414.1, §420) ───────────────────────────────────────────────────────────────

    private static readonly OrderDtoMapper Mapper = new(new PublicSiteLinks(Options.Create(new PublicSitesOptions())));
    private static readonly DateTime T0 = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    private static Order NewOrder(OrderStatus status, Action<Order>? tweak = null)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(), Number = 27, BusinessDate = new DateOnly(2026, 10, 5), PublicToken = new string('a', 43), Status = status,
            CustomerKind = OrderActorKind.Guest, CustomerName = "Иван", CustomerPhone = "79001234567", CreatedAtUtc = T0,
            AllowCustomerCancelSnapshot = true, EstimatedTotal = 591.60m, HasWeightItems = true, Version = 3
        };
        order.Items.Add(new OrderItem { Id = Guid.NewGuid(), Position = 0, NameSnapshot = "Шаурма", Unit = ProductUnit.Piece, UnitPrice = 150m, QuantityOrdered = 2, LineTotalEstimated = 300m });
        order.Items.Add(new OrderItem { Id = Guid.NewGuid(), Position = 1, NameSnapshot = "Сыр", Unit = ProductUnit.Weight, UnitPrice = 540m, QuantityOrdered = 540, LineTotalEstimated = 291.60m, WeightStepGrams = 10 });
        tweak?.Invoke(order);
        return order;
    }

    [Fact]
    public void Timeline_New_HasOnlyTheFirstStepReached()
    {
        var t = OrderDtoMapper.Timeline(NewOrder(OrderStatus.New));
        t.Select(s => s.Status).Should().Equal(OrderStatus.New, OrderStatus.Accepted, OrderStatus.Ready, OrderStatus.Issued);
        t.Select(s => s.Reached).Should().Equal(true, false, false, false);
        t[0].ReachedAtUtc.Should().Be(T0);
    }

    [Fact]
    public void Timeline_RejectedFromNew_StopsAtTheFirstStep()
    {
        var order = NewOrder(OrderStatus.Rejected, o => o.CompletedAtUtc = T0.AddMinutes(3));
        OrderDtoMapper.Timeline(order).Select(s => s.Reached).Should().Equal(true, false, false, false);
    }

    [Fact]
    public void Timeline_NotPickedUp_ReachesReadyButNotIssued()
    {
        var order = NewOrder(OrderStatus.NotPickedUp, o => { o.AcceptedAtUtc = T0.AddMinutes(1); o.ReadyAtUtc = T0.AddMinutes(20); o.CompletedAtUtc = T0.AddHours(3); });
        OrderDtoMapper.Timeline(order).Select(s => s.Reached).Should().Equal(true, true, true, false);
    }

    [Fact]
    public void Timeline_Issued_AllReached_WithCompletionTimeOnTheLastStep()
    {
        var order = NewOrder(OrderStatus.Issued, o => { o.AcceptedAtUtc = T0; o.ReadyAtUtc = T0.AddMinutes(20); o.CompletedAtUtc = T0.AddMinutes(40); });
        var t = OrderDtoMapper.Timeline(order);
        t.Should().OnlyContain(s => s.Reached);
        t[3].ReachedAtUtc.Should().Be(T0.AddMinutes(40));
    }

    [Fact]
    public void Totals_AreEstimatesUntilIssued_ThenTheFinalAmount()
    {
        var live = NewOrder(OrderStatus.Ready);
        OrderDtoMapper.DisplayTotal(live).Should().Be(591.60m);
        OrderDtoMapper.TotalIsApproximate(live).Should().BeTrue();

        var issued = NewOrder(OrderStatus.Issued, o => o.FinalTotal = 600.10m);
        OrderDtoMapper.DisplayTotal(issued).Should().Be(600.10m);
        OrderDtoMapper.TotalIsApproximate(issued).Should().BeFalse();
    }

    [Fact]
    public void Card_ExposesActionsFromTheMachine_AndFullPhone()
    {
        var card = OrderDtoMapper.ToCard(NewOrder(OrderStatus.Ready));
        card.AvailableActions.Should().Equal(OrderAction.Issue, OrderAction.NotPickedUp, OrderAction.Cancel, OrderAction.Edit);
        card.CustomerPhone.Should().Be("79001234567");
        card.StatusText.Should().Be("Готов к выдаче");
        card.Items.Select(i => i.IsApproximate).Should().Equal(false, true);
    }

    [Fact]
    public void PublicOrder_MasksPhone_HidesStaffNames_AndShowsShopChangesOnly()
    {
        var order = NewOrder(OrderStatus.Accepted, o => o.AcceptedAtUtc = T0);
        order.Events.Add(new OrderEvent
        {
            Kind = OrderEventKind.Edited, OccurredAtUtc = T0.AddMinutes(5), ActorKind = OrderActorKind.Staff, ActorNameSnapshot = "Анна Кассир",
            Comment = "Нет ржаного", TotalBefore = 591.60m, TotalAfter = 441.60m, VisibleToCustomer = true,
            ChangesJson = OrderChangeLog.SerializeEdit([new ChangeEntry("Шаурма", new ChangeSide(2, 150m, ProductUnit.Piece), new ChangeSide(1, 150m, ProductUnit.Piece))])
        });
        order.Events.Add(new OrderEvent { Kind = OrderEventKind.Accepted, OccurredAtUtc = T0, ActorKind = OrderActorKind.Staff, ActorNameSnapshot = "Анна Кассир" });

        var dto = Mapper.ToPublic(order, new Company { Name = "Шаурма", Slug = "shaurma", Kind = CompanyKind.Orders }, "Барнаул");

        dto.CustomerPhoneMasked.Should().Be("+7 900 ***-**-67");
        dto.CustomerPhoneMasked.Should().NotContain("1234");
        dto.ShopChanges.Should().ContainSingle();
        dto.ShopChanges[0].Comment.Should().Be("Нет ржаного");
        dto.ShopChanges[0].Changes[0].Text.Should().Be("Шаурма: 2 шт × 150 ₽ → 1 шт × 150 ₽");
        dto.ShopChanges[0].TotalAfter.Should().Be(441.60m);
        System.Text.Json.JsonSerializer.Serialize(dto).Should().NotContain("Анна");
        dto.Shop.PublicUrl.Should().Be("https://goods.ezbook.ru/shaurma");
        dto.IsGuest.Should().BeTrue();
        dto.CanCancel.Should().BeTrue();
    }

    [Fact]
    public void PublicOrder_AfterErasure_HasNoPersonalData()
    {
        var order = NewOrder(OrderStatus.Issued);
        OrderPersonalData.Erase(order);
        var dto = Mapper.ToPublic(order, new Company { Name = "Ш", Slug = "shaurma", Kind = CompanyKind.Orders }, null);
        dto.CustomerName.Should().BeNull();
        dto.CustomerPhoneMasked.Should().BeNull();
        dto.Comment.Should().BeNull();
    }

    [Fact]
    public void StaffEvent_Issued_ShowsOnlyZeroedStockNotes()
    {
        var log = new IssueLog([new StockWriteOff("Сыр", ProductUnit.Weight, 540, 400, Zeroed: true), new StockWriteOff("Шаурма", ProductUnit.Piece, 2, 2, Zeroed: false)]);
        var dto = OrderDtoMapper.ToEventDto(new OrderEvent { Kind = OrderEventKind.Issued, ActorKind = OrderActorKind.Staff, ChangesJson = OrderChangeLog.SerializeIssue(log) });
        dto.Changes.Should().ContainSingle();
        dto.Changes![0].Text.Should().Be("Сыр: списано 0,4 кг из 0,54 кг, остаток обнулён");
    }

    // ── LegalTextKey (§398.3) ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void OrderCheckoutNotice_IsAKey_ButNotInTheFailFastSet()
    {
        // In All it would make LegalDocumentProvider's fail-fast block the deploy until legal-counsel supplies the text.
        LegalTextKey.OrderCheckoutNotice.Should().Be("OrderCheckoutNotice");
        LegalTextKey.All.Should().NotContain(LegalTextKey.OrderCheckoutNotice);
    }

    // ── OrderChangeLog ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ChangeLog_RoundTrips_AndToleratesGarbage()
    {
        var entries = new List<ChangeEntry> { new("Чай", null, new ChangeSide(1, 80m, ProductUnit.Piece)), new("Сыр", new ChangeSide(500, 540m, ProductUnit.Weight), null) };
        var parsed = OrderChangeLog.ParseEdit(OrderChangeLog.SerializeEdit(entries));
        parsed.Should().BeEquivalentTo(entries);
        OrderChangeLog.ParseEdit("not json").Should().BeEmpty();
        OrderChangeLog.ParseEdit(null).Should().BeEmpty();
        OrderChangeLog.ParseIssue("{").Should().BeNull();
    }

    // ── Depersonalization (§398.2) ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Erase_ClearsIdentityButKeepsTheBooks()
    {
        var order = NewOrder(OrderStatus.Issued, o => { o.CustomerUserId = "u1"; o.Comment = "без лука"; o.FinalTotal = 600m; });
        OrderPersonalData.Erase(order);
        order.CustomerName.Should().BeNull();
        order.CustomerPhone.Should().BeNull();
        order.Comment.Should().BeNull();
        order.CustomerUserId.Should().BeNull();
        order.PersonalDataErased.Should().BeTrue();
        order.Number.Should().Be(27);
        order.FinalTotal.Should().Be(600m);
        order.Items.Should().HaveCount(2);
        order.Status.Should().Be(OrderStatus.Issued);
    }

    [Fact]
    public void TombstoneCustomerEvent_TouchesCustomerAndGuestOnly()
    {
        var customer = new OrderEvent { ActorKind = OrderActorKind.Customer, ActorNameSnapshot = "Иван", ActorUserId = "u1" };
        var guest = new OrderEvent { ActorKind = OrderActorKind.Guest, ActorNameSnapshot = "Иван" };
        var staff = new OrderEvent { ActorKind = OrderActorKind.Staff, ActorNameSnapshot = "Анна", ActorUserId = "u2" };
        foreach (var e in new[] { customer, guest, staff }) OrderPersonalData.TombstoneCustomerEvent(e);
        customer.ActorNameSnapshot.Should().Be("Удалённый пользователь");
        customer.ActorUserId.Should().BeNull();
        guest.ActorNameSnapshot.Should().Be("Удалённый пользователь");
        staff.ActorNameSnapshot.Should().Be("Анна");
        staff.ActorUserId.Should().Be("u2");
    }

    // ── OrderPersonalizationRule: "срок не задан" (§398.5) ──────────────────────────────────────────

    [Fact]
    public async Task PersonalizationRule_WithZeroDays_DoesNothingAndSaysSo()
    {
        var rule = new OrderPersonalizationRule(db: null!); // never touched: the rule returns before it builds a query
        var outcome = await rule.ApplyAsync(new RetentionContext(DateTime.UtcNow, new RetentionPeriods(), 500, DryRun: false), CancellationToken.None);
        outcome.Skipped.Should().BeTrue();
        outcome.Scanned.Should().Be(0);
        outcome.Affected.Should().Be(0);
        outcome.Summary.Should().Contain("срок хранения не настроен");
        new RetentionPeriods().OrderPersonalDataDays.Should().Be(0);
    }

    // ── Serilog path masking (§398.6) ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/orders/public/abcDEF123", "/api/orders/public/***")]
    [InlineData("/api/orders/public/abcDEF123/cancel", "/api/orders/public/***/cancel")]
    [InlineData("/api/orders/my", null)]
    [InlineData("/api/orders/public/", null)]
    [InlineData("/api/storefront/shaurma", null)]
    public void OrderTokenIsMaskedInRequestPath(string path, string? expected) =>
        LoggingExtensions.MaskSensitiveRequestPath(path).Should().Be(expected);
}
