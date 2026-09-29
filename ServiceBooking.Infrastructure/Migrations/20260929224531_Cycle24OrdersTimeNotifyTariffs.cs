using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Cycle24OrdersTimeNotifyTariffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPlanConfigs_IsSystemFree",
                table: "SubscriptionPlanConfigs");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CompanyId_BusinessDate_Number",
                table: "Orders");

            migrationBuilder.RenameColumn(
                name: "BusinessDate",
                table: "OrderDailyCounters",
                newName: "PickupDate");

            migrationBuilder.AddColumn<bool>(
                name: "AllowOrders",
                table: "SubscriptionPlanConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "Line",
                table: "SubscriptionPlanConfigs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxOrdersPerMonth",
                table: "SubscriptionPlanConfigs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxProductsPerShop",
                table: "SubscriptionPlanConfigs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Line",
                table: "SubscriptionChangeLogs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "OrderId",
                table: "StaffPushNotifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AcceptanceChangedAtUtc",
                table: "ShopSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcceptanceChangedByName",
                table: "ShopSettings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcceptanceChangedByUserId",
                table: "ShopSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AsapEnabled",
                table: "ShopSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CustomerMessengerEnabled",
                table: "ShopSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CustomerWebPushEnabled",
                table: "ShopSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "MinPrepMinutes",
                table: "ShopSettings",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<bool>(
                name: "OrdersStopped",
                table: "ShopSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PausedUntilUtc",
                table: "ShopSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreorderDays",
                table: "ShopSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ScheduledEnabled",
                table: "ShopSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SlotStepMinutes",
                table: "ShopSettings",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<string>(
                name: "WorkingHoursJson",
                table: "ShopSettings",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Site",
                table: "PushSubscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AvailableWeekdaysMask",
                table: "Products",
                type: "integer",
                nullable: false,
                defaultValue: 127);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SoldOutForDate",
                table: "Products",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrderId",
                table: "OutboundNotifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MessengerConsentAtUtc",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MessengerConsentVersion",
                table: "Orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyByMessenger",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // ARCHITECTURE_CYCLE24.md §448.1 — added NULLABLE, backfilled, THEN made NOT NULL (no placeholder default is left behind).
            migrationBuilder.AddColumn<DateOnly>(
                name: "PickupDate",
                table: "Orders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PickupEndUtc",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PickupKind",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PickupStartUtc",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            // Every order that exists was picked up "as soon as possible" on the day it was created: the pickup day is the creation day
            // (the number sequence keeps working — OrderDailyCounters was renamed, not rewritten) and the start is the creation moment.
            migrationBuilder.Sql(
                """
                UPDATE "Orders" SET "PickupDate" = "BusinessDate", "PickupStartUtc" = "CreatedAtUtc";
                """);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "PickupDate",
                table: "Orders",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "PickupStartUtc",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestedLine",
                table: "BillingAccounts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrderMonthlyUsages",
                columns: table => new
                {
                    BillingAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    Warned80AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Warned100AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderMonthlyUsages", x => new { x.BillingAccountId, x.Month });
                    table.ForeignKey(
                        name: "FK_OrderMonthlyUsages_BillingAccounts_BillingAccountId",
                        column: x => x.BillingAccountId,
                        principalTable: "BillingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderPushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_OrderPushSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderPushSubscriptions_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrdersSubscriptions",
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
                    table.PrimaryKey("PK_OrdersSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrdersSubscriptions_BillingAccounts_BillingAccountId",
                        column: x => x.BillingAccountId,
                        principalTable: "BillingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrdersSubscriptions_SubscriptionPlanConfigs_PlanConfigId",
                        column: x => x.PlanConfigId,
                        principalTable: "SubscriptionPlanConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ShopDailyMenus",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopDailyMenus", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShopDailyMenus_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShopSpecialDays",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                    IntervalsJson = table.Column<string>(type: "jsonb", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopSpecialDays", x => new { x.CompanyId, x.Date });
                    table.ForeignKey(
                        name: "FK_ShopSpecialDays_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerOrderPushNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_CustomerOrderPushNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerOrderPushNotifications_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerOrderPushNotifications_OrderPushSubscriptions_Subsc~",
                        column: x => x.SubscriptionId,
                        principalTable: "OrderPushSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CustomerOrderPushNotifications_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ShopDailyMenuItems",
                columns: table => new
                {
                    DailyMenuId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopDailyMenuItems", x => new { x.DailyMenuId, x.ProductId });
                    table.ForeignKey(
                        name: "FK_ShopDailyMenuItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShopDailyMenuItems_ShopDailyMenus_DailyMenuId",
                        column: x => x.DailyMenuId,
                        principalTable: "ShopDailyMenus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlanConfigs_Line_SystemFree",
                table: "SubscriptionPlanConfigs",
                column: "Line",
                unique: true,
                filter: "\"IsSystemFree\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_StaffPushNotifications_OrderId",
                table: "StaffPushNotifications",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_UserId_Site",
                table: "PushSubscriptions",
                columns: new[] { "UserId", "Site" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_AvailableWeekdaysMask_Range",
                table: "Products",
                sql: "\"AvailableWeekdaysMask\" BETWEEN 0 AND 127");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundNotifications_OrderId",
                table: "OutboundNotifications",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CompanyId_BusinessDate",
                table: "Orders",
                columns: new[] { "CompanyId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CompanyId_PickupDate_Number",
                table: "Orders",
                columns: new[] { "CompanyId", "PickupDate", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CompanyId_PickupDate_PickupStartUtc",
                table: "Orders",
                columns: new[] { "CompanyId", "PickupDate", "PickupStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerOrderPushNotifications_CompanyId",
                table: "CustomerOrderPushNotifications",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerOrderPushNotifications_Dispatch",
                table: "CustomerOrderPushNotifications",
                columns: new[] { "ExpiresAtUtc", "CreatedAt" },
                filter: "\"Status\" = 0")
                .Annotation("Npgsql:IndexInclude", new[] { "CompanyId", "SubscriptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerOrderPushNotifications_IdempotencyKey",
                table: "CustomerOrderPushNotifications",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerOrderPushNotifications_OrderId",
                table: "CustomerOrderPushNotifications",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerOrderPushNotifications_SubscriptionId",
                table: "CustomerOrderPushNotifications",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderPushSubscriptions_CreatedAtUtc",
                table: "OrderPushSubscriptions",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OrderPushSubscriptions_OrderId_Endpoint",
                table: "OrderPushSubscriptions",
                columns: new[] { "OrderId", "Endpoint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrdersSubscriptions_BillingAccountId",
                table: "OrdersSubscriptions",
                column: "BillingAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrdersSubscriptions_PlanConfigId",
                table: "OrdersSubscriptions",
                column: "PlanConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_ShopDailyMenuItems_ProductId",
                table: "ShopDailyMenuItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ShopDailyMenus_CompanyId_Date",
                table: "ShopDailyMenus",
                columns: new[] { "CompanyId", "Date" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OutboundNotifications_Orders_OrderId",
                table: "OutboundNotifications",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_StaffPushNotifications_Orders_OrderId",
                table: "StaffPushNotifications",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ARCHITECTURE_CYCLE24.md §448.3 — the system free tariff of the "Заказы" line (Line = 1) with a FIXED id (OrdersFreePlan.SeedId), and the
            // option rule that lets its accounts buy the WhatsApp/MAX number (only if that option exists: a clean database has none, and that is not an
            // error). The numbers are the customer's (US-24-25) and editable by an administrator without a deploy.
            migrationBuilder.Sql(
                """
                INSERT INTO "SubscriptionPlanConfigs"
                    ("Id", "Name", "PricePerMonth", "MaxEmployees", "MaxCompanies",
                     "AllowOnlineBooking", "AllowMailing", "AllowAnalytics", "AllowPublicListing",
                     "AllowOnlinePayment", "AllowNotificationChannel", "PhotoQuotaMb", "PhotoRetention",
                     "Description", "IsActive", "NotifyDaysBefore", "CreatedAt",
                     "Highlights", "IsPublic", "SortOrder", "IsSystemFree",
                     "Line", "MaxProductsPerShop", "MaxOrdersPerMonth", "AllowOrders")
                SELECT
                    '0c24f0e5-6a3d-4a5e-9b1f-2d4c7e8a9b01'::uuid, 'Заказы · Бесплатно', 0, 2, 1,
                    false, false, false, false,
                    false, true, 100, 0,
                    'Бесплатный уровень линейки «Заказы».', true, 7, now() AT TIME ZONE 'utc',
                    NULL, false, -1, true,
                    1, 50, 150, true
                WHERE NOT EXISTS (SELECT 1 FROM "SubscriptionPlanConfigs" WHERE "IsSystemFree" = true AND "Line" = 1);

                INSERT INTO "PlanOptionRules" ("Id", "PlanConfigId", "OptionId", "Availability", "IncludedQuantity")
                SELECT gen_random_uuid(), p."Id", o."Id", 2, NULL
                FROM "SubscriptionPlanConfigs" p
                JOIN "SubscriptionOptions" o ON o."Code" = 'notifications.whatsapp'
                WHERE p."IsSystemFree" = true AND p."Line" = 1
                  AND NOT EXISTS (SELECT 1 FROM "PlanOptionRules" r WHERE r."PlanConfigId" = p."Id" AND r."OptionId" = o."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // WARNING (ARCHITECTURE_CYCLE24.md §448.3, DEPLOY.md §22): the old unique index (CompanyId, BusinessDate, Number) cannot be re-created once
            // pre-orders with colliding numbers exist — check for duplicates by hand before rolling back.
            migrationBuilder.Sql(
                """
                DELETE FROM "PlanOptionRules" WHERE "PlanConfigId" IN (SELECT "Id" FROM "SubscriptionPlanConfigs" WHERE "IsSystemFree" = true AND "Line" = 1);
                DELETE FROM "SubscriptionPlanConfigs" WHERE "IsSystemFree" = true AND "Line" = 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_OutboundNotifications_Orders_OrderId",
                table: "OutboundNotifications");

            migrationBuilder.DropForeignKey(
                name: "FK_StaffPushNotifications_Orders_OrderId",
                table: "StaffPushNotifications");

            migrationBuilder.DropTable(
                name: "CustomerOrderPushNotifications");

            migrationBuilder.DropTable(
                name: "OrderMonthlyUsages");

            migrationBuilder.DropTable(
                name: "OrdersSubscriptions");

            migrationBuilder.DropTable(
                name: "ShopDailyMenuItems");

            migrationBuilder.DropTable(
                name: "ShopSpecialDays");

            migrationBuilder.DropTable(
                name: "OrderPushSubscriptions");

            migrationBuilder.DropTable(
                name: "ShopDailyMenus");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPlanConfigs_Line_SystemFree",
                table: "SubscriptionPlanConfigs");

            migrationBuilder.DropIndex(
                name: "IX_StaffPushNotifications_OrderId",
                table: "StaffPushNotifications");

            migrationBuilder.DropIndex(
                name: "IX_PushSubscriptions_UserId_Site",
                table: "PushSubscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_AvailableWeekdaysMask_Range",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_OutboundNotifications_OrderId",
                table: "OutboundNotifications");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CompanyId_BusinessDate",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CompanyId_PickupDate_Number",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CompanyId_PickupDate_PickupStartUtc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AllowOrders",
                table: "SubscriptionPlanConfigs");

            migrationBuilder.DropColumn(
                name: "Line",
                table: "SubscriptionPlanConfigs");

            migrationBuilder.DropColumn(
                name: "MaxOrdersPerMonth",
                table: "SubscriptionPlanConfigs");

            migrationBuilder.DropColumn(
                name: "MaxProductsPerShop",
                table: "SubscriptionPlanConfigs");

            migrationBuilder.DropColumn(
                name: "Line",
                table: "SubscriptionChangeLogs");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "StaffPushNotifications");

            migrationBuilder.DropColumn(
                name: "AcceptanceChangedAtUtc",
                table: "ShopSettings");

            migrationBuilder.DropColumn(
                name: "AcceptanceChangedByName",
                table: "ShopSettings");

            migrationBuilder.DropColumn(
                name: "AcceptanceChangedByUserId",
                table: "ShopSettings");

            migrationBuilder.DropColumn(
                name: "AsapEnabled",
                table: "ShopSettings");

            migrationBuilder.DropColumn(
                name: "CustomerMessengerEnabled",
                table: "ShopSettings");

            migrationBuilder.DropColumn(
                name: "CustomerWebPushEnabled",
                table: "ShopSettings");

            migrationBuilder.DropColumn(
                name: "MinPrepMinutes",
                table: "ShopSettings");

            migrationBuilder.DropColumn(
                name: "OrdersStopped",
                table: "ShopSettings");

            migrationBuilder.DropColumn(
                name: "PausedUntilUtc",
                table: "ShopSettings");

            migrationBuilder.DropColumn(
                name: "PreorderDays",
                table: "ShopSettings");

            migrationBuilder.DropColumn(
                name: "ScheduledEnabled",
                table: "ShopSettings");

            migrationBuilder.DropColumn(
                name: "SlotStepMinutes",
                table: "ShopSettings");

            migrationBuilder.DropColumn(
                name: "WorkingHoursJson",
                table: "ShopSettings");

            migrationBuilder.DropColumn(
                name: "Site",
                table: "PushSubscriptions");

            migrationBuilder.DropColumn(
                name: "AvailableWeekdaysMask",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SoldOutForDate",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "OutboundNotifications");

            migrationBuilder.DropColumn(
                name: "MessengerConsentAtUtc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "MessengerConsentVersion",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "NotifyByMessenger",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PickupDate",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PickupEndUtc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PickupKind",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PickupStartUtc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RequestedLine",
                table: "BillingAccounts");

            migrationBuilder.RenameColumn(
                name: "PickupDate",
                table: "OrderDailyCounters",
                newName: "BusinessDate");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlanConfigs_IsSystemFree",
                table: "SubscriptionPlanConfigs",
                column: "IsSystemFree",
                unique: true,
                filter: "\"IsSystemFree\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CompanyId_BusinessDate_Number",
                table: "Orders",
                columns: new[] { "CompanyId", "BusinessDate", "Number" },
                unique: true);
        }
    }
}
