namespace ServiceBooking.API.Services.Shops;

/// <summary>ARCHITECTURE_CYCLE23.md §409–§410 — the Russian texts of the shop and catalog refusals (the server composes, the frontend prints).</summary>
public static class ShopTexts
{
    public const string SlugInvalid = "Адрес — латиница, цифры и дефис, от 3 до 50 символов";
    public const string SlugReserved = "Этот адрес занят сервисом — выберите другой";
    public const string SlugTaken = "Адрес уже занят — выберите другой";
    public const string PhoneVerificationUnavailable = "Подтверждение телефона сейчас недоступно на платформе — режим включить нельзя";
    public const string CategoryNotEmpty = "В категории есть товары — сначала перенесите или удалите их";
    public const string UnitChangeNotAllowed = "Тип товара (штучный/весовой) менять нельзя — заведите новый товар";
    public const string CategoryNameRequired = "Укажите название категории";
    public const string CategoriesOutdated = "Список категорий устарел — обновите страницу";
    public const string ProductsOutdated = "Список товаров устарел — обновите страницу";
    public const string CategoryNotFound = "Категория не найдена";
    public const string StockInvalid = "Остаток — целое число от 0";
    public const string InnInvalid = "ИНН указан с ошибкой";
    public const string SlugOrNameRequired = "Передайте адрес или название";
    public const string StaffOnly = "В магазин можно добавить только сотрудника.";
    public const string WeekdayUnknown = "Неизвестный день недели";

    public static string CategoryLimitReached(int limit) => $"В магазине уже {limit} категорий";
    public static string ProductLimitReached(int limit) => $"В магазине уже {limit} товаров";
}
