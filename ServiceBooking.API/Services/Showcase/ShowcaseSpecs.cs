namespace ServiceBooking.API.Services.Showcase;

/// <summary>One service of a showcase company. <see cref="Image"/> is the asset key (§575.6); an image missing from the manifest is simply skipped.</summary>
public sealed record ShowcaseServiceSpec(string Name, string Description, int Price, int Minutes, string Image);

/// <summary>
/// One showcase company (§575.3). <see cref="Key"/> is stable and goes into ids; <see cref="SlugBase"/> is appended to <c>primer-</c>.
/// <see cref="Profile"/> is the business profile the "5 open of 8" rule of D-1 counts by: the network of two points is ONE profile, open in both points.
/// </summary>
public sealed record ShowcaseCompanySpec(
    string Key, string Profile, string Name, string SlugBase, string City, string TimeZoneId, string Street, string Description,
    bool BookingOpen, string OwnerKey, string OwnerFirstName, string OwnerLastName, int MasterCount, string Category,
    IReadOnlyList<ShowcaseServiceSpec> Services, bool OwnerProvidesServices = false);

/// <summary>
/// The data of the showcase: text, prices, cities. Pure data in code (ARCHITECTURE_CYCLE28.md §575.2): no dates, no randomness — the same on every
/// machine. Company and person names are fictional and contain no words like "test", "demo" or "example" (§575.3); no real brands, no real addresses
/// (streets only, no house numbers). The phone and e-mail of a company are deliberately empty (§574.4).
/// </summary>
public static class ShowcaseSpecs
{
    public static readonly IReadOnlyList<string> Cities = ["Москва", "Санкт-Петербург", "Новосибирск", "Екатеринбург", "Казань"];

    private static ShowcaseServiceSpec S(string name, string description, int price, int minutes, string image) => new(name, description, price, minutes, image);

    private static readonly ShowcaseServiceSpec[] Beauty =
    [
        S("Женская стрижка", "Мытьё головы, стрижка и лёгкая укладка.", 2500, 60, "service.haircut"),
        S("Мужская стрижка", "Стрижка ножницами или машинкой, мытьё головы.", 1500, 45, "service.haircut"),
        S("Окрашивание в один тон", "Окрашивание корней или по всей длине, уход после процедуры.", 4500, 120, "service.coloring"),
        S("Сложное окрашивание", "Мелирование, airtouch, балаяж — подбор оттенка с мастером.", 9000, 240, "service.coloring"),
        S("Укладка", "Вечерняя или повседневная укладка.", 1800, 45, "service.styling"),
        S("Маникюр классический", "Обработка кутикулы и придание формы.", 1600, 60, "service.manicure"),
        S("Маникюр с покрытием", "Маникюр и покрытие гель-лаком.", 2400, 90, "service.manicure"),
        S("Педикюр", "Аппаратный педикюр с уходом.", 2600, 90, "service.pedicure"),
        S("Восстановление волос", "Глубокое восстановление с профессиональной косметикой.", 5000, 120, "service.hair-care"),
        S("Вечерний макияж", "Макияж для праздника или фотосессии.", 3000, 60, "service.makeup"),
    ];

    private static readonly ShowcaseServiceSpec[] Barber =
    [
        S("Мужская стрижка", "Классическая стрижка с мытьём головы и укладкой.", 2000, 60, "service.haircut"),
        S("Стрижка машинкой", "Быстрая стрижка под одну или две насадки.", 1200, 30, "service.haircut"),
        S("Моделирование бороды", "Придание формы бороде и усам, уход за кожей.", 1500, 45, "service.beard"),
        S("Королевское бритьё", "Бритьё опасной бритвой с горячим полотенцем.", 2200, 60, "service.shave"),
        S("Стрижка и борода", "Комплекс: стрижка и моделирование бороды.", 3200, 90, "service.beard"),
        S("Детская стрижка", "Стрижка для мальчиков до 12 лет.", 1300, 45, "service.haircut"),
        S("Камуфляж седины", "Аккуратное тонирование бороды или волос.", 1800, 45, "service.coloring"),
        S("Укладка", "Укладка с использованием стайлинга.", 700, 30, "service.styling"),
    ];

