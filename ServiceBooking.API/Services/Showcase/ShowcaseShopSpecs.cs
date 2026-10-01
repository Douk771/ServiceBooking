using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Showcase;

/// <summary>
/// One product of a demo shop. <see cref="Image"/> is the last part of the asset key <c>product.&lt;category&gt;.&lt;image&gt;</c> (§35.11): null means "no picture", an
/// image missing from the manifest is skipped. <see cref="Stock"/> is set only in a shop that tracks stock. <see cref="SoldOutToday"/> marks the product that the
/// demo shows as "закончилось на сегодня" (and that no active order of the demo contains). <see cref="Popularity"/> is the relative weight in an order.
/// </summary>
public sealed record ShowcaseProductSpec(
    string Name, decimal Price, string? Portion = null, string? Description = null, string? Composition = null,
    ProductUnit Unit = ProductUnit.Piece, int StepGrams = 0, int MinGrams = 0, int? Stock = null, int WeekdaysMask = 127,
    string? Image = null, int Popularity = 3, bool SoldOutToday = false)
{
    public const int WeekdayMaskAll = 127;
}

public sealed record ShowcaseCategorySpec(string Name, IReadOnlyList<ShowcaseProductSpec> Products);

/// <summary>A working interval of one weekday in minutes from midnight of that day.</summary>
public readonly record struct ShowcaseHours(DayOfWeek Day, int StartMinutes, int EndMinutes);

/// <summary>A part of the day into which customers' pickup times fall, with its relative weight.</summary>
public readonly record struct ShowcasePeak(int StartMinutes, int EndMinutes, int Weight);

/// <summary>A range of orders per day (inclusive).</summary>
public readonly record struct ShowcaseDailyRange(int Min, int Max);

/// <summary>
/// One demo shop (ARCHITECTURE_CYCLE35.md §35.9.2). <see cref="Key"/> is stable and goes into ids; <see cref="SlugBase"/> is appended to <c>primer-</c>.
/// <see cref="AssetCategory"/> names the pictures (<c>logo.shop.&lt;category&gt;</c>, <c>photo.shop.&lt;category&gt;.&lt;n&gt;</c>, <c>product.&lt;category&gt;.&lt;image&gt;</c>).
/// </summary>
public sealed record ShowcaseShopSpec(
    string Key, string Name, string SlugBase, string City, string TimeZoneId, string Street, string Description, string AssetCategory,
    string OwnerFirstName, string OwnerLastName, int StaffCount, IReadOnlyList<ShowcaseHours> Hours,
    bool AsapEnabled, bool ScheduledEnabled, int SlotStepMinutes, int PreorderDays, int MinPrepMinutes,
    OrderAcceptanceMode Acceptance, bool TrackStock, string SellerLegalName,
    ShowcaseDailyRange WeekdayOrders, ShowcaseDailyRange WeekendOrders, double AsapShare, IReadOnlyList<ShowcasePeak> Peaks,
    IReadOnlyList<ShowcaseCategorySpec> Categories)
{
    public int ProductCount => Categories.Sum(c => c.Products.Count);
}

/// <summary>The pool of customers of one city: people of Moscow do not order in Novosibirsk (§35.9.2).</summary>
public sealed record ShowcaseCityPool(string City, int Registered, int Guests, IReadOnlyList<string> ShopKeys);

/// <summary>
/// The data of the five demo shops: text, prices, hours, catalogs. Pure data in code, like <see cref="ShowcaseSpecs"/>: no dates, no randomness — the same on every
/// machine. Shop and person names are fictional, contain no real brands and no real addresses (a street, no house number). The food-information text of a
/// product is neutral ("Состав: …. Может содержать …"); no health data anywhere (US-35-02).
/// </summary>
public static class ShowcaseShopSpecs
{
    private const int FridayOnly = 16; // bit 4 = Friday (WeekdayMask)

