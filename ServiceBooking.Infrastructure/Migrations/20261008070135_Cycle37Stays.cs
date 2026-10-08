using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Cycle37Stays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_TrialPhoneRegistrations_Key",
                table: "TrialPhoneRegistrations");

            migrationBuilder.DropIndex(
                name: "UX_TrialGrants_OnePerAccount",
                table: "TrialGrants");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.AddColumn<int>(
                name: "Line",
                table: "TrialPhoneRegistrations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Line",
                table: "TrialGrants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxHouses",
                table: "SubscriptionPlanConfigs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StayBookingId",
                table: "StaffPushNotifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StayBookingId",
                table: "StaffMaxMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StayBookingId",
                table: "OutboundNotifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StaffPosition",
                table: "CompanyMembers",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Houses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    ExtraBedsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ExtraBedsMax = table.Column<int>(type: "integer", nullable: false),
                    ExtraBedPriceRub = table.Column<int>(type: "integer", nullable: false),
                    DogsForbidden = table.Column<bool>(type: "boolean", nullable: false),
                    HasCot = table.Column<bool>(type: "boolean", nullable: false),
                    AmenitiesMask = table.Column<long>(type: "bigint", nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    YandexMapsUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TwoGisUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CheckInInfoText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PriceMode = table.Column<int>(type: "integer", nullable: false),
                    ConstantPriceRub = table.Column<int>(type: "integer", nullable: true),
                    ObjectKind = table.Column<int>(type: "integer", nullable: true),
                    RegistryNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    RegistryUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Houses", x => x.Id);
                    table.CheckConstraint("CK_Houses_NotPublishedAndArchived", "NOT (\"IsPublished\" AND \"ArchivedAtUtc\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Houses_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaysSettings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckInTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    CheckOutTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    MinNights = table.Column<int>(type: "integer", nullable: false),
                    MaxNights = table.Column<int>(type: "integer", nullable: false),
                    HorizonDays = table.Column<int>(type: "integer", nullable: false),
                    AllowGapFill = table.Column<bool>(type: "boolean", nullable: false),
                    AllowSameDayCheckIn = table.Column<bool>(type: "boolean", nullable: false),
                    HoldMinutes = table.Column<int>(type: "integer", nullable: false),
                    PrepayPercent = table.Column<int>(type: "integer", nullable: false),
                    PaymentDetails = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PaymentPurpose = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CancellationPolicy = table.Column<int>(type: "integer", nullable: false),
                    DogFeeRub = table.Column<int>(type: "integer", nullable: false),
                    CotFeeRub = table.Column<int>(type: "integer", nullable: false),
                    CheckInInfoSendTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    CheckInInfoText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CheckInInfoSendFullText = table.Column<bool>(type: "boolean", nullable: false),
                    ArrivalReminderEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    HousekeeperSeesGuestComment = table.Column<bool>(type: "boolean", nullable: false),
                    GuestWebPushEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    GuestMessengerEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    StaffMaxEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ProviderStatus = table.Column<int>(type: "integer", nullable: true),
                    ProviderName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ProviderInn = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    ProviderOgrn = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    ProviderClaimsAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BookingsRevision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaysSettings", x => x.CompanyId);
                    table.CheckConstraint("CK_StaysSettings_CheckOutNotAfterCheckIn", "\"CheckOutTime\" <= \"CheckInTime\"");
                    table.CheckConstraint("CK_StaysSettings_Nights", "\"MinNights\" BETWEEN 1 AND 30 AND \"MaxNights\" BETWEEN 1 AND 90 AND \"MinNights\" <= \"MaxNights\"");
                    table.ForeignKey(
                        name: "FK_StaysSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaysSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BillingAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanConfigId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaysSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaysSubscriptions_BillingAccounts_BillingAccountId",
                        column: x => x.BillingAccountId,
                        principalTable: "BillingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StaysSubscriptions_SubscriptionPlanConfigs_PlanConfigId",
                        column: x => x.PlanConfigId,
                        principalTable: "SubscriptionPlanConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "HouseBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseBlocks", x => x.Id);
                    table.CheckConstraint("CK_HouseBlocks_Dates", "\"EndDate\" > \"StartDate\"");
                    table.ForeignKey(
                        name: "FK_HouseBlocks_Houses_HouseId",
                        column: x => x.HouseId,
                        principalTable: "Houses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HousePhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HousePhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HousePhotos_Houses_HouseId",
                        column: x => x.HouseId,
                        principalTable: "Houses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HousePricePeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PriceRub = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HousePricePeriods", x => x.Id);
                    table.CheckConstraint("CK_HousePricePeriods_Dates", "\"StartDate\" <= \"EndDate\" AND \"EndDate\" - \"StartDate\" <= 730");
                    table.CheckConstraint("CK_HousePricePeriods_Price", "\"PriceRub\" BETWEEN 1 AND 1000000");
                    table.ForeignKey(
                        name: "FK_HousePricePeriods_Houses_HouseId",
                        column: x => x.HouseId,
                        principalTable: "Houses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HouseRegistryAttestations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectKind = table.Column<int>(type: "integer", nullable: false),
                    RegistryNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    RegistryUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NoticeVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AttestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttestedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseRegistryAttestations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseRegistryAttestations_Houses_HouseId",
                        column: x => x.HouseId,
                        principalTable: "Houses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StayBookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsManual = table.Column<bool>(type: "boolean", nullable: false),
                    CheckInDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CheckOutDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Nights = table.Column<int>(type: "integer", nullable: false),
                    Adults = table.Column<int>(type: "integer", nullable: false),
                    Children = table.Column<int>(type: "integer", nullable: false),
                    Dogs = table.Column<int>(type: "integer", nullable: false),
                    NeedCot = table.Column<bool>(type: "boolean", nullable: false),
                    ExtraBeds = table.Column<int>(type: "integer", nullable: false),
                    ArrivalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    GuestKind = table.Column<int>(type: "integer", nullable: false),
                    GuestUserId = table.Column<string>(type: "text", nullable: true),
                    GuestName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    GuestPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    HoldExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TotalRub = table.Column<int>(type: "integer", nullable: false),
                    PrepayRub = table.Column<int>(type: "integer", nullable: false),
                    DueAtCheckInRub = table.Column<int>(type: "integer", nullable: false),
                    PrepayPercentSnapshot = table.Column<int>(type: "integer", nullable: false),
                    NightPricesJson = table.Column<string>(type: "jsonb", nullable: false),
                    CancellationPolicySnapshot = table.Column<int>(type: "integer", nullable: false),
                    CheckInTimeSnapshot = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    CheckOutTimeSnapshot = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    TimeZoneIdSnapshot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaymentDetailsSnapshot = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PaymentPurposeSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProviderSnapshotJson = table.Column<string>(type: "jsonb", nullable: true),
                    ConsentPrivacyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConsentTermsVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConsentAcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BookingNoticeVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BookingTermsVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CancellationTermsVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    NotifyByMessenger = table.Column<bool>(type: "boolean", nullable: false),
                    MessengerConsentVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MessengerConsentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    PaymentConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaymentConfirmedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    PaymentConfirmedByNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TerminalAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HoldReminderQueuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArrivalReminderQueuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckInInfoReleasedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaymentProofsPurgedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PersonalDataErased = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StayBookings", x => x.Id);
                    table.CheckConstraint("CK_StayBookings_Dates", "\"CheckOutDate\" > \"CheckInDate\" AND \"CheckOutDate\" - \"CheckInDate\" <= 366");
                    table.CheckConstraint("CK_StayBookings_Nights", "\"Nights\" = \"CheckOutDate\" - \"CheckInDate\"");
                    table.ForeignKey(
                        name: "FK_StayBookings_AspNetUsers_GuestUserId",
                        column: x => x.GuestUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StayBookings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StayBookings_Houses_HouseId",
                        column: x => x.HouseId,
                        principalTable: "Houses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HouseBlockEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseBlockId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ActorNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: true),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseBlockEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseBlockEvents_HouseBlocks_HouseBlockId",
                        column: x => x.HouseBlockId,
                        principalTable: "HouseBlocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HouseOccupancies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    StayBookingId = table.Column<Guid>(type: "uuid", nullable: true),
                    HouseBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    HoldExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReleasedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseOccupancies", x => x.Id);
                    table.CheckConstraint("CK_HouseOccupancies_Dates", "\"EndDate\" > \"StartDate\"");
                    table.CheckConstraint("CK_HouseOccupancies_Source", "((\"Source\" = 0) = (\"StayBookingId\" IS NOT NULL)) AND ((\"Source\" = 1) = (\"HouseBlockId\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_HouseOccupancies_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HouseOccupancies_HouseBlocks_HouseBlockId",
                        column: x => x.HouseBlockId,
                        principalTable: "HouseBlocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HouseOccupancies_Houses_HouseId",
                        column: x => x.HouseId,
                        principalTable: "Houses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HouseOccupancies_StayBookings_StayBookingId",
                        column: x => x.StayBookingId,
                        principalTable: "StayBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StayBookingCharges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StayBookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPriceRub = table.Column<int>(type: "integer", nullable: false),
                    NightsCount = table.Column<int>(type: "integer", nullable: false),
                    AmountRub = table.Column<int>(type: "integer", nullable: false),
                    PrepayEligible = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StayBookingCharges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StayBookingCharges_StayBookings_StayBookingId",
                        column: x => x.StayBookingId,
                        principalTable: "StayBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StayBookingEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StayBookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorKind = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ActorNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FromStatus = table.Column<int>(type: "integer", nullable: true),
                    ToStatus = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StayBookingEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StayBookingEvents_StayBookings_StayBookingId",
                        column: x => x.StayBookingId,
                        principalTable: "StayBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StayGuestPushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StayBookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    P256dhCiphertext = table.Column<string>(type: "text", nullable: false),
                    AuthCiphertext = table.Column<string>(type: "text", nullable: false),
                    KeyId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSuccessAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StayGuestPushSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StayGuestPushSubscriptions_StayBookings_StayBookingId",
                        column: x => x.StayBookingId,
                        principalTable: "StayBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StayPaymentProofs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StayBookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SizeBytes = table.Column<int>(type: "integer", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PurgedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StayPaymentProofs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StayPaymentProofs_StayBookings_StayBookingId",
                        column: x => x.StayBookingId,
                        principalTable: "StayBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StayGuestPushNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StayBookingId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: true),
                    ReasonDetail = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StayGuestPushNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StayGuestPushNotifications_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StayGuestPushNotifications_StayBookings_StayBookingId",
                        column: x => x.StayBookingId,
                        principalTable: "StayBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StayGuestPushNotifications_StayGuestPushSubscriptions_Subsc~",
                        column: x => x.SubscriptionId,
                        principalTable: "StayGuestPushSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "UX_TrialPhoneRegistrations_Key",
                table: "TrialPhoneRegistrations",
                columns: new[] { "Line", "PhoneKeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_TrialGrants_OnePerAccount",
                table: "TrialGrants",
                columns: new[] { "BillingAccountId", "Line" },
                unique: true,
                filter: "\"Source\" <> 2");

            migrationBuilder.CreateIndex(
                name: "IX_StaffPushNotifications_StayBookingId",
                table: "StaffPushNotifications",
                column: "StayBookingId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StaffPushNotifications_OneSubject",
                table: "StaffPushNotifications",
                sql: "(CASE WHEN \"BookingId\" IS NULL THEN 0 ELSE 1 END) + (CASE WHEN \"OrderId\" IS NULL THEN 0 ELSE 1 END) + (CASE WHEN \"StayBookingId\" IS NULL THEN 0 ELSE 1 END) <= 1");

            migrationBuilder.CreateIndex(
                name: "IX_StaffMaxMessages_StayBookingId",
                table: "StaffMaxMessages",
                column: "StayBookingId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundNotifications_StayBookingId",
                table: "OutboundNotifications",
                column: "StayBookingId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboundNotifications_OneSubject",
                table: "OutboundNotifications",
                sql: "(CASE WHEN \"BookingId\" IS NULL THEN 0 ELSE 1 END) + (CASE WHEN \"OrderId\" IS NULL THEN 0 ELSE 1 END) + (CASE WHEN \"StayBookingId\" IS NULL THEN 0 ELSE 1 END) <= 1");

            migrationBuilder.CreateIndex(
                name: "IX_HouseBlockEvents_HouseBlockId_OccurredAtUtc",
                table: "HouseBlockEvents",
                columns: new[] { "HouseBlockId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HouseBlocks_CompanyId_HouseId",
                table: "HouseBlocks",
                columns: new[] { "CompanyId", "HouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_HouseBlocks_HouseId",
                table: "HouseBlocks",
                column: "HouseId");

            migrationBuilder.CreateIndex(
                name: "IX_HouseOccupancies_Active",
                table: "HouseOccupancies",
                columns: new[] { "HouseId", "EndDate" },
                filter: "\"ReleasedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HouseOccupancies_CompanyId",
                table: "HouseOccupancies",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "UX_HouseOccupancies_Block",
                table: "HouseOccupancies",
                column: "HouseBlockId",
                unique: true,
                filter: "\"HouseBlockId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_HouseOccupancies_Booking",
                table: "HouseOccupancies",
                column: "StayBookingId",
                unique: true,
                filter: "\"StayBookingId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HousePhotos_HouseId_Position",
                table: "HousePhotos",
                columns: new[] { "HouseId", "Position" });

            migrationBuilder.CreateIndex(
                name: "UX_HousePricePeriods_SingleDay",
                table: "HousePricePeriods",
                columns: new[] { "HouseId", "StartDate" },
                unique: true,
                filter: "\"StartDate\" = \"EndDate\"");

            migrationBuilder.CreateIndex(
                name: "IX_HouseRegistryAttestations_HouseId_AttestedAtUtc",
                table: "HouseRegistryAttestations",
                columns: new[] { "HouseId", "AttestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Houses_CompanyId_Position",
                table: "Houses",
                columns: new[] { "CompanyId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_Houses_CompanyId_Slug",
                table: "Houses",
                columns: new[] { "CompanyId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Houses_Published",
                table: "Houses",
                column: "CompanyId",
                filter: "\"IsPublished\" AND \"ArchivedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StayBookingCharges_StayBookingId_Position",
                table: "StayBookingCharges",
                columns: new[] { "StayBookingId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_StayBookingEvents_OccurredAtUtc",
                table: "StayBookingEvents",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StayBookingEvents_StayBookingId_OccurredAtUtc",
                table: "StayBookingEvents",
                columns: new[] { "StayBookingId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StayBookings_CompanyId_IdempotencyKey",
                table: "StayBookings",
                columns: new[] { "CompanyId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StayBookings_CompanyId_Status",
                table: "StayBookings",
                columns: new[] { "CompanyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StayBookings_ConfirmedCheckIn",
                table: "StayBookings",
                column: "CheckInDate",
                filter: "\"Status\" = 2");

            migrationBuilder.CreateIndex(
                name: "IX_StayBookings_GuestUserId_CreatedAtUtc",
                table: "StayBookings",
                columns: new[] { "GuestUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StayBookings_HoldExpiry",
                table: "StayBookings",
                column: "HoldExpiresAtUtc",
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_StayBookings_HouseId_CheckInDate",
                table: "StayBookings",
                columns: new[] { "HouseId", "CheckInDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StayBookings_Phone",
                table: "StayBookings",
                columns: new[] { "GuestPhone", "CreatedAtUtc" },
                filter: "\"GuestPhone\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StayBookings_PublicToken",
                table: "StayBookings",
                column: "PublicToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StayGuestPushNotifications_CompanyId",
                table: "StayGuestPushNotifications",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_StayGuestPushNotifications_Dispatch",
                table: "StayGuestPushNotifications",
                columns: new[] { "ExpiresAtUtc", "CreatedAt" },
                filter: "\"Status\" = 0")
                .Annotation("Npgsql:IndexInclude", new[] { "CompanyId", "SubscriptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_StayGuestPushNotifications_IdempotencyKey",
                table: "StayGuestPushNotifications",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StayGuestPushNotifications_StayBookingId",
                table: "StayGuestPushNotifications",
                column: "StayBookingId");

            migrationBuilder.CreateIndex(
                name: "IX_StayGuestPushNotifications_SubscriptionId",
                table: "StayGuestPushNotifications",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_StayGuestPushSubscriptions_CreatedAtUtc",
                table: "StayGuestPushSubscriptions",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StayGuestPushSubscriptions_StayBookingId_Endpoint",
                table: "StayGuestPushSubscriptions",
                columns: new[] { "StayBookingId", "Endpoint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StayPaymentProofs_StayBookingId",
                table: "StayPaymentProofs",
                column: "StayBookingId");

            migrationBuilder.CreateIndex(
                name: "IX_StaysSubscriptions_BillingAccountId",
                table: "StaysSubscriptions",
                column: "BillingAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaysSubscriptions_PlanConfigId",
                table: "StaysSubscriptions",
                column: "PlanConfigId");

            migrationBuilder.AddForeignKey(
                name: "FK_OutboundNotifications_StayBookings_StayBookingId",
                table: "OutboundNotifications",
                column: "StayBookingId",
                principalTable: "StayBookings",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_StaffMaxMessages_StayBookings_StayBookingId",
                table: "StaffMaxMessages",
                column: "StayBookingId",
                principalTable: "StayBookings",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_StaffPushNotifications_StayBookings_StayBookingId",
                table: "StaffPushNotifications",
                column: "StayBookingId",
                principalTable: "StayBookings",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // WARNING (DEPLOY.md, DO-37-03): re-creating the old unique index on TrialPhoneRegistrations(PhoneKeyHash) FAILS if a "Дома" trial was
            // granted to a number that also has a "Записи" trial (duplicate key) — check by hand before rolling back. Same class of risk as C24-10.
            migrationBuilder.Sql(
                """
                DELETE FROM "TrialGrants" WHERE "Line" = 2;
                DELETE FROM "TrialPhoneRegistrations" WHERE "Line" = 2;
                DELETE FROM "PlanOptionRules" WHERE "PlanConfigId" IN (SELECT "Id" FROM "SubscriptionPlanConfigs" WHERE "Line" = 2);
                DELETE FROM "SubscriptionPlanConfigs" WHERE "Line" = 2;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_OutboundNotifications_StayBookings_StayBookingId",
                table: "OutboundNotifications");

            migrationBuilder.DropForeignKey(
                name: "FK_StaffMaxMessages_StayBookings_StayBookingId",
                table: "StaffMaxMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_StaffPushNotifications_StayBookings_StayBookingId",
                table: "StaffPushNotifications");

            migrationBuilder.DropTable(
                name: "HouseBlockEvents");

            migrationBuilder.DropTable(
                name: "HouseOccupancies");

            migrationBuilder.DropTable(
                name: "HousePhotos");

            migrationBuilder.DropTable(
                name: "HousePricePeriods");

            migrationBuilder.DropTable(
                name: "HouseRegistryAttestations");

            migrationBuilder.DropTable(
                name: "StayBookingCharges");

            migrationBuilder.DropTable(
                name: "StayBookingEvents");

            migrationBuilder.DropTable(
                name: "StayGuestPushNotifications");

            migrationBuilder.DropTable(
                name: "StayPaymentProofs");

            migrationBuilder.DropTable(
                name: "StaysSettings");

            migrationBuilder.DropTable(
                name: "StaysSubscriptions");

            migrationBuilder.DropTable(
                name: "HouseBlocks");

            migrationBuilder.DropTable(
                name: "StayGuestPushSubscriptions");

            migrationBuilder.DropTable(
                name: "StayBookings");

            migrationBuilder.DropTable(
                name: "Houses");

            migrationBuilder.DropIndex(
                name: "UX_TrialPhoneRegistrations_Key",
                table: "TrialPhoneRegistrations");

            migrationBuilder.DropIndex(
                name: "UX_TrialGrants_OnePerAccount",
                table: "TrialGrants");

            migrationBuilder.DropIndex(
                name: "IX_StaffPushNotifications_StayBookingId",
                table: "StaffPushNotifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StaffPushNotifications_OneSubject",
                table: "StaffPushNotifications");

            migrationBuilder.DropIndex(
                name: "IX_StaffMaxMessages_StayBookingId",
                table: "StaffMaxMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutboundNotifications_StayBookingId",
                table: "OutboundNotifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboundNotifications_OneSubject",
                table: "OutboundNotifications");

            migrationBuilder.DropColumn(
                name: "Line",
                table: "TrialPhoneRegistrations");

            migrationBuilder.DropColumn(
                name: "Line",
                table: "TrialGrants");

            migrationBuilder.DropColumn(
                name: "MaxHouses",
                table: "SubscriptionPlanConfigs");

            migrationBuilder.DropColumn(
                name: "StayBookingId",
                table: "StaffPushNotifications");

            migrationBuilder.DropColumn(
                name: "StayBookingId",
                table: "StaffMaxMessages");

            migrationBuilder.DropColumn(
                name: "StayBookingId",
                table: "OutboundNotifications");

            migrationBuilder.DropColumn(
                name: "StaffPosition",
                table: "CompanyMembers");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.CreateIndex(
                name: "UX_TrialPhoneRegistrations_Key",
                table: "TrialPhoneRegistrations",
                column: "PhoneKeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_TrialGrants_OnePerAccount",
                table: "TrialGrants",
                column: "BillingAccountId",
                unique: true,
                filter: "\"Source\" <> 2");

            // ARCHITECTURE_CYCLE37.md §37.5 / §37.2.3 — constraints EF cannot model. EX_HouseOccupancies_NoOverlap is the last, unbreakable line of
            // defence against a double booking: no two unreleased periods of one house share a night (23P01 is turned into 409 by HouseOccupancyWriter).
            migrationBuilder.Sql(
                """
                ALTER TABLE "HouseOccupancies" ADD CONSTRAINT "EX_HouseOccupancies_NoOverlap"
                    EXCLUDE USING gist ("HouseId" WITH =, daterange("StartDate", "EndDate", '[)') WITH &&)
                    WHERE ("ReleasedAtUtc" IS NULL);

                ALTER TABLE "HousePricePeriods" ADD CONSTRAINT "EX_HousePricePeriods_NoOverlap"
                    EXCLUDE USING gist ("HouseId" WITH =, daterange("StartDate", "EndDate", '[]') WITH &&)
                    WHERE ("StartDate" < "EndDate");
                """);

            // §37.11.1 — the city of the "Дома" vertical (idempotent by (Name, Region)); SearchName is CitySearch.Normalize("Шерегеш").
            migrationBuilder.Sql(
                """
                INSERT INTO "Cities" ("Name", "Region", "TimeZoneId", "IsActive", "SearchName")
                SELECT 'Шерегеш', 'Кемеровская область', 'Asia/Novokuznetsk', true, 'шерегеш'
                WHERE NOT EXISTS (SELECT 1 FROM "Cities" WHERE "Name" = 'Шерегеш' AND "Region" = 'Кемеровская область');
                """);

            // §37.10.1 — four tariffs of the "Дома" line (Line = 2) with FIXED ids (StaysPlans.*SeedId), editable by an administrator without a deploy,
            // and the option rule that lets accounts of paid plans buy the messenger number (only if that option exists).
            migrationBuilder.Sql(
                """
                INSERT INTO "SubscriptionPlanConfigs"
                    ("Id", "Name", "PricePerMonth", "MaxEmployees", "MaxCompanies",
                     "AllowOnlineBooking", "AllowMailing", "AllowAnalytics", "AllowPublicListing",
                     "AllowOnlinePayment", "AllowNotificationChannel", "PhotoQuotaMb", "PhotoRetention",
                     "Description", "IsActive", "NotifyDaysBefore", "CreatedAt",
                     "Highlights", "IsPublic", "SortOrder", "IsSystemFree", "IsSystemTrial",
                     "Line", "MaxProductsPerShop", "MaxOrdersPerMonth", "AllowOrders", "MaxHouses")
                SELECT v.id, v.name, v.price, NULL, NULL,
                       false, false, false, false,
                       false, v.channel, 100, 0,
                       v.descr, true, 7, now() AT TIME ZONE 'utc',
                       NULL, v.pub, v.sort, false, false,
                       2, NULL, NULL, false, v.houses
                FROM (VALUES
                    ('0c37f0e5-6a3d-4a5e-9b1f-2d4c7e8a9b01'::uuid, 'Один дом',        200, true,  true,  1, 1,    'Линейка «Дома»: один опубликованный дом.'),
                    ('0c37f0e5-6a3d-4a5e-9b1f-2d4c7e8a9b02'::uuid, 'До 3 домов',      500, true,  true,  2, 3,    'Линейка «Дома»: до трёх опубликованных домов.'),
                    ('0c37f0e5-6a3d-4a5e-9b1f-2d4c7e8a9b03'::uuid, 'Без ограничения', 1000, true, true,  3, NULL, 'Линейка «Дома»: без ограничения числа домов.'),
                    ('0c37f0e5-6a3d-4a5e-9b1f-2d4c7e8a9b04'::uuid, 'Пробный период',  0,   false, false, 0, NULL, 'Пробный тариф линейки «Дома».')
                ) AS v(id, name, price, channel, pub, sort, houses, descr)
                WHERE NOT EXISTS (SELECT 1 FROM "SubscriptionPlanConfigs" p WHERE p."Id" = v.id);

                INSERT INTO "PlanOptionRules" ("Id", "PlanConfigId", "OptionId", "Availability", "IncludedQuantity")
                SELECT gen_random_uuid(), p."Id", o."Id", 2, NULL
                FROM "SubscriptionPlanConfigs" p
                JOIN "SubscriptionOptions" o ON o."Code" = 'notifications.whatsapp'
                WHERE p."Line" = 2 AND p."AllowNotificationChannel"
                  AND NOT EXISTS (SELECT 1 FROM "PlanOptionRules" r WHERE r."PlanConfigId" = p."Id" AND r."OptionId" = o."Id");
                """);
        }
    }
}
