using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Cycle39StaysServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StaffPushNotifications_OneSubject",
                table: "StaffPushNotifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboundNotifications_OneSubject",
                table: "OutboundNotifications");

            migrationBuilder.AddColumn<bool>(
                name: "AcceptServiceOrdersWithoutStay",
                table: "StaysSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ArrivalReminderPushText",
                table: "StaysSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ArrivalReminderTemplate",
                table: "StaysSettings",
                type: "character varying(700)",
                maxLength: 700,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "ArrivalReminderTime",
                table: "StaysSettings",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(18, 0, 0));

            migrationBuilder.AlterColumn<Guid>(
                name: "StayBookingId",
                table: "StayPaymentProofs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "StayServiceOrderId",
                table: "StayPaymentProofs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "StayBookingId",
                table: "StayGuestPushSubscriptions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "StayServiceOrderId",
                table: "StayGuestPushSubscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StayServiceOrderId",
                table: "StayGuestPushNotifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArrivalReminderPageText",
                table: "StayBookings",
                type: "character varying(1200)",
                maxLength: 1200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArrivalReminderSentAtUtc",
                table: "StayBookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ServiceSessionId",
                table: "StayBookingEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ServiceSessionId",
                table: "StayBookingCharges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StayServiceOrderId",
                table: "StaffPushNotifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StayServiceOrderId",
                table: "StaffMaxMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StayServiceOrderId",
                table: "OutboundNotifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StayServices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    MinHours = table.Column<int>(type: "integer", nullable: false),
                    MaxHours = table.Column<int>(type: "integer", nullable: false),
                    StepMinutes = table.Column<int>(type: "integer", nullable: false),
                    BufferMinutes = table.Column<int>(type: "integer", nullable: false),
                    ShowBufferToGuests = table.Column<bool>(type: "boolean", nullable: false),
                    MinLeadMinutes = table.Column<int>(type: "integer", nullable: false),
                    StandalonePrepayPercent = table.Column<int>(type: "integer", nullable: true),
                    CancellationPolicy = table.Column<int>(type: "integer", nullable: false),
                    CancellationBoundaryHours = table.Column<int>(type: "integer", nullable: false),
                    AvailableForHouseBookings = table.Column<bool>(type: "boolean", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StayServices", x => x.Id);
                    table.CheckConstraint("CK_StayServices_Boundary", "\"CancellationBoundaryHours\" BETWEEN 1 AND 24");
                    table.CheckConstraint("CK_StayServices_Buffer", "\"BufferMinutes\" BETWEEN 0 AND 240 AND \"BufferMinutes\" % 15 = 0");
                    table.CheckConstraint("CK_StayServices_Hours", "1 <= \"MinHours\" AND \"MinHours\" <= \"MaxHours\" AND \"MaxHours\" <= 12");
                    table.CheckConstraint("CK_StayServices_Lead", "\"MinLeadMinutes\" BETWEEN 0 AND 2880 AND \"MinLeadMinutes\" % 30 = 0");
                    table.CheckConstraint("CK_StayServices_Prepay", "\"StandalonePrepayPercent\" IS NULL OR \"StandalonePrepayPercent\" BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_StayServices_PublishedNotArchived", "NOT (\"IsPublished\" AND \"ArchivedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_StayServices_Step", "\"StepMinutes\" IN (30, 60)");
                    table.ForeignKey(
                        name: "FK_StayServices_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaysReminderTemplateChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ChangedByNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PreviousTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    NewTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    PreviousTemplate = table.Column<string>(type: "character varying(700)", maxLength: 700, nullable: true),
                    NewTemplate = table.Column<string>(type: "character varying(700)", maxLength: 700, nullable: true),
                    PreviousPushText = table.Column<bool>(type: "boolean", nullable: false),
                    NewPushText = table.Column<bool>(type: "boolean", nullable: false),
                    OwnerNoticeVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PushNoticeVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CodeMarkersConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    CodeMarkersHit = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaysReminderTemplateChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaysReminderTemplateChanges_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StayServiceDateOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                    WindowsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Comment = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StayServiceDateOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StayServiceDateOverrides_StayServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "StayServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StayServiceItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PriceRub = table.Column<int>(type: "integer", nullable: false),
                    MaxPerSession = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StayServiceItems", x => x.Id);
                    table.CheckConstraint("CK_StayServiceItems_Max", "\"MaxPerSession\" BETWEEN 1 AND 50");
                    table.CheckConstraint("CK_StayServiceItems_Price", "\"PriceRub\" BETWEEN 0 AND 100000");
                    table.ForeignKey(
                        name: "FK_StayServiceItems_StayServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "StayServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StayServiceOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsManual = table.Column<bool>(type: "boolean", nullable: false),
                    HoldExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TerminalAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StatusReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    GuestKind = table.Column<int>(type: "integer", nullable: false),
                    GuestUserId = table.Column<string>(type: "text", nullable: true),
                    GuestName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    GuestPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestBasis = table.Column<int>(type: "integer", nullable: true),
                    ServiceAmountRub = table.Column<int>(type: "integer", nullable: false),
                    ItemsAmountRub = table.Column<int>(type: "integer", nullable: false),
                    TotalRub = table.Column<int>(type: "integer", nullable: false),
                    PrepayPercentSnapshot = table.Column<int>(type: "integer", nullable: false),
                    PrepayRub = table.Column<int>(type: "integer", nullable: false),
                    DueOnSiteRub = table.Column<int>(type: "integer", nullable: false),
                    CancellationPolicySnapshot = table.Column<int>(type: "integer", nullable: false),
                    CancellationBoundaryHoursSnapshot = table.Column<int>(type: "integer", nullable: false),
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
                    PaymentConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaymentConfirmedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    PaymentConfirmedByNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PaymentProofsPurgedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HoldReminderQueuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PersonalDataErased = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StayServiceOrders", x => x.Id);
                    table.CheckConstraint("CK_StayServiceOrders_Money", "\"TotalRub\" = \"ServiceAmountRub\" + \"ItemsAmountRub\" AND \"DueOnSiteRub\" = \"TotalRub\" - \"PrepayRub\"");
                    table.ForeignKey(
                        name: "FK_StayServiceOrders_AspNetUsers_GuestUserId",
                        column: x => x.GuestUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StayServiceOrders_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StayServiceOrders_StayServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "StayServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StayServicePhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StayServicePhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StayServicePhotos_StayServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "StayServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StayServicePriceRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DaysMask = table.Column<int>(type: "integer", nullable: false),
                    FromHour = table.Column<int>(type: "integer", nullable: false),
                    ToHour = table.Column<int>(type: "integer", nullable: false),
                    PriceRub = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StayServicePriceRules", x => x.Id);
                    table.CheckConstraint("CK_StayServicePriceRules_Days", "\"DaysMask\" BETWEEN 1 AND 127");
                    table.CheckConstraint("CK_StayServicePriceRules_Hours", "\"FromHour\" BETWEEN 6 AND 29 AND \"ToHour\" > \"FromHour\" AND \"ToHour\" <= 30");
                    table.CheckConstraint("CK_StayServicePriceRules_Price", "\"PriceRub\" BETWEEN 1 AND 100000");
                    table.ForeignKey(
                        name: "FK_StayServicePriceRules_StayServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "StayServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StayServiceScheduleEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ActorNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: false),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StayServiceScheduleEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StayServiceScheduleEvents_StayServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "StayServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StayServiceWeeklyWindows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartMinute = table.Column<int>(type: "integer", nullable: false),
                    EndMinute = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StayServiceWeeklyWindows", x => x.Id);
                    table.CheckConstraint("CK_StayServiceWeeklyWindows_Day", "\"DayOfWeek\" BETWEEN 1 AND 7");
                    table.CheckConstraint("CK_StayServiceWeeklyWindows_Minutes", "\"StartMinute\" >= 0 AND \"EndMinute\" > \"StartMinute\" AND \"EndMinute\" <= 2880");
                    table.ForeignKey(
                        name: "FK_StayServiceWeeklyWindows_StayServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "StayServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StayServiceOrderEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StayServiceOrderId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_StayServiceOrderEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StayServiceOrderEvents_StayServiceOrders_StayServiceOrderId",
                        column: x => x.StayServiceOrderId,
                        principalTable: "StayServiceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StayServiceSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StayBookingId = table.Column<Guid>(type: "uuid", nullable: true),
                    StayServiceOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartMinute = table.Column<int>(type: "integer", nullable: false),
                    Hours = table.Column<int>(type: "integer", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BufferMinutesSnapshot = table.Column<int>(type: "integer", nullable: false),
                    OccupiedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ReleasedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ServiceNameSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    HourPricesJson = table.Column<string>(type: "jsonb", nullable: false),
                    ItemsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ServiceAmountRub = table.Column<int>(type: "integer", nullable: false),
                    ItemsAmountRub = table.Column<int>(type: "integer", nullable: false),
                    TotalRub = table.Column<int>(type: "integer", nullable: false),
                    AddedByKind = table.Column<int>(type: "integer", nullable: false),
                    AddedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    AddedByNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RequestBasis = table.Column<int>(type: "integer", nullable: true),
                    AddNoticeVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StayServiceSessions", x => x.Id);
                    table.CheckConstraint("CK_StayServiceSessions_Hours", "\"Hours\" BETWEEN 1 AND 12");
                    table.CheckConstraint("CK_StayServiceSessions_OneParent", "num_nonnulls(\"StayBookingId\", \"StayServiceOrderId\") = 1");
                    table.CheckConstraint("CK_StayServiceSessions_Released", "(\"ReleasedAtUtc\" IS NULL) = (\"State\" = 0)");
                    table.CheckConstraint("CK_StayServiceSessions_RequestBasis", "(\"AddedByKind\" IN (2, 3)) = (\"RequestBasis\" IS NOT NULL)");
                    table.CheckConstraint("CK_StayServiceSessions_Times", "\"EndUtc\" > \"StartUtc\" AND \"OccupiedUntilUtc\" >= \"EndUtc\"");
                    table.ForeignKey(
                        name: "FK_StayServiceSessions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StayServiceSessions_StayBookings_StayBookingId",
                        column: x => x.StayBookingId,
                        principalTable: "StayBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StayServiceSessions_StayServiceOrders_StayServiceOrderId",
                        column: x => x.StayServiceOrderId,
                        principalTable: "StayServiceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StayServiceSessions_StayServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "StayServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StayPaymentProofs_StayServiceOrderId",
                table: "StayPaymentProofs",
                column: "StayServiceOrderId",
                filter: "\"StayServiceOrderId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StayPaymentProofs_OneOwner",
                table: "StayPaymentProofs",
                sql: "num_nonnulls(\"StayBookingId\", \"StayServiceOrderId\") = 1");

            migrationBuilder.CreateIndex(
                name: "UX_StayGuestPushSubscriptions_Order_Endpoint",
                table: "StayGuestPushSubscriptions",
                columns: new[] { "StayServiceOrderId", "Endpoint" },
                unique: true,
                filter: "\"StayServiceOrderId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StayGuestPushSubscriptions_OneOwner",
                table: "StayGuestPushSubscriptions",
                sql: "num_nonnulls(\"StayBookingId\", \"StayServiceOrderId\") = 1");

            migrationBuilder.CreateIndex(
                name: "IX_StayGuestPushNotifications_StayServiceOrderId",
                table: "StayGuestPushNotifications",
                column: "StayServiceOrderId",
                filter: "\"StayServiceOrderId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StayGuestPushNotifications_OneOwner",
                table: "StayGuestPushNotifications",
                sql: "num_nonnulls(\"StayBookingId\", \"StayServiceOrderId\") <= 1");

            migrationBuilder.CreateIndex(
                name: "IX_StayBookingEvents_ServiceSessionId",
                table: "StayBookingEvents",
                column: "ServiceSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_StayBookingCharges_ServiceSessionId",
                table: "StayBookingCharges",
                column: "ServiceSessionId",
                filter: "\"ServiceSessionId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StayBookingCharges_ServiceNotPrepaid",
                table: "StayBookingCharges",
                sql: "NOT (\"Kind\" IN (5, 6) AND \"PrepayEligible\")");

            migrationBuilder.CreateIndex(
                name: "IX_StaffPushNotifications_StayServiceOrderId",
                table: "StaffPushNotifications",
                column: "StayServiceOrderId",
                filter: "\"StayServiceOrderId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StaffPushNotifications_OneSubject",
                table: "StaffPushNotifications",
                sql: "num_nonnulls(\"BookingId\", \"OrderId\", \"StayBookingId\", \"StayServiceOrderId\") <= 1");

            migrationBuilder.CreateIndex(
                name: "IX_StaffMaxMessages_StayServiceOrderId",
                table: "StaffMaxMessages",
                column: "StayServiceOrderId",
                filter: "\"StayServiceOrderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundNotifications_StayServiceOrderId",
                table: "OutboundNotifications",
                column: "StayServiceOrderId",
                filter: "\"StayServiceOrderId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboundNotifications_OneSubject",
                table: "OutboundNotifications",
                sql: "num_nonnulls(\"BookingId\", \"OrderId\", \"StayBookingId\", \"StayServiceOrderId\") <= 1");

            migrationBuilder.CreateIndex(
                name: "IX_StayServiceDateOverrides_ServiceId_BusinessDate",
                table: "StayServiceDateOverrides",
                columns: new[] { "ServiceId", "BusinessDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StayServiceItems_ServiceId_Position",
                table: "StayServiceItems",
                columns: new[] { "ServiceId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_StayServiceOrderEvents_OccurredAtUtc",
                table: "StayServiceOrderEvents",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StayServiceOrderEvents_StayServiceOrderId_OccurredAtUtc",
                table: "StayServiceOrderEvents",
                columns: new[] { "StayServiceOrderId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StayServiceOrders_CompanyId_IdempotencyKey",
                table: "StayServiceOrders",
                columns: new[] { "CompanyId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StayServiceOrders_CompanyId_Status",
                table: "StayServiceOrders",
                columns: new[] { "CompanyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StayServiceOrders_GuestUserId_CreatedAtUtc",
                table: "StayServiceOrders",
                columns: new[] { "GuestUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StayServiceOrders_HoldExpiry",
                table: "StayServiceOrders",
                column: "HoldExpiresAtUtc",
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_StayServiceOrders_Phone",
                table: "StayServiceOrders",
                columns: new[] { "GuestPhone", "CreatedAtUtc" },
                filter: "\"GuestPhone\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StayServiceOrders_PublicToken",
                table: "StayServiceOrders",
                column: "PublicToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StayServiceOrders_ServiceId",
                table: "StayServiceOrders",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_StayServicePhotos_ServiceId_Position",
                table: "StayServicePhotos",
                columns: new[] { "ServiceId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_StayServicePriceRules_ServiceId",
                table: "StayServicePriceRules",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_StayServices_CompanyId_Position",
                table: "StayServices",
                columns: new[] { "CompanyId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_StayServices_CompanyId_Slug",
                table: "StayServices",
                columns: new[] { "CompanyId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StayServiceScheduleEvents_OccurredAtUtc",
                table: "StayServiceScheduleEvents",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StayServiceScheduleEvents_ServiceId_OccurredAtUtc",
                table: "StayServiceScheduleEvents",
                columns: new[] { "ServiceId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StayServiceSessions_Active",
                table: "StayServiceSessions",
                columns: new[] { "ServiceId", "StartUtc" },
                filter: "\"ReleasedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StayServiceSessions_CompanyId_BusinessDate",
                table: "StayServiceSessions",
                columns: new[] { "CompanyId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StayServiceSessions_StayBookingId",
                table: "StayServiceSessions",
                column: "StayBookingId");

            migrationBuilder.CreateIndex(
                name: "UX_StayServiceSessions_Booking_Key",
                table: "StayServiceSessions",
                columns: new[] { "StayBookingId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_StayServiceSessions_Order",
                table: "StayServiceSessions",
                column: "StayServiceOrderId",
                unique: true,
                filter: "\"StayServiceOrderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StayServiceWeeklyWindows_ServiceId_DayOfWeek",
                table: "StayServiceWeeklyWindows",
                columns: new[] { "ServiceId", "DayOfWeek" });

            migrationBuilder.CreateIndex(
                name: "IX_StaysReminderTemplateChanges_CompanyId_ChangedAtUtc",
                table: "StaysReminderTemplateChanges",
                columns: new[] { "CompanyId", "ChangedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_OutboundNotifications_StayServiceOrders_StayServiceOrderId",
                table: "OutboundNotifications",
                column: "StayServiceOrderId",
                principalTable: "StayServiceOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_StaffMaxMessages_StayServiceOrders_StayServiceOrderId",
                table: "StaffMaxMessages",
                column: "StayServiceOrderId",
                principalTable: "StayServiceOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_StaffPushNotifications_StayServiceOrders_StayServiceOrderId",
                table: "StaffPushNotifications",
                column: "StayServiceOrderId",
                principalTable: "StayServiceOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_StayBookingCharges_StayServiceSessions_ServiceSessionId",
                table: "StayBookingCharges",
                column: "ServiceSessionId",
                principalTable: "StayServiceSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StayBookingEvents_StayServiceSessions_ServiceSessionId",
                table: "StayBookingEvents",
                column: "ServiceSessionId",
                principalTable: "StayServiceSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_StayGuestPushNotifications_StayServiceOrders_StayServiceOrd~",
                table: "StayGuestPushNotifications",
                column: "StayServiceOrderId",
                principalTable: "StayServiceOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_StayGuestPushSubscriptions_StayServiceOrders_StayServiceOrd~",
                table: "StayGuestPushSubscriptions",
                column: "StayServiceOrderId",
                principalTable: "StayServiceOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StayPaymentProofs_StayServiceOrders_StayServiceOrderId",
                table: "StayPaymentProofs",
                column: "StayServiceOrderId",
                principalTable: "StayServiceOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ARCHITECTURE_CYCLE39.md §39.5.1 / A39-3 — the last, unbreakable line of defence against a double booking of a service: no two unreleased sessions
            // of one service share a moment. REAL moments (timestamptz): midnight and the border of the business day do not exist for the constraint; the buffer
            // is part of the occupied range (OccupiedUntilUtc = EndUtc + buffer). btree_gist (since cycle 37) gives `=` on uuid inside gist.
            // 23P01 is turned into 409 SlotTaken by ServiceSessionWriter.
            migrationBuilder.Sql(
                """
                ALTER TABLE "StayServiceSessions" ADD CONSTRAINT "EX_StayServiceSessions_NoOverlap"
                    EXCLUDE USING gist ("ServiceId" WITH =, tstzrange("StartUtc", "OccupiedUntilUtc", '[)') WITH &&)
                    WHERE ("ReleasedAtUtc" IS NULL);
                """);

            // ARCHITECTURE_CYCLE39.md §39.14.1 / §39.2.5 — the second segment under a company address now also holds /uslugi/<serviceSlug> and the words reserved for
            // the calendars of cycle 40: a house that already took such an address gets a suffix (0 rows are expected on the stand).
            migrationBuilder.Sql(
                """
                UPDATE "Houses" h SET "Slug" = h."Slug" || '-dom'
                WHERE h."Slug" IN ('uslugi', 'services', 'bani', 'banya', 'kalendar', 'kalendari', 'calendar', 'ical')
                  AND NOT EXISTS (SELECT 1 FROM "Houses" o WHERE o."CompanyId" = h."CompanyId" AND o."Slug" = h."Slug" || '-dom');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // WARNING (DEPLOY.md §28, the same class of risk as C37-6): going back makes StayPaymentProofs.StayBookingId and StayGuestPushSubscriptions.StayBookingId
            // NOT NULL again — it FAILS if payment proofs or push subscriptions of stand-alone service orders already exist. Check by hand before rolling back;
            // the rows are NOT deleted silently (the files of the proofs would stay on the disk without an owner). The rename of the house addresses is not reverted.
            migrationBuilder.DropForeignKey(
                name: "FK_OutboundNotifications_StayServiceOrders_StayServiceOrderId",
                table: "OutboundNotifications");

            migrationBuilder.DropForeignKey(
                name: "FK_StaffMaxMessages_StayServiceOrders_StayServiceOrderId",
                table: "StaffMaxMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_StaffPushNotifications_StayServiceOrders_StayServiceOrderId",
                table: "StaffPushNotifications");

            migrationBuilder.DropForeignKey(
                name: "FK_StayBookingCharges_StayServiceSessions_ServiceSessionId",
                table: "StayBookingCharges");

            migrationBuilder.DropForeignKey(
                name: "FK_StayBookingEvents_StayServiceSessions_ServiceSessionId",
                table: "StayBookingEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_StayGuestPushNotifications_StayServiceOrders_StayServiceOrd~",
                table: "StayGuestPushNotifications");

            migrationBuilder.DropForeignKey(
                name: "FK_StayGuestPushSubscriptions_StayServiceOrders_StayServiceOrd~",
                table: "StayGuestPushSubscriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_StayPaymentProofs_StayServiceOrders_StayServiceOrderId",
                table: "StayPaymentProofs");

            migrationBuilder.DropTable(
                name: "StayServiceDateOverrides");

            migrationBuilder.DropTable(
                name: "StayServiceItems");

            migrationBuilder.DropTable(
                name: "StayServiceOrderEvents");

            migrationBuilder.DropTable(
                name: "StayServicePhotos");

            migrationBuilder.DropTable(
                name: "StayServicePriceRules");

            migrationBuilder.DropTable(
                name: "StayServiceScheduleEvents");

            migrationBuilder.DropTable(
                name: "StayServiceSessions");

            migrationBuilder.DropTable(
                name: "StayServiceWeeklyWindows");

            migrationBuilder.DropTable(
                name: "StaysReminderTemplateChanges");

            migrationBuilder.DropTable(
                name: "StayServiceOrders");

            migrationBuilder.DropTable(
                name: "StayServices");

            migrationBuilder.DropIndex(
                name: "IX_StayPaymentProofs_StayServiceOrderId",
                table: "StayPaymentProofs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StayPaymentProofs_OneOwner",
                table: "StayPaymentProofs");

            migrationBuilder.DropIndex(
                name: "UX_StayGuestPushSubscriptions_Order_Endpoint",
                table: "StayGuestPushSubscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StayGuestPushSubscriptions_OneOwner",
                table: "StayGuestPushSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_StayGuestPushNotifications_StayServiceOrderId",
                table: "StayGuestPushNotifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StayGuestPushNotifications_OneOwner",
                table: "StayGuestPushNotifications");

            migrationBuilder.DropIndex(
                name: "IX_StayBookingEvents_ServiceSessionId",
                table: "StayBookingEvents");

            migrationBuilder.DropIndex(
                name: "IX_StayBookingCharges_ServiceSessionId",
                table: "StayBookingCharges");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StayBookingCharges_ServiceNotPrepaid",
                table: "StayBookingCharges");

            migrationBuilder.DropIndex(
                name: "IX_StaffPushNotifications_StayServiceOrderId",
                table: "StaffPushNotifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StaffPushNotifications_OneSubject",
                table: "StaffPushNotifications");

            migrationBuilder.DropIndex(
                name: "IX_StaffMaxMessages_StayServiceOrderId",
                table: "StaffMaxMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutboundNotifications_StayServiceOrderId",
                table: "OutboundNotifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboundNotifications_OneSubject",
                table: "OutboundNotifications");

            migrationBuilder.DropColumn(
                name: "AcceptServiceOrdersWithoutStay",
                table: "StaysSettings");

            migrationBuilder.DropColumn(
                name: "ArrivalReminderPushText",
                table: "StaysSettings");

            migrationBuilder.DropColumn(
                name: "ArrivalReminderTemplate",
                table: "StaysSettings");

            migrationBuilder.DropColumn(
                name: "ArrivalReminderTime",
                table: "StaysSettings");

            migrationBuilder.DropColumn(
                name: "StayServiceOrderId",
                table: "StayPaymentProofs");

            migrationBuilder.DropColumn(
                name: "StayServiceOrderId",
                table: "StayGuestPushSubscriptions");

            migrationBuilder.DropColumn(
                name: "StayServiceOrderId",
                table: "StayGuestPushNotifications");

            migrationBuilder.DropColumn(
                name: "ArrivalReminderPageText",
                table: "StayBookings");

            migrationBuilder.DropColumn(
                name: "ArrivalReminderSentAtUtc",
                table: "StayBookings");

            migrationBuilder.DropColumn(
                name: "ServiceSessionId",
                table: "StayBookingEvents");

            migrationBuilder.DropColumn(
                name: "ServiceSessionId",
                table: "StayBookingCharges");

            migrationBuilder.DropColumn(
                name: "StayServiceOrderId",
                table: "StaffPushNotifications");

            migrationBuilder.DropColumn(
                name: "StayServiceOrderId",
                table: "StaffMaxMessages");

            migrationBuilder.DropColumn(
                name: "StayServiceOrderId",
                table: "OutboundNotifications");

            migrationBuilder.AlterColumn<Guid>(
                name: "StayBookingId",
                table: "StayPaymentProofs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "StayBookingId",
                table: "StayGuestPushSubscriptions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_StaffPushNotifications_OneSubject",
                table: "StaffPushNotifications",
                sql: "(CASE WHEN \"BookingId\" IS NULL THEN 0 ELSE 1 END) + (CASE WHEN \"OrderId\" IS NULL THEN 0 ELSE 1 END) + (CASE WHEN \"StayBookingId\" IS NULL THEN 0 ELSE 1 END) <= 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboundNotifications_OneSubject",
                table: "OutboundNotifications",
                sql: "(CASE WHEN \"BookingId\" IS NULL THEN 0 ELSE 1 END) + (CASE WHEN \"OrderId\" IS NULL THEN 0 ELSE 1 END) + (CASE WHEN \"StayBookingId\" IS NULL THEN 0 ELSE 1 END) <= 1");
        }
    }
}
