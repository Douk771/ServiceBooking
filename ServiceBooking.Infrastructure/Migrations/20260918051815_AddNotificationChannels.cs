using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationChannels : Migration
    {
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
                values: new object[,]
                {
                    { "Калининград", "Калининградская область", "Europe/Kaliningrad", true, "калининград" },
                    { "Москва", "Москва", "Europe/Moscow", true, "москва" },
                    { "Санкт-Петербург", "Санкт-Петербург", "Europe/Moscow", true, "санктпетербург" },
                    { "Воронеж", "Воронежская область", "Europe/Moscow", true, "воронеж" },
                    { "Краснодар", "Краснодарский край", "Europe/Moscow", true, "краснодар" },
                    { "Сочи", "Краснодарский край", "Europe/Moscow", true, "сочи" },
                    { "Ростов-на-Дону", "Ростовская область", "Europe/Moscow", true, "ростовнадону" },
                    { "Нижний Новгород", "Нижегородская область", "Europe/Moscow", true, "нижнийновгород" },
                    { "Казань", "Республика Татарстан", "Europe/Moscow", true, "казань" },
                    { "Набережные Челны", "Республика Татарстан", "Europe/Moscow", true, "набережныечелны" },
                    { "Волгоград", "Волгоградская область", "Europe/Moscow", true, "волгоград" },
                    { "Ярославль", "Ярославская область", "Europe/Moscow", true, "ярославль" },
                    { "Тверь", "Тверская область", "Europe/Moscow", true, "тверь" },
                    { "Иваново", "Ивановская область", "Europe/Moscow", true, "иваново" },
                    { "Рязань", "Рязанская область", "Europe/Moscow", true, "рязань" },
                    { "Липецк", "Липецкая область", "Europe/Moscow", true, "липецк" },
                    { "Тула", "Тульская область", "Europe/Moscow", true, "тула" },
                    { "Курск", "Курская область", "Europe/Moscow", true, "курск" },
                    { "Белгород", "Белгородская область", "Europe/Moscow", true, "белгород" },
                    { "Смоленск", "Смоленская область", "Europe/Moscow", true, "смоленск" },
                    { "Брянск", "Брянская область", "Europe/Moscow", true, "брянск" },
                    { "Орёл", "Орловская область", "Europe/Moscow", true, "орел" },
                    { "Тамбов", "Тамбовская область", "Europe/Moscow", true, "тамбов" },
                    { "Пенза", "Пензенская область", "Europe/Moscow", true, "пенза" },
                    { "Саратов", "Саратовская область", "Europe/Moscow", true, "саратов" },
                    { "Астрахань", "Астраханская область", "Europe/Moscow", true, "астрахань" },
                    { "Ставрополь", "Ставропольский край", "Europe/Moscow", true, "ставрополь" },
                    { "Владимир", "Владимирская область", "Europe/Moscow", true, "владимир" },
                    { "Калуга", "Калужская область", "Europe/Moscow", true, "калуга" },
                    { "Кострома", "Костромская область", "Europe/Moscow", true, "кострома" },
                    { "Вологда", "Вологодская область", "Europe/Moscow", true, "вологда" },
                    { "Череповец", "Вологодская область", "Europe/Moscow", true, "череповец" },
                    { "Мурманск", "Мурманская область", "Europe/Moscow", true, "мурманск" },
                    { "Архангельск", "Архангельская область", "Europe/Moscow", true, "архангельск" },
                    { "Нарьян-Мар", "Ненецкий автономный округ", "Europe/Moscow", true, "нарьянмар" },
                    { "Петрозаводск", "Республика Карелия", "Europe/Moscow", true, "петрозаводск" },
                    { "Сыктывкар", "Республика Коми", "Europe/Moscow", true, "сыктывкар" },
                    { "Йошкар-Ола", "Республика Марий Эл", "Europe/Moscow", true, "йошкарола" },
                    { "Саранск", "Республика Мордовия", "Europe/Moscow", true, "саранск" },
                    { "Чебоксары", "Чувашская Республика", "Europe/Moscow", true, "чебоксары" },
                    { "Ульяновск", "Ульяновская область", "Europe/Moscow", true, "ульяновск" },
                    { "Киров", "Кировская область", "Europe/Moscow", true, "киров" },
                    { "Великий Новгород", "Новгородская область", "Europe/Moscow", true, "великийновгород" },
                    { "Псков", "Псковская область", "Europe/Moscow", true, "псков" },
                    { "Нальчик", "Кабардино-Балкарская Республика", "Europe/Moscow", true, "нальчик" },
                    { "Владикавказ", "Республика Северная Осетия — Алания", "Europe/Moscow", true, "владикавказ" },
                    { "Грозный", "Чеченская Республика", "Europe/Moscow", true, "грозный" },
                    { "Махачкала", "Республика Дагестан", "Europe/Moscow", true, "махачкала" },
                    { "Черкесск", "Карачаево-Черкесская Республика", "Europe/Moscow", true, "черкесск" },
                    { "Майкоп", "Республика Адыгея", "Europe/Moscow", true, "майкоп" },
                    { "Магас", "Республика Ингушетия", "Europe/Moscow", true, "магас" },
                    { "Элиста", "Республика Калмыкия", "Europe/Moscow", true, "элиста" },
                    { "Симферополь", "Республика Крым", "Europe/Simferopol", true, "симферополь" },
                    { "Севастополь", "Севастополь", "Europe/Simferopol", true, "севастополь" },
                    { "Самара", "Самарская область", "Europe/Samara", true, "самара" },
                    { "Тольятти", "Самарская область", "Europe/Samara", true, "тольятти" },
                    { "Ижевск", "Удмуртская Республика", "Europe/Samara", true, "ижевск" },
                    { "Екатеринбург", "Свердловская область", "Asia/Yekaterinburg", true, "екатеринбург" },
                    { "Нижний Тагил", "Свердловская область", "Asia/Yekaterinburg", true, "нижнийтагил" },
                    { "Челябинск", "Челябинская область", "Asia/Yekaterinburg", true, "челябинск" },
                    { "Магнитогорск", "Челябинская область", "Asia/Yekaterinburg", true, "магнитогорск" },
                    { "Уфа", "Республика Башкортостан", "Asia/Yekaterinburg", true, "уфа" },
                    { "Пермь", "Пермский край", "Asia/Yekaterinburg", true, "пермь" },
                    { "Оренбург", "Оренбургская область", "Asia/Yekaterinburg", true, "оренбург" },
                    { "Тюмень", "Тюменская область", "Asia/Yekaterinburg", true, "тюмень" },
                    { "Курган", "Курганская область", "Asia/Yekaterinburg", true, "курган" },
                    { "Ханты-Мансийск", "Ханты-Мансийский автономный округ", "Asia/Yekaterinburg", true, "хантымансийск" },
                    { "Сургут", "Ханты-Мансийский автономный округ", "Asia/Yekaterinburg", true, "сургут" },
                    { "Салехард", "Ямало-Ненецкий автономный округ", "Asia/Yekaterinburg", true, "салехард" },
                    { "Омск", "Омская область", "Asia/Omsk", true, "омск" },
                    { "Новосибирск", "Новосибирская область", "Asia/Novosibirsk", true, "новосибирск" },
                    { "Барнаул", "Алтайский край", "Asia/Barnaul", true, "барнаул" },
                    { "Горно-Алтайск", "Республика Алтай", "Asia/Barnaul", true, "горноалтайск" },
                    { "Томск", "Томская область", "Asia/Tomsk", true, "томск" },
                    { "Кемерово", "Кемеровская область", "Asia/Novokuznetsk", true, "кемерово" },
                    { "Новокузнецк", "Кемеровская область", "Asia/Novokuznetsk", true, "новокузнецк" },
                    { "Красноярск", "Красноярский край", "Asia/Krasnoyarsk", true, "красноярск" },
                    { "Абакан", "Республика Хакасия", "Asia/Krasnoyarsk", true, "абакан" },
                    { "Кызыл", "Республика Тыва", "Asia/Krasnoyarsk", true, "кызыл" },
                    { "Иркутск", "Иркутская область", "Asia/Irkutsk", true, "иркутск" },
                    { "Улан-Удэ", "Республика Бурятия", "Asia/Irkutsk", true, "уланудэ" },
                    { "Чита", "Забайкальский край", "Asia/Chita", true, "чита" },
                    { "Якутск", "Республика Саха (Якутия)", "Asia/Yakutsk", true, "якутск" },
                    { "Благовещенск", "Амурская область", "Asia/Yakutsk", true, "благовещенск" },
                    { "Владивосток", "Приморский край", "Asia/Vladivostok", true, "владивосток" },
                    { "Хабаровск", "Хабаровский край", "Asia/Vladivostok", true, "хабаровск" },
                    { "Биробиджан", "Еврейская автономная область", "Asia/Vladivostok", true, "биробиджан" },
                    { "Южно-Сахалинск", "Сахалинская область", "Asia/Sakhalin", true, "южносахалинск" },
                    { "Магадан", "Магаданская область", "Asia/Magadan", true, "магадан" },
                    { "Петропавловск-Камчатский", "Камчатский край", "Asia/Kamchatka", true, "петропавловсккамчатский" },
                    { "Анадырь", "Чукотский автономный округ", "Asia/Anadyr", true, "анадырь" },
                });

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
