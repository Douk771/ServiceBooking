using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Showcase.Tariffs;

/// <summary>One tariff of the "Записи" grid (ARCHITECTURE_CYCLE28.md §573.2).</summary>
public sealed record ZapisTariff(
    Guid Id, string Name, decimal PricePerMonth, int? MaxCompanies, int? MaxEmployees,
    int? PhotoQuotaMb, PhotoRetention PhotoRetention, bool IsPublic, int SortOrder, bool IsSystemTrial,
    string Description, IReadOnlyList<string> Highlights);

/// <summary>
/// The pure description of the tariff grid approved by the customer (D-2, values in ARCHITECTURE_CYCLE28.md §573.2). Ids are literals:
/// the seeder finds a row by id first and by name second, so a row an administrator already created by hand is never duplicated.
///
/// Advantages are worded ONLY through what the product really limits (§573.3, §573.4a): no "custom domain", no "priority support",
/// no "form without a signature", no "summary over all branches" — none of them exist. Photo limits are the architect's proposal
/// accepted by the customer (Q28-3, variant A) and are edited in the admin panel without a deploy.
/// </summary>
public static class ZapisTariffCatalog
{
    public static readonly Guid StudioId = Guid.Parse("5a1e0c28-0000-4000-8000-000000000020");
    public static readonly Guid SalonId = Guid.Parse("5a1e0c28-0000-4000-8000-000000000030");
    public static readonly Guid NetworkId = Guid.Parse("5a1e0c28-0000-4000-8000-000000000040");
    public static readonly Guid TrialId = Guid.Parse("5a1e0c28-0000-4000-8000-000000000010");

    public static readonly ZapisTariff Trial = new(
        TrialId, "Пробный период", 0m, MaxCompanies: 3, MaxEmployees: 15, PhotoQuotaMb: 3000, PhotoRetention.TwelveMonths,
        IsPublic: true, SortOrder: 10, IsSystemTrial: true,
        Description: "14 дней возможностей тарифа «Салон» бесплатно.",
        Highlights: ["14 дней тарифа «Салон»", "Один раз на подтверждённый номер телефона", "Потом — бесплатный тариф, ничего не удаляется"]);

    public static readonly ZapisTariff Studio = new(
        StudioId, "Студия", 790m, MaxCompanies: 1, MaxEmployees: 5, PhotoQuotaMb: 1000, PhotoRetention.SixMonths,
        IsPublic: true, SortOrder: 20, IsSystemTrial: false,
        Description: "Для небольшого салона или барбершопа: 1 компания, до 5 сотрудников.",
        Highlights:
        [
            "Онлайн-запись клиентов на странице компании",
            "Отчёты по выручке компании и мастеров",
            "Комиссии мастеров в отчётах",
            "Фото к заметкам о клиентах — до 1 ГБ, хранение 6 месяцев",
        ]);

    public static readonly ZapisTariff Salon = new(
        SalonId, "Салон", 1890m, MaxCompanies: 3, MaxEmployees: 15, PhotoQuotaMb: 3000, PhotoRetention.TwelveMonths,
        IsPublic: true, SortOrder: 30, IsSystemTrial: false,
        Description: "Для салона с несколькими точками: до 3 филиалов и 15 сотрудников.",
        Highlights:
        [
            "Всё, что в тарифе «Студия»",
            "До 3 филиалов на одной подписке",
            "До 15 сотрудников во всех филиалах",
            "Фото к заметкам — до 3 ГБ, хранение 12 месяцев",
        ]);

    public static readonly ZapisTariff Network = new(
        NetworkId, "Сеть", 3900m, MaxCompanies: null, MaxEmployees: null, PhotoQuotaMb: 10000, PhotoRetention.TwelveMonths,
        IsPublic: true, SortOrder: 40, IsSystemTrial: false,
        Description: "Для сети от 4 филиалов. Стоимость от 3 900 ₽ в месяц, зависит от числа филиалов.",
        Highlights:
        [
            "Всё, что в тарифе «Салон»",
            "Филиалы и сотрудники без ограничения",
            "Фото к заметкам — до 10 ГБ, хранение 12 месяцев",
            "Итоговая цена — после заявки, по числу филиалов",
        ]);

    /// <summary>Tariffs created by <c>ops tariffs apply</c>, in creation order.</summary>
    public static readonly IReadOnlyList<ZapisTariff> Grid = [Trial, Studio, Salon, Network];

    /// <summary>The hidden service tariff of the showcase (§573.2). Created by <c>ops showcase create</c>, not by the tariff command:
    /// it is a service row, not a product. Not public, no advantages.</summary>
    public static readonly ZapisTariff Showcase = new(
        ShowcaseCatalog.ShowcasePlanId, ShowcaseCatalog.ShowcasePlanName, 0m, MaxCompanies: null, MaxEmployees: null,
        PhotoQuotaMb: null, PhotoRetention.TwelveMonths, IsPublic: false, SortOrder: 900, IsSystemTrial: false,
        Description: "Служебный тариф витринных компаний. Не назначайте его настоящим аккаунтам.",
        Highlights: []);

    // ── The system free tariff "Старт" (customer decision Q28-1) ───────────────────────────────────────────────
    // The row was seeded by migration SeedBillingCatalog (cycle 7). Values of the seed, byte for byte: the seeder changes a field
    // only while it still has exactly this value, i.e. while no administrator touched it.
    public const string LegacyFreeName = "Бесплатный";
    public const string LegacyFreeDescription = "Чтобы попробовать: одна компания, один сотрудник, запись руками.";
    public const string LegacyFreeHighlights = "Показ в каталоге салонов\n1 компания\n1 сотрудник";

    public const string StartName = "Старт";
    public const string StartDescription = "Чтобы начать: онлайн-запись, страница компании и показ в каталоге. Одна компания, до 2 сотрудников.";
    public const string StartHighlights =
        "Онлайн-запись клиентов на странице компании\nПоказ в каталоге салонов\n1 компания\nДо 2 сотрудников";
    public const int StartMaxEmployees = 2;
}
