using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Orders.Notifications;

/// <summary>The body of a web-push: the contract with the service worker (<c>{title, body, tag, url}</c>, url relative).</summary>
public sealed record PushPayload(string Title, string Body, string Tag, string Url);

/// <summary>What the texts need to know about an order. No customer name or phone — by design [legal L10, L11].</summary>
public sealed record OrderTextFacts(
    string ShopName, int Number, PickupKind PickupKind, DateOnly PickupDate, int PickupStartMinutes, DateOnly Today,
    int ItemCount, decimal Total, string? Reason = null, int? PreviousNumber = null);

/// <summary>
/// ARCHITECTURE_CYCLE24.md §455, §456.2, §457.1, API_CONTRACT_CYCLE24.md §486 — every fixed text an order notification carries, in one
/// pure class. Texts are NOT editable by a shop and never run through the salon template validator [legal L10, L11]: they contain the
/// order number, the shop, the time, the number of items and the amount — no name, no phone. Order links come from
/// <c>PublicSiteLinks</c> and are passed in.
/// </summary>
public static class OrderNotificationTexts
{
    // ── time phrases ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>For staff: "как можно скорее", "к 12:30", "пт 2 окт, к 12:30".</summary>
    public static string StaffTimePhrase(OrderTextFacts f)
    {
        if (f.PickupKind == PickupKind.Asap) return "как можно скорее";
        var clock = ShopTimeTexts.Clock(f.PickupStartMinutes);
        return f.PickupDate == f.Today ? $"к {clock}" : $"{ShopTimeTexts.DateShort(f.PickupDate)}, к {clock}";
    }

    /// <summary>For the customer, lower case: "как можно скорее", "сегодня к 12:30", "завтра к 12:30", "пт 2 окт, к 12:30".</summary>
    public static string CustomerTimePhrase(OrderTextFacts f)
    {
        if (f.PickupKind == PickupKind.Asap) return "как можно скорее";
        var clock = ShopTimeTexts.Clock(f.PickupStartMinutes);
        if (f.PickupDate == f.Today) return $"сегодня к {clock}";
        if (f.PickupDate == f.Today.AddDays(1)) return $"завтра к {clock}";
        return $"{ShopTimeTexts.DateShort(f.PickupDate)}, к {clock}";
    }

    // ── staff push ──────────────────────────────────────────────────────────────────────────────────────────

    public static string StaffOrderUrl(Guid shopId, Guid orderId) => $"/cabinet/{shopId}/orders?order={orderId}";

    public static string OrderTag(Guid orderId) => $"o-{orderId}";

    /// <summary>"Новый заказ № 27" / "к 12:30 · 3 позиции · ≈ 540 ₽ · Шаурма на Ленина".</summary>
    public static PushPayload StaffOrderCreated(OrderTextFacts f, Guid shopId, Guid orderId) => new(
        $"Новый заказ № {f.Number}",
        $"{StaffTimePhrase(f)} · {f.ItemCount} {ShopTimeTexts.Plural(f.ItemCount, "позиция", "позиции", "позиций")} · ≈ {OrderTexts.Money(f.Total)} · {f.ShopName}",
        OrderTag(orderId), StaffOrderUrl(shopId, orderId));

    /// <summary>"Покупатель отменил заказ № 27" / "к 12:30 · Шаурма на Ленина".</summary>
    public static PushPayload StaffOrderCancelledByCustomer(OrderTextFacts f, Guid shopId, Guid orderId) => new(
        $"Покупатель отменил заказ № {f.Number}", $"{StaffTimePhrase(f)} · {f.ShopName}", OrderTag(orderId), StaffOrderUrl(shopId, orderId));

    /// <summary>The owner's warning at 80 % / 100 % of the monthly limit (§459.6). <c>month</c> is the first day of the month.</summary>
    public static PushPayload OwnerOrderLimitWarning(bool reached, int used, int limit, DateOnly month)
    {
        // "в октябре": the prepositional case of the month is the one the body needs.
        var inMonth = PrepositionalMonth(month);
        return new PushPayload(
            reached ? "Лимит заказов исчерпан" : "Использовано 80 % лимита заказов",
            reached
                ? $"{used} из {limit} в {inMonth}. Новые заказы не принимаются до {ShopTimeTexts.FirstOfNextMonth(month)} или смены тарифа"
                : $"{used} из {limit} заказов в {inMonth}",
            $"limit-{month:yyyy-MM}", "/cabinet/subscription");
    }

    private static string PrepositionalMonth(DateOnly month) => month.Month switch
    {
        1 => "январе", 2 => "феврале", 3 => "марте", 4 => "апреле", 5 => "мае", 6 => "июне",
        7 => "июле", 8 => "августе", 9 => "сентябре", 10 => "октябре", 11 => "ноябре", _ => "декабре"
    };

    // ── customer: web-push and messenger ────────────────────────────────────────────────────────────────────

    /// <summary>The sentence of a customer notification, capitalised, with no shop name and no link.</summary>
    public static string CustomerSentence(NotificationType type, OrderTextFacts f) => type switch
    {
        NotificationType.OrderAccepted => $"Заказ № {f.Number} принят. Получение: {CustomerTimePhrase(f)}.",
        NotificationType.OrderReady => $"Заказ № {f.Number} готов к выдаче.",
        NotificationType.OrderRejected => $"Заказ № {f.Number} отклонён.{ReasonSuffix(f.Reason)}",
        NotificationType.OrderCancelledByShop => $"Заказ № {f.Number} отменён магазином.{ReasonSuffix(f.Reason)}",
        NotificationType.OrderEditedByShop => $"Магазин изменил заказ № {f.Number}, итог ≈ {OrderTexts.Money(f.Total)}. Подробности по ссылке.",
        NotificationType.OrderPickupChanged => PickupChangedSentence(f),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a customer order notification.")
    };

    private static string PickupChangedSentence(OrderTextFacts f)
    {
        // The customer knows the old number; the new one is named only when the pickup date moved and the number changed with it.
        var oldNumber = f.PreviousNumber ?? f.Number;
        var renumbered = f.PreviousNumber is { } previous && previous != f.Number ? $" (теперь № {f.Number})" : string.Empty;
        return $"Изменено время получения заказа № {oldNumber}{renumbered}: {CustomerTimePhrase(f)}.";
    }

    private static string ReasonSuffix(string? reason) => string.IsNullOrWhiteSpace(reason) ? string.Empty : $" Причина: {reason.Trim()}";

    public static string CustomerOrderUrl(string token) => $"/o/{token}";

    /// <summary>Web-push to the customer: the shop is the title, the sentence the body; the token stays inside the encrypted payload.</summary>
    public static PushPayload CustomerWebPush(NotificationType type, OrderTextFacts f, Guid orderId, string token) =>
        new(f.ShopName, CustomerSentence(type, f), $"co-{orderId}", CustomerOrderUrl(token));

    /// <summary>The messenger text: "{Shop}: заказ № 27 принят. …" + the order link line + the unsubscribe line.</summary>
    public static string Messenger(NotificationType type, OrderTextFacts f, string orderUrl, string? unsubscribeUrl)
    {
        var text = $"{f.ShopName}: {LowerFirst(CustomerSentence(type, f))}\nЗаказ: {orderUrl}";
        return string.IsNullOrEmpty(unsubscribeUrl) ? text : $"{text}\nОтказаться от уведомлений: {unsubscribeUrl}";
    }

    private static string LowerFirst(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];
}
