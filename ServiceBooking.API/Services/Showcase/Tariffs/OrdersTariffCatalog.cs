namespace ServiceBooking.API.Services.Showcase.Tariffs;

/// <summary>One paid tariff of the "Заказы" grid (ARCHITECTURE_CYCLE37.md §37.10.1).</summary>
public sealed record OrdersTariff(
    Guid Id, string Name, decimal PricePerMonth, int? MaxCompanies, int? MaxEmployees,
    int? MaxProductsPerShop, int? MaxOrdersPerMonth, int SortOrder, string Description, IReadOnlyList<string> Highlights);

/// <summary>
/// The pure description of the paid "Заказы" grid approved by the customer (Q37-1; values in ARCHITECTURE_CYCLE37.md §37.10.1, wording in
/// SPEC_CYCLE37 §6.2). Ids are literals: the seeder finds a row by id first and by name (within the Orders line only) second.
/// Advantages name only what exists; today every Orders feature is open on any tariff and the tiers differ by limits.
/// </summary>
public static class OrdersTariffCatalog
{
    public static readonly Guid LavkaId = Guid.Parse("5a1e0c37-0000-4000-8000-000000000020");
    public static readonly Guid ShopId = Guid.Parse("5a1e0c37-0000-4000-8000-000000000030");
    public static readonly Guid ChainId = Guid.Parse("5a1e0c37-0000-4000-8000-000000000040");

    public static readonly OrdersTariff Lavka = new(
        LavkaId, "Лавка", 690m, MaxCompanies: 1, MaxEmployees: 5, MaxProductsPerShop: 300, MaxOrdersPerMonth: 1500, SortOrder: 20,
        Description: "Для пекарни, кофейни или небольшого магазина с постоянным потоком заказов.",
        Highlights:
        [
            "Всё, что в бесплатном",
            "Предзаказ на дату и меню на день",
            "Весовые товары с точной суммой при выдаче",
        ]);

    public static readonly OrdersTariff Shop = new(
        ShopId, "Магазин", 1490m, MaxCompanies: 3, MaxEmployees: 15, MaxProductsPerShop: 1000, MaxOrdersPerMonth: 5000, SortOrder: 30,
        Description: "Для магазина с несколькими точками: до 3 магазинов на одной подписке.",
        Highlights:
        [
            "Всё, что в «Лавке»",
            "До 3 магазинов на одной подписке",
            "Отчёты: история заказов, сводка, лист сборки",
        ]);

    public static readonly OrdersTariff Chain = new(
        ChainId, "Сеть магазинов", 2990m, MaxCompanies: null, MaxEmployees: null, MaxProductsPerShop: 1000, MaxOrdersPerMonth: null, SortOrder: 40,
        Description: "Для сети от 4 магазинов. Стоимость от 2 990 ₽ в месяц, зависит от числа магазинов.",
        Highlights:
        [
            "Всё, что в «Магазине»",
            "Магазины и участники без ограничения",
            "Итоговая цена — после заявки, по числу магазинов",
        ]);

    /// <summary>Tariffs created by <c>ops tariffs apply</c>, in creation order.</summary>
    public static readonly IReadOnlyList<OrdersTariff> Grid = [Lavka, Shop, Chain];

    // ── The system free tariff (customer decision Q37-2) ───────────────────────────────────────────────────────
    // Seeded by migration Cycle24OrdersTimeNotifyTariffs, byte for byte; Highlights = NULL, IsPublic = false, SortOrder = -1.
    // The seeder changes a field only while it still has this value.
    public const string LegacyFreeName = "Заказы · Бесплатно";
    public const string LegacyFreeDescription = "Бесплатный уровень линейки «Заказы».";
    public const int LegacyFreeSortOrder = -1;

    public const string FreeName = "Бесплатный";
    public const string FreeDescription = "Чтобы начать: один магазин, приём заказов по ссылке, QR-коду и из каталога.";
    public const string FreeHighlights =
        "Экран заказов со звуком нового заказа\nВремя получения: как можно скорее или к часу\nПоказ в каталоге goods";
    public const int FreeSortOrder = 10;

    /// <summary>The only option a created Orders tariff gets as "extra" (the free tariff's own rule from the cycle-24 migration).</summary>
    public const string WhatsAppOptionCode = "notifications.whatsapp";
}
