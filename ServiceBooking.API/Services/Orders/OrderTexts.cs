using System.Globalization;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Orders;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §396.1, §420 — every Russian text a customer or staff member reads about an order is
/// assembled HERE, on the server (the project convention: the frontend prints, it does not compose). Pure.
/// </summary>
public static class OrderTexts
{
    private static readonly CultureInfo Ru = new("ru-RU");

    public static string StatusText(OrderStatus status) => status switch
    {
        OrderStatus.New => "Новый",
        OrderStatus.Accepted => "Принят",
        OrderStatus.Ready => "Готов к выдаче",
        OrderStatus.Issued => "Выдан",
        OrderStatus.Rejected => "Отклонён",
        OrderStatus.CancelledByCustomer => "Отменён покупателем",
        OrderStatus.CancelledByShop => "Отменён магазином",
        OrderStatus.NotPickedUp => "Не забран",
        _ => status.ToString()
    };

    /// <summary>Titles of the four timeline steps New → Accepted → Ready → Issued.</summary>
    public static string TimelineTitle(OrderStatus step) => step switch
    {
        OrderStatus.New => "Заказ оформлен",
        OrderStatus.Accepted => "Магазин принял заказ",
        OrderStatus.Ready => "Заказ собран",
        OrderStatus.Issued => "Заказ выдан",
        _ => StatusText(step)
    };

    /// <summary>Money as the customer reads it: "150 ₽", "1 234,50 ₽".</summary>
    public static string Money(decimal amount)
    {
        var rounded = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        var whole = rounded == Math.Truncate(rounded);
        return rounded.ToString(whole ? "#,0" : "#,0.00", Ru).Replace(' ', ' ') + " ₽";
    }

    /// <summary>Quantity in the product's unit: "2 шт", weight always in kilograms — "0,8 кг", "1,25 кг".</summary>
    public static string Quantity(ProductUnit unit, int quantity)
    {
        if (unit == ProductUnit.Piece) return $"{quantity} шт";
        var kg = quantity / 1000m;
        return kg.ToString("0.###", Ru) + " кг";
    }

    /// <summary>"2 шт × 150 ₽" / "0,54 кг × 540 ₽/кг" — one line of an order in the "was → became" texts.</summary>
    public static string LineSummary(ProductUnit unit, int quantity, decimal unitPrice) =>
        unit == ProductUnit.Piece
            ? $"{Quantity(unit, quantity)} × {Money(unitPrice)}"
            : $"{Quantity(unit, quantity)} × {Money(unitPrice)}/кг";

    /// <summary>The ready-made "was → became" line of an edit.</summary>
    public static string ChangeLine(string name, string? before, string? after) =>
        (before, after) switch
        {
            (null, null) => name,
            (null, _) => $"{name}: добавлено — {after}",
            (_, null) => $"{name}: убрано (было {before})",
            _ => $"{name}: {before} → {after}"
        };

    public static string ActorName(OrderActorKind kind, string? snapshot) => kind switch
    {
        OrderActorKind.Customer => "Покупатель",
        OrderActorKind.Guest => "Гость",
        OrderActorKind.Staff => string.IsNullOrWhiteSpace(snapshot) ? "Сотрудник" : snapshot,
        OrderActorKind.SuperAdmin => "Администратор платформы",
        _ => "Система"
    };

    /// <summary>The journal line of one event.</summary>
    public static string EventText(OrderEventKind kind, OrderStatus? toStatus, string? reason)
    {
        var text = kind switch
        {
            OrderEventKind.Created => toStatus == OrderStatus.Accepted ? "Заказ создан и принят автоматически" : "Заказ создан",
            OrderEventKind.Accepted => "Заказ принят",
            OrderEventKind.Rejected => "Заказ отклонён",
            OrderEventKind.MarkedReady => "Заказ готов к выдаче",
            OrderEventKind.Issued => "Заказ выдан",
            OrderEventKind.NotPickedUp => "Заказ не забран",
            OrderEventKind.CancelledByCustomer => "Заказ отменён покупателем",
            OrderEventKind.CancelledByShop => "Заказ отменён магазином",
            OrderEventKind.Edited => "Магазин изменил заказ",
            _ => kind.ToString()
        };
        return string.IsNullOrWhiteSpace(reason) ? text : $"{text}. Причина: {reason}";
    }

    // ── Messages of the 409 bodies (API_CONTRACT_CYCLE23.md §413–§418) ─────────────────────────────────

    public const string ShopNotAvailable = "Магазин недоступен";
    public const string EmptyCart = "Корзина пуста";
    public const string TooManyLines = "В заказе не больше 50 позиций";
    public const string LoginRequired = "Этот магазин принимает заказы только от покупателей с подтверждённым телефоном. Войдите или зарегистрируйтесь";
    public const string PhoneVerificationUnavailable = "Подтверждение телефона сейчас недоступно — заказать в этом магазине пока нельзя";
    public const string PhoneVerificationRequired = "Подтвердите номер телефона через MAX, чтобы оформить заказ";
    public const string PriceChanged = "Цена изменилась — проверьте и подтвердите заказ ещё раз";
    public const string ItemsUnavailable = "Некоторые позиции недоступны — поправьте заказ";
    public const string VersionMismatch = "Заказ уже изменён — вот актуальное состояние";
    public const string AlreadyReady = "Заказ уже собран — свяжитесь с магазином";
    public const string CancelNotAllowed = "Отменить этот заказ нельзя — свяжитесь с магазином";
    public const string LastItemCannotBeRemoved = "Пустой заказ не бывает — отклоните или отмените заказ";
    public const string ProductUnavailable = "Товар больше не продаётся";
    public const string InvalidQuantity = "Количество указано неверно — проверьте шаг и пределы";
    public const string InsufficientStock = "Не хватает остатка";

    public static string InvalidTransition(OrderStatus current) =>
        $"Это действие недоступно для заказа в статусе «{StatusText(current)}»";

    /// <summary>"Осталось только 2 шт"; when nothing is left — "Закончилось" ("only 0 pcs left" reads wrong).</summary>
    public static string OnlyLeft(ProductUnit unit, int available) =>
        available <= 0 ? "Закончилось" : $"Осталось только {Quantity(unit, available)}";

    public static string PriceWas(decimal was, decimal now) => $"Было {Money(was)}, стало {Money(now)}";

    public static string ProblemMessage(OrderProblemReason reason, ProductUnit unit, int? availableQuantity, int? minQuantity) => reason switch
    {
        OrderProblemReason.NotFound => ProductUnavailable,
        OrderProblemReason.Unpublished => "Товар больше не продаётся",
        OrderProblemReason.CategoryHidden => "Товар больше не продаётся",
        OrderProblemReason.SoldOut => "Закончилось",
        OrderProblemReason.InsufficientStock => OnlyLeft(unit, availableQuantity ?? 0),
        OrderProblemReason.BelowMinimum => $"Минимальный заказ — {Quantity(unit, minQuantity ?? 1)}",
        OrderProblemReason.InvalidQuantity => InvalidQuantity,
        _ => ItemsUnavailable
    };
}
