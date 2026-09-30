using ServiceBooking.API.Services.Orders.Notifications;

namespace ServiceBooking.API.Services.StaffMax;

/// <summary>
/// API_CONTRACT_CYCLE25.md §525 [legal L15] — every fixed text of "MAX for staff", in one pure class: the bot's replies at linking and the messages to
/// the staff. Neutral constants until the lawyer's conclusion; changing a text is a change of this file only. NO customer name or phone anywhere.
/// </summary>
public static class StaffMaxTexts
{
    public const int MaxShopNamesInReply = 5;
    public const int MaxMessageLength = 2000;

    // ── the bot's replies (§525.2) ──────────────────────────────────────────────────────────────────────────

    public static string Linked(IReadOnlyList<string> shopNames)
    {
        var quoted = shopNames.Take(MaxShopNamesInReply).Select(n => $"«{n}»").ToList();
        var list = string.Join(", ", quoted);
        if (shopNames.Count > MaxShopNamesInReply) list += $" и ещё {shopNames.Count - MaxShopNamesInReply}";
        return $"Готово: сюда будут приходить новые заказы и отмены покупателями из магазинов: {list}. " +
               "Отключить можно в кабинете goods.ezbook.ru → «Уведомления на это устройство» или остановив этого бота.";
    }

    public const string AlreadyLinked = "Этот чат уже подключён к заказам ezbook.";

    public const string LinkExpired = "Ссылка устарела. Получите новую в кабинете goods.ezbook.ru → «Уведомления на это устройство».";

    public const string Disabled = "Сообщения о заказах в MAX пока не включены.";

    public const string NoShops = "Вы больше не состоите ни в одном магазине — подключать нечего.";

    // ── messages to staff (§525.1) ──────────────────────────────────────────────────────────────────────────

    /// <summary>"Новый заказ № 27 · к 12:30 · 3 позиции · ≈ 540 ₽ · Шаурма на Ленина" + the link line.</summary>
    public static string OrderCreated(OrderTextFacts facts, string orderUrl)
    {
        var push = OrderNotificationTexts.StaffOrderCreated(facts, Guid.Empty, Guid.Empty);
        return Cap($"{push.Title} · {push.Body}\nОткрыть: {orderUrl}");
    }

    /// <summary>"Покупатель отменил заказ № 27 (к 12:30) · Шаурма на Ленина" + the link line.</summary>
    public static string OrderCancelledByCustomer(OrderTextFacts facts, string orderUrl) =>
        Cap($"Покупатель отменил заказ № {facts.Number} ({OrderNotificationTexts.StaffTimePhrase(facts)}) · " +
            $"{OrderNotificationTexts.ShortName(facts.ShopName)}\nОткрыть: {orderUrl}");

    /// <summary>"Использовано 80 % лимита заказов: 120 из 150 заказов в октябре" / "Лимит заказов исчерпан: …" + the subscription link line.</summary>
    public static string OwnerOrderLimitWarning(bool reached, int used, int limit, DateOnly month, string subscriptionUrl)
    {
        var push = OrderNotificationTexts.OwnerOrderLimitWarning(reached, used, limit, month);
        return Cap($"{push.Title}: {push.Body}\nПодписка: {subscriptionUrl}");
    }

    private static string Cap(string text) => text.Length <= MaxMessageLength ? text : text[..(MaxMessageLength - 1)] + "…";
}
