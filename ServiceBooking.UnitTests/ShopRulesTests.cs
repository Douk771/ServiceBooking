using FluentAssertions;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>Cycle 23: ShopOrderingGate, CatalogAvailability, ProductInputRules, ShopAccess, SellerInfoRequirements.</summary>
public class ShopRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    // ── ShopOrderingGate (§392.3) ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Gate_ActiveShop_Accepts() =>
        ShopOrderingGate.Evaluate(new Company { Kind = CompanyKind.Orders, IsActive = true }, null, Now)
            .Should().Be(new ShopOrderingGate.Result(true, null));

    [Fact]
    public void Gate_BlockedShop_DoesNotAccept_WithText()
    {
        var r = ShopOrderingGate.Evaluate(new Company { Kind = CompanyKind.Orders, IsActive = false }, new ShopSettings(), Now);
        r.Accepting.Should().BeFalse();
        r.ReasonText.Should().Be("Магазин недоступен");
    }

    [Fact]
    public void Gate_Salon_DoesNotAccept() =>
        ShopOrderingGate.Evaluate(new Company { Kind = CompanyKind.Services, IsActive = true }, null, Now).Accepting.Should().BeFalse();

    // ── CatalogAvailability (§393.2) ────────────────────────────────────────────────────────────────

    private static Product Piece(Action<Product>? tweak = null)
    {
        var p = new Product { Unit = ProductUnit.Piece, IsPublished = true };
        tweak?.Invoke(p);
        return p;
    }

    [Fact]
    public void Availability_PlainPublishedProduct_IsAvailable() =>
        CatalogAvailability.IsAvailable(Piece(), null, false, null, true).Should().BeTrue();

    [Fact]
    public void Availability_Precedence_DeletedThenUnpublishedThenHiddenThenSoldOut()
    {
        var all = Piece(p => { p.DeletedAtUtc = Now; p.IsPublished = false; p.IsSoldOut = true; });
        CatalogAvailability.Evaluate(all, new ProductCategory { IsHidden = true }, false, null, true).Should().Be(ProductAvailability.Deleted);
        var unpublished = Piece(p => { p.IsPublished = false; p.IsSoldOut = true; });
        CatalogAvailability.Evaluate(unpublished, new ProductCategory { IsHidden = true }, false, null, true).Should().Be(ProductAvailability.Unpublished);
        CatalogAvailability.Evaluate(Piece(p => p.IsSoldOut = true), new ProductCategory { IsHidden = true }, false, null, true).Should().Be(ProductAvailability.CategoryHidden);
        CatalogAvailability.Evaluate(Piece(p => p.IsSoldOut = true), null, false, null, true).Should().Be(ProductAvailability.SoldOut);
    }

    [Theory]
    [InlineData(true, 5, 1, ProductAvailability.Available)]
    [InlineData(true, 0, 1, ProductAvailability.InsufficientStock)]
    [InlineData(true, -3, 1, ProductAvailability.InsufficientStock)]
    [InlineData(false, 0, 1, ProductAvailability.Available)] // shop does not track stock — the figure is ignored
    public void Availability_Stock(bool tracks, int free, int stockOnHand, ProductAvailability expected) =>
        CatalogAvailability.Evaluate(Piece(p => p.StockOnHand = stockOnHand), null, tracks, free, true).Should().Be(expected);

    [Fact]
    public void Availability_StockNotTrackedForProduct_IsIgnored() =>
        CatalogAvailability.Evaluate(Piece(), null, true, 0, true).Should().Be(ProductAvailability.Available);

    [Fact]
    public void Availability_WeightProduct_FreeBelowMinimum_IsInsufficient()
    {
        var p = new Product { Unit = ProductUnit.Weight, IsPublished = true, StockOnHand = 500, WeightStepGrams = 100, MinQuantityGrams = 300 };
        CatalogAvailability.Evaluate(p, null, true, 200, true).Should().Be(ProductAvailability.InsufficientStock);
        CatalogAvailability.Evaluate(p, null, true, 300, true).Should().Be(ProductAvailability.Available);
    }

    [Fact]
    public void Availability_ShopNotAccepting_MakesEverythingUnavailable() =>
        CatalogAvailability.Evaluate(Piece(), null, false, null, false).Should().Be(ProductAvailability.ShopNotAccepting);

    // ── ProductInputRules (§410.2) ──────────────────────────────────────────────────────────────────

    private static string? Error(string? name = "Шаурма", decimal price = 250m, ProductUnit unit = ProductUnit.Piece,
        string? portion = null, int? step = null, int? min = null, string? composition = null, string? description = null)
    {
        ProductInputRules.TryNormalize(name, description, price, unit, portion, step, min, composition, out _, out var error);
        return error;
    }

    [Fact]
    public void Product_Valid_Piece_IsNormalized()
    {
        ProductInputRules.TryNormalize("  Шаурма ", " вкусно ", 250m, ProductUnit.Piece, " 300 г ", null, null, " ", out var r, out var e).Should().BeTrue();
        e.Should().BeNull();
        r!.Name.Should().Be("Шаурма");
        r.Description.Should().Be("вкусно");
        r.PortionText.Should().Be("300 г");
        r.CompositionAndAllergens.Should().BeNull();
        r.WeightStepGrams.Should().BeNull();
    }

    [Fact]
    public void Product_Weight_GetsDefaultStepAndMinimum()
    {
        ProductInputRules.TryNormalize("Сыр", null, 540m, ProductUnit.Weight, null, null, null, null, out var r, out _).Should().BeTrue();
        r!.WeightStepGrams.Should().Be(100);
        r.MinQuantityGrams.Should().Be(100);
    }

    [Theory]
    [InlineData("", "Укажите название товара")]
    [InlineData("   ", "Укажите название товара")]
    public void Product_Name(string name, string expected) => Error(name: name).Should().Be(expected);

    [Fact]
    public void Product_NameTooLong() => Error(name: new string('а', 201)).Should().Be("Укажите название товара");

    [Theory]
    [InlineData(0)]
    [InlineData(0.001)]
    [InlineData(1000000.01)]
    [InlineData(10.555)]
    [InlineData(-5)]
    public void Product_BadPrice(double price) =>
        Error(price: (decimal)price).Should().Be("Цена — от 0,01 до 1 000 000 ₽, не больше двух знаков после запятой");

    [Theory]
    [InlineData(0.01)]
    [InlineData(1000000)]
    [InlineData(99.99)]
    public void Product_PriceBoundariesAreOk(double price) => Error(price: (decimal)price).Should().BeNull();

    [Fact]
    public void Product_PortionOnWeight_Refused() =>
        Error(unit: ProductUnit.Weight, portion: "300 г").Should().Be("Порция указывается только у штучного товара");

    [Fact]
    public void Product_StepOnPiece_Refused() => Error(step: 100).Should().NotBeNull();

    [Fact]
    public void Product_BadWeightStep() =>
        Error(unit: ProductUnit.Weight, step: 5).Should().Be("Шаг — от 10 до 5000 г");

    [Fact]
    public void Product_BadWeightMin() =>
        Error(unit: ProductUnit.Weight, step: 100, min: 150).Should().Be("Минимальный вес — не меньше шага и кратен ему");

    [Fact]
    public void Product_Composition_LimitIs2000()
    {
        Error(composition: new string('x', 2000)).Should().BeNull();
        Error(composition: new string('x', 2001)).Should().Be("Состав — не длиннее 2000 символов");
    }

    // ── ShopAccess (§392.2) ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ShopRole.Owner, ShopPermission.EditCatalog, true)]
    [InlineData(ShopRole.Owner, ShopPermission.ManageStaff, true)]
    [InlineData(ShopRole.SuperAdmin, ShopPermission.EditSettings, true)]
    [InlineData(ShopRole.Staff, ShopPermission.ManageOrders, true)]
    [InlineData(ShopRole.Staff, ShopPermission.ManageStock, true)]
    [InlineData(ShopRole.Staff, ShopPermission.ViewShop, true)]
    [InlineData(ShopRole.Staff, ShopPermission.EditCatalog, false)]
    [InlineData(ShopRole.Staff, ShopPermission.EditSettings, false)]
    [InlineData(ShopRole.Staff, ShopPermission.ManageStaff, false)]
    public void Access_MatchesTheRightsTable(ShopRole role, ShopPermission permission, bool expected) =>
        ShopAccess.Allows(role, permission).Should().Be(expected);

    // ── SellerInfoRequirements (§404 L2) ────────────────────────────────────────────────────────────

    [Fact]
    public void Seller_Cycle1_NothingIsRequired()
    {
        SellerInfoRequirements.RequiredFields().Should().BeEmpty();
        SellerInfoRequirements.IsComplete(null, null, null, null, null).Should().BeTrue();
    }

    // ── PublicOrderToken (§395.2 step 9) ────────────────────────────────────────────────────────────

    [Fact]
    public void Token_Is43Base64UrlChars_AndUnique()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => PublicOrderToken.Generate()).ToList();
        tokens.Should().OnlyContain(t => PublicOrderToken.IsWellFormed(t));
        tokens.Distinct().Should().HaveCount(200);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]   // 44 chars
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa+")]     // 43, not base64url
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa=")]
    public void Token_Malformed_IsRejected(string? token) => PublicOrderToken.IsWellFormed(token).Should().BeFalse();

    // ── OrderTexts (§420, §413.3) ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrderStatus.New, "Новый")]
    [InlineData(OrderStatus.Accepted, "Принят")]
    [InlineData(OrderStatus.Ready, "Готов к выдаче")]
    [InlineData(OrderStatus.Issued, "Выдан")]
    [InlineData(OrderStatus.Rejected, "Отклонён")]
    [InlineData(OrderStatus.CancelledByCustomer, "Отменён покупателем")]
    [InlineData(OrderStatus.CancelledByShop, "Отменён магазином")]
    [InlineData(OrderStatus.NotPickedUp, "Не забран")]
    public void StatusText_MatchesContract(OrderStatus status, string text) => OrderTexts.StatusText(status).Should().Be(text);

    [Theory]
    [InlineData(ProductUnit.Piece, 2, "2 шт")]
    [InlineData(ProductUnit.Weight, 800, "0,8 кг")]
    [InlineData(ProductUnit.Weight, 1250, "1,25 кг")]
    [InlineData(ProductUnit.Weight, 1000, "1 кг")]
    [InlineData(ProductUnit.Weight, 540, "0,54 кг")]
    public void Quantity(ProductUnit unit, int quantity, string expected) => OrderTexts.Quantity(unit, quantity).Should().Be(expected);

    [Theory]
    [InlineData(150, "150 ₽")]
    [InlineData(1234.5, "1 234,50 ₽")]
    [InlineData(0.99, "0,99 ₽")]
    public void Money(double amount, string expected) => OrderTexts.Money((decimal)amount).Should().Be(expected);

    [Fact]
    public void InsufficientStockMessage_MatchesContractExamples()
    {
        OrderTexts.OnlyLeft(ProductUnit.Piece, 2).Should().Be("Осталось только 2 шт");
        OrderTexts.OnlyLeft(ProductUnit.Weight, 800).Should().Be("Осталось только 0,8 кг");
        OrderTexts.OnlyLeft(ProductUnit.Piece, 0).Should().Be("Закончилось");
        OrderTexts.PriceWas(250m, 270m).Should().Be("Было 250 ₽, стало 270 ₽");
    }

    [Fact]
    public void InvalidTransition_UsesTheStatusText() =>
        OrderTexts.InvalidTransition(OrderStatus.Issued).Should().Be("Это действие недоступно для заказа в статусе «Выдан»");

    [Fact]
    public void ChangeLine_CoversChangedAddedRemoved()
    {
        OrderTexts.ChangeLine("Шаурма", "2 шт × 250 ₽", "1 шт × 250 ₽").Should().Be("Шаурма: 2 шт × 250 ₽ → 1 шт × 250 ₽");
        OrderTexts.ChangeLine("Чай", null, "1 шт × 80 ₽").Should().Contain("добавлено");
        OrderTexts.ChangeLine("Чай", "1 шт × 80 ₽", null).Should().Contain("убрано");
    }

    [Fact]
    public void EventText_AutoAccept_AndReason()
    {
        OrderTexts.EventText(OrderEventKind.Created, OrderStatus.Accepted, null).Should().Be("Заказ создан и принят автоматически");
        OrderTexts.EventText(OrderEventKind.Rejected, OrderStatus.Rejected, "нет товара").Should().Be("Заказ отклонён. Причина: нет товара");
    }

    [Fact]
    public void ActorName_HidesNothingItShouldNotInvent()
    {
        OrderTexts.ActorName(OrderActorKind.Staff, "Анна").Should().Be("Анна");
        OrderTexts.ActorName(OrderActorKind.Staff, null).Should().Be("Сотрудник");
        OrderTexts.ActorName(OrderActorKind.Guest, "Анна").Should().Be("Гость");
    }
}