    private static ShowcaseProductSpec P(
        string name, decimal price, string? portion = null, string? image = null, int popularity = 3, string? description = null, string? composition = null,
        int? stock = null, int weekdays = ShowcaseProductSpec.WeekdayMaskAll, bool soldOut = false) =>
        new(name, price, portion, description, composition, ProductUnit.Piece, 0, 0, stock, weekdays, image, popularity, soldOut);

    private static ShowcaseProductSpec W(
        string name, decimal pricePerKg, int step, int min, string? image = null, int popularity = 3, string? description = null, string? composition = null) =>
        new(name, pricePerKg, null, description, composition, ProductUnit.Weight, step, min, null, ShowcaseProductSpec.WeekdayMaskAll, image, popularity, false);

    private static ShowcaseCategorySpec C(string name, params ShowcaseProductSpec[] products) => new(name, products);

    private static IReadOnlyList<ShowcaseHours> Daily(int start, int end) =>
        Enum.GetValues<DayOfWeek>().Select(d => new ShowcaseHours(d, start, end)).ToList();

    private static IReadOnlyList<ShowcaseHours> Weekdays(int start, int end) =>
        new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }.Select(d => new ShowcaseHours(d, start, end)).ToList();

    private static int H(int hours, int minutes = 0) => hours * 60 + minutes;

    /// <summary>The coffee shop of the demo owner: the one the roles «владелец», «сотрудник» and «покупатель» work with.</summary>
    public static readonly ShowcaseShopSpec Kofeinya = new(
        Key: "kofeinya", Name: "Кофейня «Зерно и корица»", SlugBase: "kofeinya", City: "Москва", TimeZoneId: "Europe/Moscow",
        Street: "Садовая улица", AssetCategory: "coffee",
        Description: "Небольшая кофейня у дома: свежая обжарка, выпечка каждое утро и завтраки. Заказывайте заранее или «как можно скорее» и забирайте без очереди.",
        OwnerFirstName: "Андрей", OwnerLastName: "Воронов", StaffCount: 3, Hours: Daily(H(7), H(23)),
        AsapEnabled: true, ScheduledEnabled: true, SlotStepMinutes: 15, PreorderDays: 1, MinPrepMinutes: 15,
        Acceptance: OrderAcceptanceMode.Manual, TrackStock: false, SellerLegalName: "Кофейня «Зерно и корица» (пример)",
        WeekdayOrders: new(35, 55), WeekendOrders: new(30, 45), AsapShare: 0.65,
        Peaks: [new(H(7), H(10), 40), new(H(12), H(14, 30), 25), new(H(15), H(19), 22), new(H(19), H(22, 30), 13)],
        Categories:
        [
            C("Кофе",
                P("Эспрессо", 150, "30 мл", "espresso", 4, "Двойной шот средней обжарки."),
                P("Американо", 190, "250 мл", "americano", 5),
                P("Капучино", 260, "250 мл", "cappuccino", 8, "Плотная молочная пена, сбалансированный вкус.", "Состав: кофе, молоко."),
                P("Латте", 290, "300 мл", "latte", 8, "Мягкий кофе с большим количеством молока.", "Состав: кофе, молоко."),
                P("Флэт уайт", 290, "200 мл", "flat-white", 4, null, "Состав: кофе, молоко."),
                P("Раф", 330, "300 мл", "raf", 4, "Сливки, ванильный сахар, эспрессо.", "Состав: кофе, сливки, сахар."),
                P("Айс-латте", 320, "400 мл", null, 3, "Холодный кофе со льдом и молоком."),
                P("Фильтр-кофе", 180, "300 мл", null, 3)),
            C("Чай и какао",
                P("Чёрный чай", 170, "400 мл", "tea-black", 3),
                P("Зелёный чай", 170, "400 мл", "tea-green", 2),
                P("Травяной сбор", 180, "400 мл", "tea-herbal", 2, "Мята, мелисса, липа."),
                P("Какао", 240, "300 мл", "cocoa", 3, null, "Состав: какао, молоко, сахар."),
                P("Горячий шоколад", 290, "250 мл", null, 2, null, "Состав: шоколад, молоко, сливки."),
                P("Облепиховый чай", 260, "500 мл", null, 2, "С мёдом и апельсином.")),
            C("Выпечка",
                P("Круассан", 190, "1 шт", "croissant", 7, "Слоёный, на сливочном масле.", "Состав: мука пшеничная, масло сливочное, молоко, яйцо. Может содержать следы орехов."),
                P("Круассан с миндалём", 240, "1 шт", "croissant", 3, null, "Состав: мука пшеничная, масло сливочное, миндаль, яйцо."),
                P("Маффин шоколадный", 170, "1 шт", "muffin", 4, null, "Состав: мука пшеничная, шоколад, яйцо, молоко."),
                P("Булочка с корицей", 180, "1 шт", null, 5, null, "Состав: мука пшеничная, корица, масло сливочное, сахар."),
                P("Печенье овсяное", 90, "1 шт", "cookie", 3),
                P("Сырники", 290, "3 шт", null, 3, "Со сметаной и ягодным соусом.", "Состав: творог, яйцо, мука, сахар.")),
            C("Десерты",
                P("Чизкейк", 290, "120 г", "cheesecake", 4, "Классический, на песочной основе.", "Состав: сливочный сыр, яйцо, мука, сахар, масло сливочное."),
                P("Тирамису", 320, "130 г", null, 3, null, "Состав: маскарпоне, яйцо, кофе, печенье, какао."),
                P("Брауни", 210, "1 шт", null, 3, "С грецким орехом.", "Состав: шоколад, мука, яйцо, орех грецкий."),
                P("Медовик", 260, "120 г", null, 2)),
            C("Завтраки и сэндвичи",
                P("Сэндвич с курицей", 350, "200 г", "sandwich", 4, "Курица, салат, соус, хлеб на закваске."),
                P("Тост с авокадо", 380, "1 шт", "toast-avocado", 3, "Авокадо, яйцо пашот, зерновой хлеб."),
                P("Гранола с йогуртом", 290, "250 г", "granola", 3, null, "Состав: овсяные хлопья, мёд, йогурт, орехи."),
                P("Овсяная каша", 240, "300 г", null, 3, "На молоке, с ягодами."),
                P("Сэндвич с сыром", 290, "180 г", null, 2)),
        ]);

    /// <summary>The bakery: acceptance is automatic, stock is tracked, a couple of products are sold out.</summary>
    public static readonly ShowcaseShopSpec Pekarnya = new(
        Key: "pekarnya", Name: "Пекарня «Золотой колос»", SlugBase: "pekarnya", City: "Москва", TimeZoneId: "Europe/Moscow",
        Street: "Пекарский переулок", AssetCategory: "bakery",
        Description: "Хлеб, булочки и пироги из печи каждый день. Оформите заказ на удобное время и заберите тёплым.",
        OwnerFirstName: "Ирина", OwnerLastName: "Сафонова", StaffCount: 2, Hours: Daily(H(7), H(21)),
        AsapEnabled: false, ScheduledEnabled: true, SlotStepMinutes: 30, PreorderDays: 2, MinPrepMinutes: 30,
        Acceptance: OrderAcceptanceMode.Auto, TrackStock: true, SellerLegalName: "Пекарня «Золотой колос» (пример)",
        WeekdayOrders: new(10, 20), WeekendOrders: new(12, 20), AsapShare: 0,
        Peaks: [new(H(7), H(10), 38), new(H(11), H(14), 22), new(H(16), H(20, 30), 40)],
        Categories:
        [
            C("Хлеб",
                P("Батон нарезной", 75, "400 г", "loaf", 6, null, "Состав: мука пшеничная, вода, дрожжи, соль, сахар.", 45),
                P("Багет классический", 95, "250 г", "baguette", 5, null, "Состав: мука пшеничная, вода, закваска, соль.", 35),
                P("Хлеб ржаной", 110, "500 г", "rye-bread", 4, null, "Состав: мука ржаная, мука пшеничная, закваска, солод, соль.", 30),
                P("Хлеб зерновой", 130, "450 г", null, 3, null, "Состав: мука пшеничная, семена подсолнечника, лён, кунжут, закваска.", 25),
                P("Чиабатта", 90, "300 г", null, 3, null, "Состав: мука пшеничная, вода, оливковое масло, соль.", 28),
                P("Хлеб на закваске", 160, "600 г", null, 3, null, "Состав: мука пшеничная, закваска, вода, соль.", 18)),
            C("Булочки",
                P("Булочка с маком", 65, "1 шт", "bun", 4, null, "Состав: мука пшеничная, мак, молоко, яйцо, сахар.", 40),
                P("Булочка с корицей", 85, "1 шт", "cinnamon-roll", 6, null, "Состав: мука пшеничная, корица, масло сливочное, сахар, яйцо.", 38),
                P("Плюшка сахарная", 55, "1 шт", "bun", 3, null, "Состав: мука пшеничная, сахар, масло сливочное.", 42),
                P("Слойка с сыром", 95, "1 шт", null, 4, null, "Состав: мука пшеничная, сыр, масло сливочное, яйцо.", 26),
                P("Слойка с вишней", 90, "1 шт", null, 3, null, "Состав: мука пшеничная, вишня, масло сливочное, сахар.", 0, ShowcaseProductSpec.WeekdayMaskAll, true),
                P("Круассан", 110, "1 шт", null, 5, null, "Состав: мука пшеничная, масло сливочное, молоко, яйцо.", 32)),
            C("Пироги",
                P("Пирог яблочный", 420, "целый, 800 г", "pie-apple", 3, null, "Состав: мука пшеничная, яблоки, масло сливочное, яйцо, сахар, корица.", 10),
                P("Пирог с капустой", 380, "целый, 800 г", "pie-cabbage", 2, null, "Состав: мука пшеничная, капуста, яйцо, лук, масло сливочное.", 8),
                P("Пирог мясной", 480, "целый, 850 г", "pie-meat", 3, null, "Состав: мука пшеничная, говядина, лук, яйцо, масло сливочное.", 7),
                P("Пирожок с картошкой", 70, "1 шт", null, 5, null, "Состав: мука пшеничная, картофель, лук, масло растительное.", 36),
                P("Пирожок с повидлом", 65, "1 шт", null, 4, null, "Состав: мука пшеничная, повидло яблочное, сахар.", 0, ShowcaseProductSpec.WeekdayMaskAll, true)),
            C("Торты и пирожные",
                P("Торт «Медовик»", 1450, "1 кг", "cake-honey", 2, "Медовые коржи и сметанный крем.", "Состав: мука пшеничная, мёд, сметана, яйцо, сахар.", 4),
                P("Торт шоколадный", 1650, "1 кг", "cake-chocolate", 2, null, "Состав: мука пшеничная, шоколад, сливки, яйцо, сахар.", 3),
                P("Эклер", 130, "1 шт", "eclair", 5, "Заварное тесто, ванильный крем.", "Состав: мука пшеничная, молоко, яйцо, масло сливочное, сахар.", 24),
                P("Корзиночка", 120, "1 шт", null, 3, null, "Состав: мука пшеничная, масло сливочное, сливки, ягоды.", 18),
                P("Тарталетка ягодная", 160, "1 шт", null, 3, null, "Состав: мука пшеничная, масло сливочное, ягоды, крем.", 14)),
            C("К чаю",
                P("Печенье песочное", 280, "250 г", "cookies", 3, null, "Состав: мука пшеничная, масло сливочное, сахар, яйцо.", 20),
                P("Пряники", 240, "300 г", null, 2, null, "Состав: мука ржаная, мёд, специи, сахар.", 16),
                P("Сушки", 120, "200 г", null, 2, null, "Состав: мука пшеничная, сахар, мак.", 22),
                P("Зефир", 260, "200 г", null, 2, null, "Состав: яблочное пюре, сахар, яичный белок, агар.", 12)),
        ]);

    /// <summary>The canteen: open on weekdays only, daily menus, one dish sold on Fridays only.</summary>
    public static readonly ShowcaseShopSpec Stolovaya = new(
        Key: "stolovaya", Name: "Столовая «Обед дома»", SlugBase: "stolovaya", City: "Санкт-Петербург", TimeZoneId: "Europe/Moscow",
        Street: "Заводская улица", AssetCategory: "canteen",
        Description: "Домашние обеды по будням: супы, горячее, гарниры и салаты. Меню меняется каждый день.",
        OwnerFirstName: "Светлана", OwnerLastName: "Корнилова", StaffCount: 2, Hours: Weekdays(H(11), H(17)),
        AsapEnabled: true, ScheduledEnabled: false, SlotStepMinutes: 15, PreorderDays: 0, MinPrepMinutes: 10,
        Acceptance: OrderAcceptanceMode.Manual, TrackStock: false, SellerLegalName: "Столовая «Обед дома» (пример)",
        WeekdayOrders: new(20, 35), WeekendOrders: new(0, 0), AsapShare: 1.0,
        Peaks: [new(H(11, 20), H(13, 30), 62), new(H(13, 30), H(16, 30), 38)],
        Categories:
        [
            C("Первые блюда",
                P("Борщ со сметаной", 160, "300 мл", "soup-borsch", 6),
                P("Куриный суп с лапшой", 140, "300 мл", "soup-chicken", 5),
                P("Солянка", 190, "300 мл", null, 3),
                P("Щи из свежей капусты", 130, "300 мл", null, 3),
                P("Гороховый суп", 120, "300 мл", null, 2)),
            C("Вторые блюда",
                P("Котлета куриная", 170, "100 г", "cutlet", 6),
                P("Гуляш говяжий", 240, "120 г", "goulash", 4),
                P("Запечённая курица", 210, "150 г", null, 4),
                P("Тефтели в соусе", 190, "120 г", null, 4),
                P("Печень по-строгановски", 220, "120 г", null, 2),
                P("Плов с курицей", 230, "250 г", null, 4),
                P("Запечённая треска (рыбный день)", 260, "150 г", "fish", 3, "Только по пятницам.", null, null, FridayOnly)),
            C("Гарниры",
                P("Гречка", 70, "150 г", "buckwheat", 5),
                P("Рис", 70, "150 г", "rice", 4),
                P("Картофельное пюре", 85, "150 г", "mashed-potato", 5),
                P("Макароны", 70, "150 г", "pasta", 3)),
            C("Салаты",
                P("Винегрет", 90, "100 г", "salad-vinaigrette", 3),
                P("Оливье", 120, "100 г", null, 4),
                P("Свежие овощи", 100, "120 г", "salad-fresh", 3),
                P("Салат «Мимоза»", 110, "100 г", null, 2),
                P("Квашеная капуста", 70, "80 г", null, 2)),
            C("Напитки",
                P("Компот", 60, "250 мл", "compote", 5),
                P("Морс", 70, "250 мл", null, 3),
                P("Чай", 40, "250 мл", "tea", 4),
                P("Кисель", 60, "250 мл", null, 2)),
            C("Выпечка",
                P("Блины со сметаной", 110, "2 шт", "pancakes", 3),
                P("Пирожок с капустой", 55, "1 шт", null, 3),
                P("Ватрушка", 75, "1 шт", null, 2)),
        ]);

    /// <summary>The flower shop: long preparation, pre-orders a week ahead, a holiday with its own hours.</summary>
    public static readonly ShowcaseShopSpec Cvety = new(
        Key: "cvety", Name: "Цветочная лавка «Ромашка и лён»", SlugBase: "cvety", City: "Казань", TimeZoneId: "Europe/Moscow",
        Street: "Цветочная улица", AssetCategory: "flowers",
        Description: "Букеты и композиции, которые собирают под ваш повод. Заказывайте заранее: флорист соберёт букет к выбранному времени.",
        OwnerFirstName: "Мария", OwnerLastName: "Беляева", StaffCount: 1, Hours: Daily(H(9), H(20)),
        AsapEnabled: false, ScheduledEnabled: true, SlotStepMinutes: 60, PreorderDays: 7, MinPrepMinutes: 120,
        Acceptance: OrderAcceptanceMode.Manual, TrackStock: false, SellerLegalName: "Цветочная лавка «Ромашка и лён» (пример)",
        WeekdayOrders: new(1, 3), WeekendOrders: new(2, 4), AsapShare: 0,
        Peaks: [new(H(10), H(13), 40), new(H(13), H(17), 35), new(H(17), H(19), 25)],
        Categories:
        [
            C("Букеты",
                P("Букет из роз «Нежность»", 3900, "15 роз", "bouquet-roses", 4, "Кремовые и розовые розы с зеленью."),
                P("Букет «Весенний микс»", 2900, "около 35 см", "bouquet-mixed", 5),
                P("Тюльпаны", 2400, "15 шт", "bouquet-tulips", 4),
                P("Пионы", 3200, "5 шт", "bouquet-peonies", 3, "Сезонный товар."),
                P("Букет «Полевой»", 1900, "около 30 см", null, 3),
                P("Монобукет из гербер", 2100, "9 шт", null, 2),
                P("Подсолнухи", 1500, "3 шт", null, 2),
                P("Хризантемы кустовые", 1800, "7 веток", null, 2)),
            C("Композиции",
                P("Композиция в коробке", 3500, "диаметр 20 см", "arrangement-box", 4),
                P("Корзина с цветами", 4200, "диаметр 30 см", null, 2),
                P("Композиция в шляпной коробке", 4800, "диаметр 25 см", null, 2),
                P("Настольный венок", 2600, "диаметр 25 см", null, 1)),
            C("Комнатные растения",
                P("Орхидея фаленопсис", 2100, "горшок 12 см", "orchid", 3),
                P("Суккуленты в горшке", 690, "микс", "succulent", 4),
                P("Фиалка", 450, "горшок 9 см", "plant-pot", 2),
                P("Драцена", 1400, "горшок 17 см", null, 1),
                P("Замиокулькас", 1900, "горшок 19 см", null, 1)),
            C("Дополнения",
                P("Открытка", 150, "1 шт", "card", 5),
                P("Подарочная упаковка", 350, "1 шт", null, 3),
                P("Лента атласная", 120, "1 шт", null, 1)),
        ]);

    /// <summary>The farm shop: weight products, the order is issued by the actual weight.</summary>
    public static readonly ShowcaseShopSpec Fermerskaya = new(
        Key: "fermerskaya", Name: "Фермерская лавка «Речные луга»", SlugBase: "fermerskaya", City: "Новосибирск", TimeZoneId: "Asia/Novosibirsk",
        Street: "Луговая улица", AssetCategory: "farm",
        Description: "Овощи, ягоды, мясо, молочные продукты и мёд от местных фермеров. Вес уточним при выдаче, платите за фактический.",
        OwnerFirstName: "Павел", OwnerLastName: "Лебедев", StaffCount: 1, Hours: Daily(H(9), H(20)),
        AsapEnabled: false, ScheduledEnabled: true, SlotStepMinutes: 30, PreorderDays: 3, MinPrepMinutes: 60,
        Acceptance: OrderAcceptanceMode.Manual, TrackStock: false, SellerLegalName: "Фермерская лавка «Речные луга» (пример)",
        WeekdayOrders: new(3, 7), WeekendOrders: new(4, 8), AsapShare: 0,
        Peaks: [new(H(10), H(13), 35), new(H(13), H(17), 30), new(H(17), H(19, 30), 35)],
        Categories:
        [
            C("Овощи",
                W("Помидоры", 280, 100, 500, "tomatoes", 6, "Грунтовые, с грядки."),
                W("Огурцы", 190, 100, 500, "cucumbers", 5),
                W("Картофель", 65, 500, 1000, "potatoes", 4),
                W("Морковь", 70, 500, 500, "carrots", 3),
                W("Свёкла", 60, 500, 500, null, 2),
                W("Капуста белая", 55, 500, 1000, null, 2),
                W("Лук репчатый", 50, 500, 500, null, 2)),
            C("Фрукты и ягоды",
                W("Яблоки", 140, 200, 1000, "apples", 5),
                W("Груши", 220, 200, 1000, null, 2),
                W("Смородина", 420, 100, 300, "berries", 3),
                W("Малина", 650, 100, 300, "berries", 3)),
            C("Молочные продукты",
                P("Молоко", 120, "1 л", "milk", 6, null, "Молоко цельное пастеризованное."),
                P("Творог", 260, "400 г", null, 4, null, "Состав: молоко, закваска."),
                P("Сметана", 180, "350 мл", null, 3, null, "Состав: сливки, закваска."),
                P("Сыр домашний", 690, "300 г", "cheese", 3, null, "Состав: молоко, соль, закваска."),
                P("Кефир", 95, "1 л", null, 3, null, "Состав: молоко, закваска.")),
            C("Мясо",
                W("Говядина (вырезка)", 1200, 100, 500, "meat-beef", 3),
                W("Свинина (шея)", 650, 100, 500, null, 3),
                W("Курица домашняя (тушка)", 420, 500, 1000, null, 3)),
            C("Мёд, яйца, хлеб",
                P("Мёд цветочный", 550, "500 г", "honey", 4),
                P("Яйца домашние", 140, "10 шт", "eggs", 5),
                P("Хлеб деревенский", 120, "600 г", "bread-farm", 3, null, "Состав: мука пшеничная, закваска, соль.")),
        ]);

    /// <summary>The five shops in the order of ids and phones. Adding a shop at the END never shifts a phone or an id of the earlier ones.</summary>
    public static readonly IReadOnlyList<ShowcaseShopSpec> Shops = [Kofeinya, Pekarnya, Stolovaya, Cvety, Fermerskaya];

    /// <summary>~80 registered customers and ~450 guests in total, split by city.</summary>
    public static readonly IReadOnlyList<ShowcaseCityPool> CityPools =
    [
        new("Москва", 38, 200, ["kofeinya", "pekarnya"]),
        new("Санкт-Петербург", 18, 110, ["stolovaya"]),
        new("Казань", 10, 50, ["cvety"]),
        new("Новосибирск", 14, 90, ["fermerskaya"]),
    ];

    /// <summary>Neutral notes of a shop about a regular customer. No health data, no "allergy": a unit test checks the dictionary.</summary>
    public static readonly IReadOnlyList<string> CustomerNotes =
    [
        "Просит без сахара", "Обычно забирает после 18:00", "Любит, когда напиток погорячее", "Заказывает на всю команду",
        "Предпочитает оплату на месте", "Забирает у входа", "Часто берёт то же самое", "Просит положить салфетки",
    ];

    /// <summary>Neutral short comments of customers to an order (a tenth of orders). No health data.</summary>
    public static readonly IReadOnlyList<string> OrderComments =
    [
        "Без сахара, пожалуйста", "Заберу у входа", "Подогрейте, пожалуйста", "Положите салфетки", "Я буду чуть раньше",
        "Упакуйте отдельно", "Спасибо!", "Позвоните, если что-то закончится",
    ];

    public static readonly IReadOnlyList<string> RejectReasons =
    [
        "Сейчас не успеваем, извините", "Закончился товар", "Не можем приготовить к этому времени",
    ];

    public static readonly IReadOnlyList<string> ShopCancelReasons =
    [
        "Закончился товар", "Не смогли приготовить к этому времени", "Технический сбой на кухне",
    ];

    public static readonly IReadOnlyList<string> EditComments =
    [
        "Заменили по вашей просьбе", "Не хватило товара — собрали меньше", "Добавили по согласованию",
    ];
}
