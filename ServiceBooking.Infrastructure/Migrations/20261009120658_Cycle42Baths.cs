using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Cycle42Baths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxResources",
                table: "SubscriptionPlanConfigs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ServiceReminderHours",
                table: "StaysSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Capacity",
                table: "StayServices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GuestsCount",
                table: "StayServiceOrders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SessionReminderAtUtc",
                table: "StayServiceOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BathsSubscriptions",
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
                    table.PrimaryKey("PK_BathsSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BathsSubscriptions_BillingAccounts_BillingAccountId",
                        column: x => x.BillingAccountId,
                        principalTable: "BillingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BathsSubscriptions_SubscriptionPlanConfigs_PlanConfigId",
                        column: x => x.PlanConfigId,
                        principalTable: "SubscriptionPlanConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StayServiceItemConfirmations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    ItemNameSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MarkersHit = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NoticeKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NoticeVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ConfirmedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ConfirmedByNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StayServiceItemConfirmations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StayServiceItemConfirmations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StayServiceItemConfirmations_StayServiceItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "StayServiceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StayServiceItemConfirmations_StayServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "StayServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StaysSettings_ServiceReminderHours",
                table: "StaysSettings",
                sql: "\"ServiceReminderHours\" IS NULL OR \"ServiceReminderHours\" BETWEEN 1 AND 24");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StayServices_Capacity",
                table: "StayServices",
                sql: "\"Capacity\" IS NULL OR \"Capacity\" BETWEEN 1 AND 30");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StayServiceOrders_GuestsCount",
                table: "StayServiceOrders",
                sql: "\"GuestsCount\" IS NULL OR \"GuestsCount\" BETWEEN 1 AND 30");

            migrationBuilder.CreateIndex(
                name: "IX_BathsSubscriptions_BillingAccountId",
                table: "BathsSubscriptions",
                column: "BillingAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BathsSubscriptions_PlanConfigId",
                table: "BathsSubscriptions",
                column: "PlanConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_StayServiceItemConfirmations_CompanyId",
                table: "StayServiceItemConfirmations",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_StayServiceItemConfirmations_ItemId",
                table: "StayServiceItemConfirmations",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StayServiceItemConfirmations_ServiceId_ConfirmedAtUtc",
                table: "StayServiceItemConfirmations",
                columns: new[] { "ServiceId", "ConfirmedAtUtc" });

            // ARCHITECTURE_CYCLE42.md §42.2.5 — four tariffs of the «Бани» line (Line = 3) with FIXED ids (BathsPlans.*SeedId), editable by an administrator
            // without a deploy. DEVIATION from §42.2.5: IsSystemTrial = false for the trial plan too — IX_SubscriptionPlanConfigs_IsSystemTrial is unique across ALL lines
            // (one flagged row in the table, already taken by the «Записи» trial); the «Дома» trial of cycle 37 is seeded the same way and is found by its fixed id.
            // The whatsapp option rule (same availability as the paid «Дома» plans, trial included, Q-L42-4). Idempotent.
            migrationBuilder.Sql(
                """
                INSERT INTO "SubscriptionPlanConfigs"
                    ("Id", "Name", "PricePerMonth", "MaxEmployees", "MaxCompanies",
                     "AllowOnlineBooking", "AllowMailing", "AllowAnalytics", "AllowPublicListing",
                     "AllowOnlinePayment", "AllowNotificationChannel", "PhotoQuotaMb", "PhotoRetention",
                     "Description", "IsActive", "NotifyDaysBefore", "CreatedAt",
                     "Highlights", "IsPublic", "SortOrder", "IsSystemFree", "IsSystemTrial",
                     "Line", "MaxProductsPerShop", "MaxOrdersPerMonth", "AllowOrders", "MaxHouses", "MaxResources")
                SELECT v.id, v.name, v.price, NULL, NULL,
                       false, false, false, false,
                       false, true, 100, 0,
                       v.descr, true, 7, now() AT TIME ZONE 'utc',
                       NULL, false, v.sort, false, false,
                       3, NULL, NULL, false, NULL, v.res
                FROM (VALUES
                    ('0c42ba70-6a3d-4a5e-9b1f-2d4c7e8a9b01'::uuid, 'Одна баня',        200,  1, 1,    false, 'Линейка «Бани»: одна опубликованная баня.'),
                    ('0c42ba70-6a3d-4a5e-9b1f-2d4c7e8a9b02'::uuid, 'До 3 бань',        500,  2, 3,    false, 'Линейка «Бани»: до трёх опубликованных бань.'),
                    ('0c42ba70-6a3d-4a5e-9b1f-2d4c7e8a9b03'::uuid, 'Без ограничения', 1000,  3, NULL, false, 'Линейка «Бани»: без ограничения числа бань.'),
                    ('0c42ba70-6a3d-4a5e-9b1f-2d4c7e8a9b04'::uuid, 'Пробный период «Бани»', 0, 0, NULL, true, 'Пробный тариф линейки «Бани».')
                ) AS v(id, name, price, sort, res, trial, descr)
                WHERE NOT EXISTS (SELECT 1 FROM "SubscriptionPlanConfigs" p WHERE p."Id" = v.id);

                INSERT INTO "PlanOptionRules" ("Id", "PlanConfigId", "OptionId", "Availability", "IncludedQuantity")
                SELECT gen_random_uuid(), p."Id", o."Id", 2, NULL
                FROM "SubscriptionPlanConfigs" p
                JOIN "SubscriptionOptions" o ON o."Code" = 'notifications.whatsapp'
                WHERE p."Line" = 3 AND p."AllowNotificationChannel"
                  AND NOT EXISTS (SELECT 1 FROM "PlanOptionRules" r WHERE r."PlanConfigId" = p."Id" AND r."OptionId" = o."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // §42.2.6 — data of the line 3 goes first. Companies Kind = 3, devices Site = 3, journal rows with Line = 3 and their bookings are NOT touched
            // (as C37-6): roll back only with a pg_dump and after those companies were removed by hand (DEPLOY.md §31).
            migrationBuilder.Sql(
                """
                DELETE FROM "TrialGrants" WHERE "Line" = 3;
                DELETE FROM "TrialPhoneRegistrations" WHERE "Line" = 3;
                DELETE FROM "PlanOptionRules" WHERE "PlanConfigId" IN (SELECT "Id" FROM "SubscriptionPlanConfigs" WHERE "Id"::text LIKE '0c42ba70-%');
                DELETE FROM "SubscriptionPlanConfigs" WHERE "Id"::text LIKE '0c42ba70-%';
                """);

            migrationBuilder.DropTable(
                name: "BathsSubscriptions");

            migrationBuilder.DropTable(
                name: "StayServiceItemConfirmations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StaysSettings_ServiceReminderHours",
                table: "StaysSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StayServices_Capacity",
                table: "StayServices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StayServiceOrders_GuestsCount",
                table: "StayServiceOrders");

            migrationBuilder.DropColumn(
                name: "MaxResources",
                table: "SubscriptionPlanConfigs");

            migrationBuilder.DropColumn(
                name: "ServiceReminderHours",
                table: "StaysSettings");

            migrationBuilder.DropColumn(
                name: "Capacity",
                table: "StayServices");

            migrationBuilder.DropColumn(
                name: "GuestsCount",
                table: "StayServiceOrders");

            migrationBuilder.DropColumn(
                name: "SessionReminderAtUtc",
                table: "StayServiceOrders");
        }
    }
}
