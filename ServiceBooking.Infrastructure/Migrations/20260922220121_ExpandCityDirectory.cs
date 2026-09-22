using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <summary>
    /// US-113 (ARCHITECTURE_CYCLE9.md §103.3). Pads the ~91-row city directory seeded by
    /// AddNotificationChannels/SeedCities up to a real cross-Russia list, sourced (composition and
    /// region assignment) the way §103.3 requires future pop-ups of this table to be sourced: cities
    /// with population &gt;= 50,000 plus every remaining administrative centre not already present,
    /// IANA time zone assigned per the same zone.tab/zone1970.tab mapping the original 91 rows used,
    /// with the region-by-region deviations from "same zone as Moscow" spelled out below because a
    /// previous cycle's migration already got exactly this wrong once (Барнаул vs Asia/Novosibirsk,
    /// called out in AddNotificationChannels' own comment).
    ///
    /// EXACT COUNT: 210 new rows, 91 existing + 210 = 301 total (target from §103.3: 310 +/- 10).
    ///
    /// Deliberate zone choices for regions that are NOT simply "Europe/Moscow" (spelled out because the
    /// wrong, more obvious choice is one hour off and won't be caught by any test short of a person who
    /// already knows the region):
    ///   - Волгоградская область -&gt; Europe/Volgograd (NOT Europe/Moscow, despite the pre-existing
    ///     "Волгоград" row from cycle 4 having been seeded as Europe/Moscow, since corrected upstream
    ///     tzdata data was not available/used at seed time; that pre-existing row is untouched per rule
    ///     3 below — this migration does not retroactively fix it, only gets new rows right).
    ///   - Саратовская область -&gt; Europe/Saratov (same caveat as Волгоград above for the pre-existing
    ///     "Саратов" row).
    ///   - Ульяновская область -&gt; Europe/Ulyanovsk (same caveat for the pre-existing "Ульяновск" row).
    ///   - Астраханская область -&gt; Europe/Astrakhan (no new rows added for this region in this
    ///     migration, but the zone is recorded here for the next person who adds one).
    ///   - Алтайский край -&gt; Asia/Barnaul, Кемеровская область -&gt; Asia/Novokuznetsk,
    ///     Томская область -&gt; Asia/Tomsk, Новосибирская область -&gt; Asia/Novosibirsk — all four
    ///     already used correctly by the pre-existing 91 rows; repeated here only for new rows in the
    ///     same regions, using the SAME zone id as the existing sibling rows (e.g. "Стерлитамак" gets
    ///     Asia/Yekaterinburg, the same zone "Уфа" already uses).
    ///
    /// Mechanics (§103.3 points 1-5):
    ///   1. <see cref="Rows"/> is the public, testable array — read directly by
    ///      ServiceBooking.UnitTests/CityDirectoryDataTests.cs, no DB required.
    ///   2. SearchName is NOT hand-typed: <see cref="NormalizeForSearch"/> below duplicates
    ///      ServiceBooking.API.Services.CitySearch.Normalize's exact rule (Infrastructure cannot
    ///      reference the API project, so — same pattern as CitySearch's own doc comment describes for
    ///      PhoneNormalizer/NormalizePhoneNumbers — the rule is duplicated by hand here and cross-checked
    ///      by CityDirectoryDataTests, not re-invented).
    ///   3. The INSERT is idempotent per (Name, Region): a correlated NOT EXISTS subquery, not
    ///      migrationBuilder.InsertData (which has no "skip if already there" mode). Existing rows —
    ///      their Id, SearchName, TimeZoneId — are never touched: Company.CityId references Id, and nothing
    ///      here can renumber or overwrite an existing row.
    ///   4. Deduplication is unconditional and re-runnable, not "only if we know there's a duplicate right
    ///      now": DELETE keeps the row with the SMALLEST Id per (Name, Region) — existing companies may
    ///      already reference that lower Id — then the unique index on (Name, Region) makes "no
    ///      duplicates" a schema property going forward (same two-step shape as cycle 1's
    ///      DeduplicateWorkingHours -&gt; AddWorkingHoursUniqueIndex, done here as one migration instead of
    ///      two files since both steps touch the same table in the same direction).
    ///   5. Down removes only what this migration could have added: the exact (Name, Region) pairs from
    ///      <see cref="Rows"/>, then the unique index. It does not attempt to undo step 4's dedup DELETE
    ///      (not reconstructable — same accepted trade-off DeduplicateWorkingHours' own Down documents).
    ///      If a company was assigned to one of these new cities, Down stops with the existing
    ///      FK_Companies_Cities_CityId (Restrict) error rather than silently orphaning it.
    /// </summary>
    public partial class ExpandCityDirectory : Migration
    {
        /// <summary>
        /// (Name, Region, TimeZoneId) for every row this migration may insert. Public and static so
        /// ServiceBooking.UnitTests/CityDirectoryDataTests.cs can assert on it directly without touching a
        /// database — SearchName is deliberately NOT part of this tuple (computed, not stored twice).
        /// </summary>
        public static readonly (string Name, string Region, string TimeZoneId)[] Rows =
        {
            ("Балашиха", "Московская область", "Europe/Moscow"),
            ("Химки", "Московская область", "Europe/Moscow"),
            ("Подольск", "Московская область", "Europe/Moscow"),
            ("Королёв", "Московская область", "Europe/Moscow"),
            ("Мытищи", "Московская область", "Europe/Moscow"),
            ("Люберцы", "Московская область", "Europe/Moscow"),
            ("Электросталь", "Московская область", "Europe/Moscow"),
            ("Красногорск", "Московская область", "Europe/Moscow"),
            ("Коломна", "Московская область", "Europe/Moscow"),
            ("Одинцово", "Московская область", "Europe/Moscow"),
            ("Серпухов", "Московская область", "Europe/Moscow"),
            ("Орехово-Зуево", "Московская область", "Europe/Moscow"),
            ("Домодедово", "Московская область", "Europe/Moscow"),
            ("Жуковский", "Московская область", "Europe/Moscow"),
            ("Пушкино", "Московская область", "Europe/Moscow"),
            ("Щёлково", "Московская область", "Europe/Moscow"),
            ("Раменское", "Московская область", "Europe/Moscow"),
            ("Долгопрудный", "Московская область", "Europe/Moscow"),
            ("Реутов", "Московская область", "Europe/Moscow"),
            ("Сергиев Посад", "Московская область", "Europe/Moscow"),
            ("Ногинск", "Московская область", "Europe/Moscow"),
            ("Клин", "Московская область", "Europe/Moscow"),
            ("Воскресенск", "Московская область", "Europe/Moscow"),
            ("Ивантеевка", "Московская область", "Europe/Moscow"),
            ("Дмитров", "Московская область", "Europe/Moscow"),
            ("Чехов", "Московская область", "Europe/Moscow"),
            ("Наро-Фоминск", "Московская область", "Europe/Moscow"),
            ("Ступино", "Московская область", "Europe/Moscow"),
            ("Лобня", "Московская область", "Europe/Moscow"),
            ("Фрязино", "Московская область", "Europe/Moscow"),
            ("Видное", "Московская область", "Europe/Moscow"),
            ("Егорьевск", "Московская область", "Europe/Moscow"),
            ("Павловский Посад", "Московская область", "Europe/Moscow"),
            ("Гатчина", "Ленинградская область", "Europe/Moscow"),
            ("Выборг", "Ленинградская область", "Europe/Moscow"),
            ("Сосновый Бор", "Ленинградская область", "Europe/Moscow"),
            ("Тихвин", "Ленинградская область", "Europe/Moscow"),
            ("Кириши", "Ленинградская область", "Europe/Moscow"),
            ("Всеволожск", "Ленинградская область", "Europe/Moscow"),
            ("Кингисепп", "Ленинградская область", "Europe/Moscow"),
            ("Волхов", "Ленинградская область", "Europe/Moscow"),
            ("Россошь", "Воронежская область", "Europe/Moscow"),
            ("Борисоглебск", "Воронежская область", "Europe/Moscow"),
            ("Новороссийск", "Краснодарский край", "Europe/Moscow"),
            ("Армавир", "Краснодарский край", "Europe/Moscow"),
            ("Ейск", "Краснодарский край", "Europe/Moscow"),
            ("Анапа", "Краснодарский край", "Europe/Moscow"),
            ("Туапсе", "Краснодарский край", "Europe/Moscow"),
            ("Кропоткин", "Краснодарский край", "Europe/Moscow"),
            ("Славянск-на-Кубани", "Краснодарский край", "Europe/Moscow"),
            ("Тимашёвск", "Краснодарский край", "Europe/Moscow"),
            ("Геленджик", "Краснодарский край", "Europe/Moscow"),
            ("Лабинск", "Краснодарский край", "Europe/Moscow"),
            ("Крымск", "Краснодарский край", "Europe/Moscow"),
            ("Таганрог", "Ростовская область", "Europe/Moscow"),
            ("Шахты", "Ростовская область", "Europe/Moscow"),
            ("Новочеркасск", "Ростовская область", "Europe/Moscow"),
            ("Волгодонск", "Ростовская область", "Europe/Moscow"),
            ("Новошахтинск", "Ростовская область", "Europe/Moscow"),
            ("Батайск", "Ростовская область", "Europe/Moscow"),
            ("Каменск-Шахтинский", "Ростовская область", "Europe/Moscow"),
            ("Азов", "Ростовская область", "Europe/Moscow"),
            ("Гуково", "Ростовская область", "Europe/Moscow"),
            ("Сальск", "Ростовская область", "Europe/Moscow"),
            ("Дзержинск", "Нижегородская область", "Europe/Moscow"),
            ("Арзамас", "Нижегородская область", "Europe/Moscow"),
            ("Саров", "Нижегородская область", "Europe/Moscow"),
            ("Выкса", "Нижегородская область", "Europe/Moscow"),
            ("Бор", "Нижегородская область", "Europe/Moscow"),
            ("Павлово", "Нижегородская область", "Europe/Moscow"),
            ("Кстово", "Нижегородская область", "Europe/Moscow"),
            ("Нижнекамск", "Республика Татарстан", "Europe/Moscow"),
            ("Альметьевск", "Республика Татарстан", "Europe/Moscow"),
            ("Зеленодольск", "Республика Татарстан", "Europe/Moscow"),
            ("Бугульма", "Республика Татарстан", "Europe/Moscow"),
            ("Елабуга", "Республика Татарстан", "Europe/Moscow"),
            ("Чистополь", "Республика Татарстан", "Europe/Moscow"),
            ("Лениногорск", "Республика Татарстан", "Europe/Moscow"),
            ("Волжский", "Волгоградская область", "Europe/Volgograd"),
            ("Камышин", "Волгоградская область", "Europe/Volgograd"),
            ("Михайловка", "Волгоградская область", "Europe/Volgograd"),
            ("Рыбинск", "Ярославская область", "Europe/Moscow"),
            ("Ржев", "Тверская область", "Europe/Moscow"),
            ("Кинешма", "Ивановская область", "Europe/Moscow"),
            ("Шуя", "Ивановская область", "Europe/Moscow"),
            ("Елец", "Липецкая область", "Europe/Moscow"),
            ("Новомосковск", "Тульская область", "Europe/Moscow"),
            ("Донской", "Тульская область", "Europe/Moscow"),
            ("Алексин", "Тульская область", "Europe/Moscow"),
            ("Железногорск", "Курская область", "Europe/Moscow"),
            ("Старый Оскол", "Белгородская область", "Europe/Moscow"),
            ("Губкин", "Белгородская область", "Europe/Moscow"),
            ("Вязьма", "Смоленская область", "Europe/Moscow"),
            ("Клинцы", "Брянская область", "Europe/Moscow"),
            ("Мичуринск", "Тамбовская область", "Europe/Moscow"),
            ("Кузнецк", "Пензенская область", "Europe/Moscow"),
            ("Энгельс", "Саратовская область", "Europe/Saratov"),
            ("Балаково", "Саратовская область", "Europe/Saratov"),
            ("Балашов", "Саратовская область", "Europe/Saratov"),
            ("Пятигорск", "Ставропольский край", "Europe/Moscow"),
            ("Кисловодск", "Ставропольский край", "Europe/Moscow"),
            ("Невинномысск", "Ставропольский край", "Europe/Moscow"),
            ("Ессентуки", "Ставропольский край", "Europe/Moscow"),
            ("Минеральные Воды", "Ставропольский край", "Europe/Moscow"),
            ("Георгиевск", "Ставропольский край", "Europe/Moscow"),
            ("Железноводск", "Ставропольский край", "Europe/Moscow"),
            ("Будённовск", "Ставропольский край", "Europe/Moscow"),
            ("Ковров", "Владимирская область", "Europe/Moscow"),
            ("Муром", "Владимирская область", "Europe/Moscow"),
            ("Гусь-Хрустальный", "Владимирская область", "Europe/Moscow"),
            ("Александров", "Владимирская область", "Europe/Moscow"),
            ("Обнинск", "Калужская область", "Europe/Moscow"),
            ("Апатиты", "Мурманская область", "Europe/Moscow"),
            ("Североморск", "Мурманская область", "Europe/Moscow"),
            ("Северодвинск", "Архангельская область", "Europe/Moscow"),
            ("Котлас", "Архангельская область", "Europe/Moscow"),
            ("Ухта", "Республика Коми", "Europe/Moscow"),
            ("Воркута", "Республика Коми", "Europe/Moscow"),
            ("Волжск", "Республика Марий Эл", "Europe/Moscow"),
            ("Новочебоксарск", "Чувашская Республика", "Europe/Moscow"),
            ("Димитровград", "Ульяновская область", "Europe/Ulyanovsk"),
            ("Кирово-Чепецк", "Кировская область", "Europe/Moscow"),
            ("Великие Луки", "Псковская область", "Europe/Moscow"),
            ("Прохладный", "Кабардино-Балкарская Республика", "Europe/Moscow"),
            ("Гудермес", "Чеченская Республика", "Europe/Moscow"),
            ("Хасавюрт", "Республика Дагестан", "Europe/Moscow"),
            ("Дербент", "Республика Дагестан", "Europe/Moscow"),
            ("Каспийск", "Республика Дагестан", "Europe/Moscow"),
            ("Буйнакск", "Республика Дагестан", "Europe/Moscow"),
            ("Кизляр", "Республика Дагестан", "Europe/Moscow"),
            ("Назрань", "Республика Ингушетия", "Europe/Moscow"),
            ("Керчь", "Республика Крым", "Europe/Simferopol"),
            ("Евпатория", "Республика Крым", "Europe/Simferopol"),
            ("Феодосия", "Республика Крым", "Europe/Simferopol"),
            ("Ялта", "Республика Крым", "Europe/Simferopol"),
            ("Сызрань", "Самарская область", "Europe/Samara"),
            ("Новокуйбышевск", "Самарская область", "Europe/Samara"),
            ("Чапаевск", "Самарская область", "Europe/Samara"),
            ("Сарапул", "Удмуртская Республика", "Europe/Samara"),
            ("Воткинск", "Удмуртская Республика", "Europe/Samara"),
            ("Каменск-Уральский", "Свердловская область", "Asia/Yekaterinburg"),
            ("Первоуральск", "Свердловская область", "Asia/Yekaterinburg"),
            ("Серов", "Свердловская область", "Asia/Yekaterinburg"),
            ("Новоуральск", "Свердловская область", "Asia/Yekaterinburg"),
            ("Асбест", "Свердловская область", "Asia/Yekaterinburg"),
            ("Ревда", "Свердловская область", "Asia/Yekaterinburg"),
            ("Полевской", "Свердловская область", "Asia/Yekaterinburg"),
            ("Краснотурьинск", "Свердловская область", "Asia/Yekaterinburg"),
            ("Верхняя Пышма", "Свердловская область", "Asia/Yekaterinburg"),
            ("Златоуст", "Челябинская область", "Asia/Yekaterinburg"),
            ("Миасс", "Челябинская область", "Asia/Yekaterinburg"),
            ("Копейск", "Челябинская область", "Asia/Yekaterinburg"),
            ("Троицк", "Челябинская область", "Asia/Yekaterinburg"),
            ("Озёрск", "Челябинская область", "Asia/Yekaterinburg"),
            ("Снежинск", "Челябинская область", "Asia/Yekaterinburg"),
            ("Стерлитамак", "Республика Башкортостан", "Asia/Yekaterinburg"),
            ("Салават", "Республика Башкортостан", "Asia/Yekaterinburg"),
            ("Нефтекамск", "Республика Башкортостан", "Asia/Yekaterinburg"),
            ("Октябрьский", "Республика Башкортостан", "Asia/Yekaterinburg"),
            ("Ишимбай", "Республика Башкортостан", "Asia/Yekaterinburg"),
            ("Туймазы", "Республика Башкортостан", "Asia/Yekaterinburg"),
            ("Кумертау", "Республика Башкортостан", "Asia/Yekaterinburg"),
            ("Березники", "Пермский край", "Asia/Yekaterinburg"),
            ("Соликамск", "Пермский край", "Asia/Yekaterinburg"),
            ("Чайковский", "Пермский край", "Asia/Yekaterinburg"),
            ("Кунгур", "Пермский край", "Asia/Yekaterinburg"),
            ("Лысьва", "Пермский край", "Asia/Yekaterinburg"),
            ("Орск", "Оренбургская область", "Asia/Yekaterinburg"),
            ("Новотроицк", "Оренбургская область", "Asia/Yekaterinburg"),
            ("Бузулук", "Оренбургская область", "Asia/Yekaterinburg"),
            ("Тобольск", "Тюменская область", "Asia/Yekaterinburg"),
            ("Ишим", "Тюменская область", "Asia/Yekaterinburg"),
            ("Шадринск", "Курганская область", "Asia/Yekaterinburg"),
            ("Нижневартовск", "Ханты-Мансийский автономный округ", "Asia/Yekaterinburg"),
            ("Нефтеюганск", "Ханты-Мансийский автономный округ", "Asia/Yekaterinburg"),
            ("Нягань", "Ханты-Мансийский автономный округ", "Asia/Yekaterinburg"),
            ("Когалым", "Ханты-Мансийский автономный округ", "Asia/Yekaterinburg"),
            ("Новый Уренгой", "Ямало-Ненецкий автономный округ", "Asia/Yekaterinburg"),
            ("Ноябрьск", "Ямало-Ненецкий автономный округ", "Asia/Yekaterinburg"),
            ("Бердск", "Новосибирская область", "Asia/Novosibirsk"),
            ("Искитим", "Новосибирская область", "Asia/Novosibirsk"),
            ("Бийск", "Алтайский край", "Asia/Barnaul"),
            ("Рубцовск", "Алтайский край", "Asia/Barnaul"),
            ("Новоалтайск", "Алтайский край", "Asia/Barnaul"),
            ("Северск", "Томская область", "Asia/Tomsk"),
            ("Прокопьевск", "Кемеровская область", "Asia/Novokuznetsk"),
            ("Ленинск-Кузнецкий", "Кемеровская область", "Asia/Novokuznetsk"),
            ("Киселёвск", "Кемеровская область", "Asia/Novokuznetsk"),
            ("Междуреченск", "Кемеровская область", "Asia/Novokuznetsk"),
            ("Белово", "Кемеровская область", "Asia/Novokuznetsk"),
            ("Анжеро-Судженск", "Кемеровская область", "Asia/Novokuznetsk"),
            ("Юрга", "Кемеровская область", "Asia/Novokuznetsk"),
            ("Норильск", "Красноярский край", "Asia/Krasnoyarsk"),
            ("Ачинск", "Красноярский край", "Asia/Krasnoyarsk"),
            ("Железногорск", "Красноярский край", "Asia/Krasnoyarsk"),
            ("Канск", "Красноярский край", "Asia/Krasnoyarsk"),
            ("Лесосибирск", "Красноярский край", "Asia/Krasnoyarsk"),
            ("Минусинск", "Красноярский край", "Asia/Krasnoyarsk"),
            ("Черногорск", "Республика Хакасия", "Asia/Krasnoyarsk"),
            ("Братск", "Иркутская область", "Asia/Irkutsk"),
            ("Ангарск", "Иркутская область", "Asia/Irkutsk"),
            ("Усть-Илимск", "Иркутская область", "Asia/Irkutsk"),
            ("Усолье-Сибирское", "Иркутская область", "Asia/Irkutsk"),
            ("Краснокаменск", "Забайкальский край", "Asia/Chita"),
            ("Нерюнгри", "Республика Саха (Якутия)", "Asia/Yakutsk"),
            ("Белогорск", "Амурская область", "Asia/Yakutsk"),
            ("Уссурийск", "Приморский край", "Asia/Vladivostok"),
            ("Находка", "Приморский край", "Asia/Vladivostok"),
            ("Артём", "Приморский край", "Asia/Vladivostok"),
            ("Комсомольск-на-Амуре", "Хабаровский край", "Asia/Vladivostok"),
        };

        /// <summary>
        /// Duplicates ServiceBooking.API.Services.CitySearch.Normalize exactly (lowercase, 'ё'-&gt;'е',
        /// strip control chars/space/hyphen/parens/em dash) — see this migration's class doc comment for
        /// why it can't just call that method. Kept in lockstep by
        /// ServiceBooking.UnitTests/CityDirectoryDataTests.cs, which asserts, for every row in
        /// <see cref="Rows"/>, that this produces the same value CitySearch.Normalize(Name) would.
        /// </summary>
        public static string NormalizeForSearch(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            var lowered = raw.Trim().ToLowerInvariant().Replace('ё', 'е');
            return new string(lowered.Where(c =>
                !char.IsControl(c) && c != ' ' && c != '-' && c != '(' && c != ')' && c != '—').ToArray());
        }

        private static string SqlEscape(string value) => value.Replace("'", "''");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1+2+3 (§103.3): one idempotent INSERT ... SELECT ... WHERE NOT EXISTS statement,
            // built from Rows with SearchName computed by NormalizeForSearch — never hand-typed, never
            // migrationBuilder.InsertData (no idempotency mode).
            var valuesSql = new StringBuilder();
            for (var i = 0; i < Rows.Length; i++)
            {
                var (name, region, timeZoneId) = Rows[i];
                var searchName = NormalizeForSearch(name);
                valuesSql.Append("    ('")
                    .Append(SqlEscape(name)).Append("', '")
                    .Append(SqlEscape(region)).Append("', '")
                    .Append(SqlEscape(timeZoneId)).Append("', '")
                    .Append(SqlEscape(searchName)).Append("')");
                valuesSql.Append(i == Rows.Length - 1 ? "\n" : ",\n");
            }

            migrationBuilder.Sql(
                "INSERT INTO \"Cities\" (\"Name\", \"Region\", \"TimeZoneId\", \"IsActive\", \"SearchName\")\n" +
                "SELECT v.name, v.region, v.tz, TRUE, v.search_name\n" +
                "FROM (VALUES\n" + valuesSql +
                ") AS v(name, region, tz, search_name)\n" +
                "WHERE NOT EXISTS (\n" +
                "    SELECT 1 FROM \"Cities\" c WHERE c.\"Name\" = v.name AND c.\"Region\" = v.region\n" +
                ");");

            // Step 4 (§103.3): unconditional dedup by (Name, Region), keeping the row with the SMALLEST
            // Id — the one that could already be referenced by Company.CityId — THEN the unique index.
            // Must run in this order: Postgres refuses to create a unique index over rows that violate it.
            migrationBuilder.Sql(
                "DELETE FROM \"Cities\" c USING \"Cities\" dup " +
                "WHERE c.\"Name\" = dup.\"Name\" AND c.\"Region\" = dup.\"Region\" AND c.\"Id\" > dup.\"Id\";");

            migrationBuilder.CreateIndex(
                name: "IX_Cities_Name_Region",
                table: "Cities",
                columns: new[] { "Name", "Region" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Cities_Name_Region",
                table: "Cities");

            // Only the exact (Name, Region) pairs this migration could have inserted — never touches a
            // row that predates it. Does NOT attempt to undo the Up() dedup DELETE (not reconstructable,
            // same trade-off DeduplicateWorkingHours' own Down documents). If a company was assigned to
            // one of these cities in the meantime, FK_Companies_Cities_CityId (Restrict) stops this with
            // a descriptive database error instead of silently orphaning the company's CityId.
            var pairsSql = new StringBuilder();
            for (var i = 0; i < Rows.Length; i++)
            {
                var (name, region, _) = Rows[i];
                pairsSql.Append("    ('").Append(SqlEscape(name)).Append("', '").Append(SqlEscape(region)).Append("')");
                pairsSql.Append(i == Rows.Length - 1 ? "\n" : ",\n");
            }

            migrationBuilder.Sql(
                "DELETE FROM \"Cities\" c WHERE (c.\"Name\", c.\"Region\") IN (\n" + pairsSql + ");");
        }
    }
}