    private static readonly ShowcaseServiceSpec[] Nails =
    [
        S("Маникюр классический", "Обработка кутикулы и придание формы.", 1500, 60, "service.manicure"),
        S("Маникюр с гель-лаком", "Маникюр и покрытие гель-лаком в один цвет.", 2300, 90, "service.manicure"),
        S("Снятие покрытия", "Аккуратное снятие гель-лака или геля.", 500, 30, "service.manicure"),
        S("Укрепление гелем", "Укрепление натуральной ногтевой пластины.", 2800, 120, "service.nails-design"),
        S("Наращивание ногтей", "Наращивание на формы, покрытие и дизайн.", 3600, 150, "service.nails-design"),
        S("Дизайн ногтей", "Художественный дизайн одного или нескольких ногтей.", 300, 30, "service.nails-design"),
        S("Педикюр классический", "Аппаратный педикюр с обработкой стоп.", 2400, 90, "service.pedicure"),
        S("Педикюр с покрытием", "Педикюр и покрытие гель-лаком.", 3000, 120, "service.pedicure"),
        S("Уход для рук", "Скраб, маска и массаж рук.", 900, 30, "service.spa"),
        S("Мужской маникюр", "Аккуратный маникюр без покрытия.", 1400, 60, "service.manicure"),
    ];

    private static readonly ShowcaseServiceSpec[] Massage =
    [
        S("Массаж спины", "Классический массаж спины и воротниковой зоны.", 2500, 60, "service.massage"),
        S("Общий массаж", "Массаж всего тела, расслабляющая программа.", 4500, 90, "service.massage"),
        S("Антицеллюлитный массаж", "Интенсивная работа с проблемными зонами.", 3500, 60, "service.massage"),
        S("Лимфодренажный массаж", "Мягкая техника для снятия отёков.", 3800, 60, "service.massage"),
        S("Массаж лица", "Расслабляющий массаж лица и шеи.", 2200, 45, "service.facial"),
        S("Стоун-терапия", "Массаж тёплыми камнями.", 4800, 90, "service.spa"),
        S("Обёртывание", "Обёртывание с водорослями или шоколадом.", 3200, 60, "service.wrap"),
        S("SPA-программа для двоих", "Массаж, чай и время для отдыха вдвоём.", 9500, 150, "service.spa"),
    ];

    private static readonly ShowcaseServiceSpec[] Cosmetology =
    [
        S("Ультразвуковая чистка лица", "Мягкая чистка без травматичного воздействия.", 3500, 60, "service.facial"),
        S("Комбинированная чистка", "Аппаратная и мануальная чистка кожи.", 4500, 90, "service.facial"),
        S("Пилинг", "Поверхностный пилинг по типу кожи.", 3800, 45, "service.peeling"),
        S("Мезотерапия", "Курс питательных коктейлей для кожи лица.", 6500, 60, "service.mesotherapy"),
        S("Биоревитализация", "Инъекционное увлажнение кожи.", 9000, 60, "service.mesotherapy"),
        S("Массаж лица", "Скульптурный массаж лица.", 2500, 45, "service.facial"),
        S("Уход по типу кожи", "Программа ухода с подбором косметики.", 3200, 90, "service.facial"),
        S("Консультация косметолога", "Оценка состояния кожи и план ухода.", 800, 30, "service.consult"),
    ];

    private static readonly ShowcaseServiceSpec[] BrowsLashes =
    [
        S("Коррекция бровей", "Форма по типу лица, пинцет и воск.", 900, 30, "service.brows"),
        S("Окрашивание бровей", "Краска или хна на выбор.", 1100, 30, "service.brows"),
        S("Архитектура бровей", "Коррекция и окрашивание, подбор формы.", 1800, 45, "service.brows"),
        S("Ламинирование бровей", "Укладка и фиксация формы на несколько недель.", 2600, 60, "service.brows"),
        S("Ламинирование ресниц", "Изгиб, объём и питание натуральных ресниц.", 3000, 90, "service.lashes"),
        S("Наращивание ресниц, классика", "Одна искусственная ресница на одну натуральную.", 3200, 120, "service.lashes"),
        S("Наращивание ресниц, объём 2D–3D", "Пучковое наращивание для выразительного взгляда.", 3800, 150, "service.lashes"),
        S("Снятие ресниц", "Бережное снятие наращённых ресниц.", 600, 30, "service.lashes"),
        S("Коррекция наращивания", "Обновление через 2–3 недели.", 2400, 90, "service.lashes"),
    ];

    private static readonly ShowcaseServiceSpec[] HomeMaster =
    [
        S("Маникюр", "Аккуратный маникюр без покрытия.", 1400, 60, "service.manicure"),
        S("Маникюр с гель-лаком", "Маникюр и покрытие гель-лаком.", 2100, 90, "service.manicure"),
        S("Педикюр", "Педикюр с обработкой стоп.", 2200, 90, "service.pedicure"),
        S("Наращивание ногтей", "Наращивание, коррекция формы.", 3200, 150, "service.nails-design"),
        S("Дизайн ногтей", "Рисунок, страз или фольга.", 700, 30, "service.nails-design"),
        S("Снятие покрытия", "Снятие гель-лака или геля.", 400, 30, "service.manicure"),
    ];

