using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationChannels : Migration
    {
        /// <summary>
        /// (Name, Region, TimeZoneId, SearchName) for every row this migration seeds — the original
        /// ~91-row city directory (§34.2/§35). Public and static, same pattern
        /// ExpandCityDirectory.Rows uses one migration later, so
        /// ServiceBooking.UnitTests/CityDirectoryDataTests.cs can compare the two arrays directly (e.g.
        /// detect a (Name, Region) pair present in both, which the later migration's own
        /// "WHERE NOT EXISTS" would then silently skip instead of actually inserting a new row) without
        /// touching a database. SearchName is kept hand-typed here (not recomputed via a shared
        /// normalizer) because this is the pre-existing seed CitySearchTests already cross-checks against
        /// CitySearch.Normalize; this array only exposes those same hand-typed values for reuse, it does
        /// not change how they were produced.
        /// </summary>
        /// <summary>
        /// Builds the object[,] (true 2D array) InsertData's multi-row overload requires, from
        /// <see cref="SeedRows"/>. NOT SeedRows.Select(...).ToArray() (object[][], one row per element):
        /// because C# arrays are covariant, an object[][] silently binds to InsertData's SINGLE-row
        /// object[] overload instead (object[][] is-a object[]) — the exact failure mode this comment
        /// exists to prevent, caught only by actually running this migration against Postgres, not by a
        /// successful `dotnet build`.
        /// </summary>
        private static object[,] BuildSeedRowsValues()
        {
            var values = new object[SeedRows.Length, 5];
            for (var i = 0; i < SeedRows.Length; i++)
            {
                values[i, 0] = SeedRows[i].Name;
                values[i, 1] = SeedRows[i].Region;
                values[i, 2] = SeedRows[i].TimeZoneId;
                values[i, 3] = true;
                values[i, 4] = SeedRows[i].SearchName;
            }
            return values;
        }

        public static readonly (string Name, string Region, string TimeZoneId, string SearchName)[] SeedRows =
        {
            ("Калининград", "Калининградская область", "Europe/Kaliningrad", "калининград"),
            ("Москва", "Москва", "Europe/Moscow", "москва"),
            ("Санкт-Петербург", "Санкт-Петербург", "Europe/Moscow", "санктпетербург"),
            ("Воронеж", "Воронежская область", "Europe/Moscow", "воронеж"),
            ("Краснодар", "Краснодарский край", "Europe/Moscow", "краснодар"),
            ("Сочи", "Краснодарский край", "Europe/Moscow", "сочи"),
            ("Ростов-на-Дону", "Ростовская область", "Europe/Moscow", "ростовнадону"),
            ("Нижний Новгород", "Нижегородская область", "Europe/Moscow", "нижнийновгород"),
            ("Казань", "Республика Татарстан", "Europe/Moscow", "казань"),
            ("Набережные Челны", "Республика Татарстан", "Europe/Moscow", "набережныечелны"),
            ("Волгоград", "Волгоградская область", "Europe/Moscow", "волгоград"),
            ("Ярославль", "Ярославская область", "Europe/Moscow", "ярославль"),
            ("Тверь", "Тверская область", "Europe/Moscow", "тверь"),
            ("Иваново", "Ивановская область", "Europe/Moscow", "иваново"),
            ("Рязань", "Рязанская область", "Europe/Moscow", "рязань"),
            ("Липецк", "Липецкая область", "Europe/Moscow", "липецк"),
            ("Тула", "Тульская область", "Europe/Moscow", "тула"),
            ("Курск", "Курская область", "Europe/Moscow", "курск"),
            ("Белгород", "Белгородская область", "Europe/Moscow", "белгород"),
            ("Смоленск", "Смоленская область", "Europe/Moscow", "смоленск"),
            ("Брянск", "Брянская область", "Europe/Moscow", "брянск"),
            ("Орёл", "Орловская область", "Europe/Moscow", "орел"),
            ("Тамбов", "Тамбовская область", "Europe/Moscow", "тамбов"),
            ("Пенза", "Пензенская область", "Europe/Moscow", "пенза"),
            ("Саратов", "Саратовская область", "Europe/Moscow", "саратов"),
            ("Астрахань", "Астраханская область", "Europe/Moscow", "астрахань"),
            ("Ставрополь", "Ставропольский край", "Europe/Moscow", "ставрополь"),
            ("Владимир", "Владимирская область", "Europe/Moscow", "владимир"),
            ("Калуга", "Калужская область", "Europe/Moscow", "калуга"),
            ("Кострома", "Костромская область", "Europe/Moscow", "кострома"),
            ("Вологда", "Вологодская область", "Europe/Moscow", "вологда"),
            ("Череповец", "Вологодская область", "Europe/Moscow", "череповец"),
            ("Мурманск", "Мурманская область", "Europe/Moscow", "мурманск"),
            ("Архангельск", "Архангельская область", "Europe/Moscow", "архангельск"),
            ("Нарьян-Мар", "Ненецкий автономный округ", "Europe/Moscow", "нарьянмар"),
            ("Петрозаводск", "Республика Карелия", "Europe/Moscow", "петрозаводск"),
            ("Сыктывкар", "Республика Коми", "Europe/Moscow", "сыктывкар"),
            ("Йошкар-Ола", "Республика Марий Эл", "Europe/Moscow", "йошкарола"),
            ("Саранск", "Республика Мордовия", "Europe/Moscow", "саранск"),
            ("Чебоксары", "Чувашская Республика", "Europe/Moscow", "чебоксары"),
            ("Ульяновск", "Ульяновская область", "Europe/Moscow", "ульяновск"),
            ("Киров", "Кировская область", "Europe/Moscow", "киров"),
            ("Великий Новгород", "Новгородская область", "Europe/Moscow", "великийновгород"),
            ("Псков", "Псковская область", "Europe/Moscow", "псков"),
            ("Нальчик", "Кабардино-Балкарская Республика", "Europe/Moscow", "нальчик"),
            ("Владикавказ", "Республика Северная Осетия — Алания", "Europe/Moscow", "владикавказ"),
            ("Грозный", "Чеченская Республика", "Europe/Moscow", "грозный"),
            ("Махачкала", "Республика Дагестан", "Europe/Moscow", "махачкала"),
            ("Черкесск", "Карачаево-Черкесская Республика", "Europe/Moscow", "черкесск"),
            ("Майкоп", "Республика Адыгея", "Europe/Moscow", "майкоп"),
            ("Магас", "Республика Ингушетия", "Europe/Moscow", "магас"),
            ("Элиста", "Республика Калмыкия", "Europe/Moscow", "элиста"),
            ("Симферополь", "Республика Крым", "Europe/Simferopol", "симферополь"),
            ("Севастополь", "Севастополь", "Europe/Simferopol", "севастополь"),
            ("Самара", "Самарская область", "Europe/Samara", "самара"),
            ("Тольятти", "Самарская область", "Europe/Samara", "тольятти"),
            ("Ижевск", "Удмуртская Республика", "Europe/Samara", "ижевск"),
            ("Екатеринбург", "Свердловская область", "Asia/Yekaterinburg", "екатеринбург"),
            ("Нижний Тагил", "Свердловская область", "Asia/Yekaterinburg", "нижнийтагил"),
            ("Челябинск", "Челябинская область", "Asia/Yekaterinburg", "челябинск"),
            ("Магнитогорск", "Челябинская область", "Asia/Yekaterinburg", "магнитогорск"),
            ("Уфа", "Республика Башкортостан", "Asia/Yekaterinburg", "уфа"),
            ("Пермь", "Пермский край", "Asia/Yekaterinburg", "пермь"),
            ("Оренбург", "Оренбургская область", "Asia/Yekaterinburg", "оренбург"),
            ("Тюмень", "Тюменская область", "Asia/Yekaterinburg", "тюмень"),
            ("Курган", "Курганская область", "Asia/Yekaterinburg", "курган"),
            ("Ханты-Мансийск", "Ханты-Мансийский автономный округ", "Asia/Yekaterinburg", "хантымансийск"),
            ("Сургут", "Ханты-Мансийский автономный округ", "Asia/Yekaterinburg", "сургут"),
            ("Салехард", "Ямало-Ненецкий автономный округ", "Asia/Yekaterinburg", "салехард"),
            ("Омск", "Омская область", "Asia/Omsk", "омск"),
            ("Новосибирск", "Новосибирская область", "Asia/Novosibirsk", "новосибирск"),
            ("Барнаул", "Алтайский край", "Asia/Barnaul", "барнаул"),
            ("Горно-Алтайск", "Республика Алтай", "Asia/Barnaul", "горноалтайск"),
            ("Томск", "Томская область", "Asia/Tomsk", "томск"),
            ("Кемерово", "Кемеровская область", "Asia/Novokuznetsk", "кемерово"),
            ("Новокузнецк", "Кемеровская область", "Asia/Novokuznetsk", "новокузнецк"),
            ("Красноярск", "Красноярский край", "Asia/Krasnoyarsk", "красноярск"),
            ("Абакан", "Республика Хакасия", "Asia/Krasnoyarsk", "абакан"),
            ("Кызыл", "Республика Тыва", "Asia/Krasnoyarsk", "кызыл"),
            ("Иркутск", "Иркутская область", "Asia/Irkutsk", "иркутск"),
            ("Улан-Удэ", "Республика Бурятия", "Asia/Irkutsk", "уланудэ"),
            ("Чита", "Забайкальский край", "Asia/Chita", "чита"),
            ("Якутск", "Республика Саха (Якутия)", "Asia/Yakutsk", "якутск"),
            ("Благовещенск", "Амурская область", "Asia/Yakutsk", "благовещенск"),
            ("Владивосток", "Приморский край", "Asia/Vladivostok", "владивосток"),
            ("Хабаровск", "Хабаровский край", "Asia/Vladivostok", "хабаровск"),
            ("Биробиджан", "Еврейская автономная область", "Asia/Vladivostok", "биробиджан"),
            ("Южно-Сахалинск", "Сахалинская область", "Asia/Sakhalin", "южносахалинск"),
            ("Магадан", "Магаданская область", "Asia/Magadan", "магадан"),
            ("Петропавловск-Камчатский", "Камчатский край", "Asia/Kamchatka", "петропавловсккамчатский"),
            ("Анадырь", "Чукотский автономный округ", "Asia/Anadyr", "анадырь"),
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowNotificationChannel",
                table: "SubscriptionPlanConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CityId",
                table: "Companies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Companies",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Europe/Moscow");

            migrationBuilder.AddColumn<bool>(
                name: "TimeZoneIsManual",
                table: "Companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Cities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Region = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SearchName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cities", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Cities",
                columns: new[] { "Name", "Region", "TimeZoneId", "IsActive", "SearchName" },
                values: BuildSeedRowsValues());

            // §34.2, §35 migration 2: seed ~90 administrative centers/major cities FIRST (SeedCities must
            // precede AddCompanyCityAndTimeZone — jointly implemented here as one migration since this
            // session generated the schema as a single EF diff rather than nine separate files; see the
            // PR description for that deviation from ARCHITECTURE_CYCLE4.md §35's exact migration count).
            // Then backfill every EXISTING company to Барнаул, Алтайский край / Asia/Barnaul — Altai Krai
            // moved to this zone in 2016 and it is NOT the same IANA id as Novosibirsk despite sharing the
            // same UTC+7 offset today; using Asia/Novosibirsk here would be the exact mistake this comment
            // exists to prevent.
            migrationBuilder.Sql(@"
                UPDATE ""Companies""
                SET ""CityId"" = (SELECT ""Id"" FROM ""Cities"" WHERE ""Name"" = 'Барнаул' AND ""Region"" = 'Алтайский край'),
                    ""TimeZoneId"" = 'Asia/Barnaul'
                WHERE ""CityId"" IS NULL;");

            migrationBuilder.CreateTable(
                name: "CompanyNotificationSettings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnabledTypeMask = table.Column<int>(type: "integer", nullable: false),
                    ReminderLeadMinutes = table.Column<int>(type: "integer", nullable: false),
                    MinLeadMinutes = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyNotificationSettings", x => x.CompanyId);
                    table.ForeignKey(
                        name: "FK_CompanyNotificationSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<string>(type: "text", nullable: false),
                    Transport = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    ProviderInstanceId = table.Column<string>(type: "text", nullable: true),
                    ProviderSecretCiphertext = table.Column<string>(type: "text", nullable: true),
                    ProviderSecretKeyId = table.Column<string>(type: "text", nullable: true),
                    OrphanedInstanceId = table.Column<string>(type: "text", nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ContactEmail = table.Column<string>(type: "text", nullable: true),
                    PaidFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsSuspendedByAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    IdleSinceUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IdleWarningSentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InstanceCreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConnectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastStateCheckAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsecutiveSendFailures = table.Column<int>(type: "integer", nullable: false),
                    LastTestMessageAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisruptionNotifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RiskAcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RiskAcceptedVersion = table.Column<string>(type: "text", nullable: true),
                    ReplacedByChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationChannels_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotificationChannels_NotificationChannels_ReplacedByChannel~",
                        column: x => x.ReplacedByChannelId,
                        principalTable: "NotificationChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationOptOuts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    OptedOutAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationOptOuts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationTemplateHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    PreviousBody = table.Column<string>(type: "text", nullable: false),
                    ChangedByUserId = table.Column<string>(type: "text", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationTemplateHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationTemplates_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlatformSettingChangeLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    OldValue = table.Column<string>(type: "text", nullable: true),
                    NewValue = table.Column<string>(type: "text", nullable: true),
                    ChangedByUserId = table.Column<string>(type: "text", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettingChangeLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ChannelCompanyAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedByUserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelCompanyAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelCompanyAssignments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChannelCompanyAssignments_NotificationChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "NotificationChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChannelPaymentLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedByUserId = table.Column<string>(type: "text", nullable: false),
                    OldPaidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NewPaidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelPaymentLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelPaymentLogs_NotificationChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "NotificationChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChannelStateEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromState = table.Column<int>(type: "integer", nullable: false),
                    ToState = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelStateEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelStateEvents_NotificationChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "NotificationChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OutboundNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    RecipientPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RecipientName = table.Column<string>(type: "text", nullable: true),
                    RecipientUserId = table.Column<string>(type: "text", nullable: true),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VisitStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: true),
                    ReasonDetail = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReadAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutboundNotifications_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OutboundNotifications_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutboundNotifications_NotificationChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "NotificationChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Companies_CityId",
                table: "Companies",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelCompanyAssignments_ChannelId",
                table: "ChannelCompanyAssignments",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelCompanyAssignments_CompanyId",
                table: "ChannelCompanyAssignments",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelPaymentLogs_ChannelId",
                table: "ChannelPaymentLogs",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelStateEvents_ChannelId_OccurredAtUtc",
                table: "ChannelStateEvents",
                columns: new[] { "ChannelId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Cities_Region_Name",
                table: "Cities",
                columns: new[] { "Region", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Cities_SearchName",
                table: "Cities",
                column: "SearchName");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationChannels_OwnerUserId",
                table: "NotificationChannels",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationChannels_ProviderInstanceId",
                table: "NotificationChannels",
                column: "ProviderInstanceId",
                unique: true,
                filter: "\"ProviderInstanceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationChannels_ReplacedByChannelId",
                table: "NotificationChannels",
                column: "ReplacedByChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationChannels_State",
                table: "NotificationChannels",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOptOuts_Phone",
                table: "NotificationOptOuts",
                column: "Phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationTemplateHistories_CompanyId_Type_ChangedAtUtc",
                table: "NotificationTemplateHistories",
                columns: new[] { "CompanyId", "Type", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationTemplates_CompanyId_Type",
                table: "NotificationTemplates",
                columns: new[] { "CompanyId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboundNotifications_BookingId",
                table: "OutboundNotifications",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundNotifications_Channel_Status",
                table: "OutboundNotifications",
                columns: new[] { "ChannelId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundNotifications_Company_CreatedAt",
                table: "OutboundNotifications",
                columns: new[] { "CompanyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundNotifications_Dispatch",
                table: "OutboundNotifications",
                columns: new[] { "VisitStartUtc", "CreatedAt" },
                filter: "\"Status\" = 0")
                .Annotation("Npgsql:IndexInclude", new[] { "DueAtUtc", "ChannelId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundNotifications_IdempotencyKey",
                table: "OutboundNotifications",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboundNotifications_ProviderMessageId",
                table: "OutboundNotifications",
                column: "ProviderMessageId",
                filter: "\"ProviderMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformSettingChangeLogs_Key",
                table: "PlatformSettingChangeLogs",
                column: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_Cities_CityId",
                table: "Companies",
                column: "CityId",
                principalTable: "Cities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Companies_Cities_CityId",
                table: "Companies");

            migrationBuilder.DropTable(
                name: "ChannelCompanyAssignments");

            migrationBuilder.DropTable(
                name: "ChannelPaymentLogs");

            migrationBuilder.DropTable(
                name: "ChannelStateEvents");

            migrationBuilder.DropTable(
                name: "Cities");

            migrationBuilder.DropTable(
                name: "CompanyNotificationSettings");

            migrationBuilder.DropTable(
                name: "NotificationOptOuts");

            migrationBuilder.DropTable(
                name: "NotificationTemplateHistories");

            migrationBuilder.DropTable(
                name: "NotificationTemplates");

            migrationBuilder.DropTable(
                name: "OutboundNotifications");

            migrationBuilder.DropTable(
                name: "PlatformSettingChangeLogs");

            migrationBuilder.DropTable(
                name: "PlatformSettings");

            migrationBuilder.DropTable(
                name: "NotificationChannels");

            migrationBuilder.DropIndex(
                name: "IX_Companies_CityId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "AllowNotificationChannel",
                table: "SubscriptionPlanConfigs");

            migrationBuilder.DropColumn(
                name: "CityId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "TimeZoneIsManual",
                table: "Companies");
        }
    }
}