    private static ShowcaseServiceSpec[] Network(double factor) => Beauty.Take(8)
        .Select(s => s with { Price = (int)(Math.Round(s.Price * factor / 50.0) * 50) }).ToArray();

    public static readonly IReadOnlyList<ShowcaseCompanySpec> Companies =
    [
        new("lavanda", "salon", "Салон красоты «Лаванда»", "lavanda", "Москва", "Europe/Moscow", "Тверская улица",
            "Просторный салон красоты в центре города: парикмахеры, ногтевой сервис и макияж под одной крышей. Работаем без спешки и подбираем уход под каждого гостя. Принимаем по записи, ждать в очереди не придётся.",
            BookingOpen: true, "lavanda-owner", "Елена", "Воронцова", MasterCount: 6, "beauty", Beauty),
        new("borodach", "barber", "Барбершоп «Бородач»", "borodach", "Новосибирск", "Asia/Novosibirsk", "Красный проспект",
            "Мужской барбершоп: классические и современные стрижки, оформление бороды, бритьё опасной бритвой. Атмосфера без суеты и хороший кофе для гостей.",
            BookingOpen: true, "borodach-owner", "Артём", "Лаптев", MasterCount: 5, "barber", Barber),
        new("zhemchug", "nails", "Ногтевая студия «Жемчуг»", "zhemchug", "Санкт-Петербург", "Europe/Moscow", "Садовая улица",
            "Студия маникюра и педикюра. Стерильные инструменты, аккуратная работа и дизайн на любой вкус. Свободные окна видно в календаре заранее.",
            BookingOpen: true, "zhemchug-owner", "Алёна", "Кравченко", MasterCount: 4, "nails", Nails),
        new("tihaya-gavan", "massage", "Массаж и SPA «Тихая гавань»", "tihaya-gavan", "Екатеринбург", "Asia/Yekaterinburg", "улица Малышева",
            "Пространство для отдыха: классический и лечебный массаж, обёртывания и SPA-программы. Приглушённый свет, тишина и тёплые полотенца.",
            BookingOpen: false, "tihaya-gavan-owner", "Ирина", "Мельникова", MasterCount: 3, "massage", Massage),
        new("chistaya-kozha", "cosmetology", "Косметология «Чистая кожа»", "chistaya-kozha", "Казань", "Europe/Moscow", "улица Баумана",
            "Кабинет косметолога: чистки, пилинги и уходовые процедуры. Перед каждой программой — консультация и подбор ухода по типу кожи.",
            BookingOpen: false, "chistaya-kozha-owner", "Наталья", "Гусева", MasterCount: 3, "cosmetology", Cosmetology),
        new("vzglyad", "brows", "Студия бровей и ресниц «Взгляд»", "vzglyad", "Москва", "Europe/Moscow", "Арбат",
            "Всё для выразительного взгляда: брови, ламинирование, наращивание ресниц. Мастера с большим опытом и аккуратной техникой.",
            BookingOpen: true, "vzglyad-owner", "Марина", "Соболева", MasterCount: 3, "brows", BrowsLashes),
        new("irina-manikyur", "home", "Ирина Соколова — маникюр и педикюр", "irina-manikyur", "Казань", "Europe/Moscow", "улица Кремлёвская",
            "Частный мастер маникюра и педикюра. Принимаю дома в уютной студии, по предварительной записи. Работаю только стерильными инструментами.",
            BookingOpen: false, "irina-manikyur-owner", "Ирина", "Соколова", MasterCount: 1, "home", HomeMaster, OwnerProvidesServices: true),
        new("myata-moskva", "network", "Сеть салонов «Мята» — Москва", "myata-moskva", "Москва", "Europe/Moscow", "Ленинградский проспект",
            "Сеть городских салонов красоты «Мята». Стрижки, окрашивание, маникюр и педикюр по единым стандартам и понятным ценам. Записывайтесь в любую точку сети.",
            BookingOpen: true, "myata-owner", "Дмитрий", "Ларин", MasterCount: 3, "beauty", Network(1.0)),
        new("myata-spb", "network", "Сеть салонов «Мята» — Санкт-Петербург", "myata-spb", "Санкт-Петербург", "Europe/Moscow", "Невский проспект",
            "Сеть городских салонов красоты «Мята». Стрижки, окрашивание, маникюр и педикюр по единым стандартам и понятным ценам. Записывайтесь в любую точку сети.",
            BookingOpen: true, "myata-owner", "Дмитрий", "Ларин", MasterCount: 3, "beauty", Network(0.95)),
    ];

    public static readonly IReadOnlyList<string> FemaleNames =
    [
        "Анна", "Мария", "Екатерина", "Ольга", "Наталья", "Татьяна", "Юлия", "Елена", "Светлана", "Ирина", "Виктория", "Дарья", "Ксения", "Алина",
        "Полина", "Анастасия", "Валерия", "Вероника", "Кристина", "Диана", "Алёна", "Софья", "Ева", "Лилия", "Яна", "Маргарита", "Оксана", "Людмила",
    ];

    public static readonly IReadOnlyList<string> MaleNames =
    [
        "Александр", "Дмитрий", "Максим", "Сергей", "Андрей", "Алексей", "Артём", "Илья", "Кирилл", "Михаил", "Никита", "Егор", "Роман", "Павел",
        "Владимир", "Денис", "Антон", "Игорь", "Олег", "Тимур", "Руслан", "Станислав", "Григорий", "Вадим", "Евгений", "Ярослав", "Лев", "Матвей",
    ];

    // Surnames come as (masculine, feminine) pairs.
    public static readonly IReadOnlyList<(string Male, string Female)> Surnames =
    [
        ("Иванов", "Иванова"), ("Смирнов", "Смирнова"), ("Кузнецов", "Кузнецова"), ("Попов", "Попова"), ("Васильев", "Васильева"),
        ("Петров", "Петрова"), ("Соколов", "Соколова"), ("Михайлов", "Михайлова"), ("Новиков", "Новикова"), ("Фёдоров", "Фёдорова"),
        ("Морозов", "Морозова"), ("Волков", "Волкова"), ("Алексеев", "Алексеева"), ("Лебедев", "Лебедева"), ("Семёнов", "Семёнова"),
        ("Егоров", "Егорова"), ("Павлов", "Павлова"), ("Козлов", "Козлова"), ("Степанов", "Степанова"), ("Николаев", "Николаева"),
        ("Орлов", "Орлова"), ("Андреев", "Андреева"), ("Макаров", "Макарова"), ("Никитин", "Никитина"), ("Захаров", "Захарова"),
        ("Зайцев", "Зайцева"), ("Соловьёв", "Соловьёва"), ("Борисов", "Борисова"), ("Яковлев", "Яковлева"), ("Григорьев", "Григорьева"),
        ("Романов", "Романова"), ("Воробьёв", "Воробьёва"), ("Сергеев", "Сергеева"), ("Кузьмин", "Кузьмина"), ("Фролов", "Фролова"),
        ("Александров", "Александрова"), ("Дмитриев", "Дмитриева"), ("Королёв", "Королёва"), ("Гусев", "Гусева"), ("Киселёв", "Киселёва"),
    ];

    public static readonly IReadOnlyList<string> CancellationReasons =
    [
        "Изменились планы", "Заболел(а)", "Не успеваю приехать", "Перенесу на другую дату", "Уезжаю в командировку", "Нашёл(ла) более удобное время",
    ];

    /// <summary>Short master biography by business category; <c>{0}</c> is the years of experience.</summary>
    public static string Bio(string category, int years) => category switch
    {
        "beauty" or "network" => $"Стаж {years} {Years(years)}. Работаю с любой длиной и типом волос, помогу подобрать образ.",
        "barber" => $"Стаж {years} {Years(years)}. Классические и современные мужские стрижки, аккуратное оформление бороды.",
        "nails" or "home" => $"Стаж {years} {Years(years)}. Аккуратный маникюр и педикюр, проверенные материалы и стерильные инструменты.",
        "massage" => $"Стаж {years} {Years(years)}. Классический и расслабляющий массаж, подбираю технику под запрос.",
        "cosmetology" => $"Стаж {years} {Years(years)}. Уходовые процедуры и чистки, подбор программы по типу кожи.",
        "brows" => $"Стаж {years} {Years(years)}. Брови и ресницы: естественный результат и бережная работа.",
        _ => $"Стаж {years} {Years(years)}.",
    };

    private static string Years(int n) => (n % 100 is >= 11 and <= 14) ? "лет" : (n % 10) switch { 1 => "год", 2 or 3 or 4 => "года", _ => "лет" };
}
