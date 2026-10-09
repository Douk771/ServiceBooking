using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Cycle40ChannelOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AutoTestInstanceId",
                table: "NotificationChannels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastTestResult",
                table: "NotificationChannels",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastTestResultAtUtc",
                table: "NotificationChannels",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderServerCountry",
                table: "NotificationChannels",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MessengerConsentAtUtc",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MessengerConsentByUserId",
                table: "Bookings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MessengerConsentVersion",
                table: "Bookings",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyByMessenger",
                table: "Bookings",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChannelOptionChangeLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BillingAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    OldPaidUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NewPaidUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OldEndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NewEndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ChangedByUserId = table.Column<string>(type: "text", nullable: true),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelOptionChangeLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelOptionChangeLogs_BillingAccounts_BillingAccountId",
                        column: x => x.BillingAccountId,
                        principalTable: "BillingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationChannels_TestPending",
                table: "NotificationChannels",
                column: "Id",
                filter: "\"LastTestResult\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelOptionChangeLogs_BillingAccountId_OptionCode_Changed~",
                table: "ChannelOptionChangeLogs",
                columns: new[] { "BillingAccountId", "OptionCode", "ChangedAtUtc" });

            // ARCHITECTURE_CYCLE40.md §40.2.1: the notifications.max option row (additive only; PlanOptionRules and
            // AccountSubscriptionOptions are not touched). A copy of the WhatsApp row, SortOrder + 1 — EXCEPT THE PRICE, which is left NULL on purpose
            // (review of cycle 40, LEGAL_REVIEW_CYCLE40.md Т40-L-01): an option with a price is for sale as soon as it is open and the offer is published, and the offer
            // published today does not cover MAX. The price is set by the SuperAdmin once the new edition of the offer is published (DEPLOY.md §31); until then
            // MAX is neither sold nor granted by a trial.
            migrationBuilder.Sql("""
                INSERT INTO "SubscriptionOptions"
                    ("Id", "Code", "Name", "Description", "Kind", "CapabilityKey", "PricePerMonth",
                     "UnitName", "MaxQuantity", "UnitPriceText", "IsPublic", "IsActive", "SortOrder",
                     "CreatedAtUtc", "UpdatedAtUtc")
                SELECT 'c4000000-0000-4000-8000-000000000040'::uuid, 'notifications.max', 'MAX',
                       'Номер для уведомлений клиентам в MAX.', w."Kind", 'notifications.max', NULL,
                       w."UnitName", w."MaxQuantity", w."UnitPriceText", w."IsPublic", w."IsActive", w."SortOrder" + 1,
                       now() AT TIME ZONE 'utc', now() AT TIME ZONE 'utc'
                FROM "SubscriptionOptions" w
                WHERE w."Code" = 'notifications.whatsapp'
                  AND NOT EXISTS (SELECT 1 FROM "SubscriptionOptions" m WHERE m."Code" = 'notifications.max');

                INSERT INTO "SubscriptionOptions"
                    ("Id", "Code", "Name", "Description", "Kind", "CapabilityKey", "PricePerMonth",
                     "UnitName", "MaxQuantity", "UnitPriceText", "IsPublic", "IsActive", "SortOrder",
                     "CreatedAtUtc", "UpdatedAtUtc")
                SELECT 'c4000000-0000-4000-8000-000000000040'::uuid, 'notifications.max', 'MAX',
                       'Номер для уведомлений клиентам в MAX.', 1, 'notifications.max', NULL,
                       'номер', NULL, NULL, false, true,
                       COALESCE((SELECT MAX("SortOrder") FROM "SubscriptionOptions"), 2) + 1,
                       now() AT TIME ZONE 'utc', now() AT TIME ZONE 'utc'
                WHERE NOT EXISTS (SELECT 1 FROM "SubscriptionOptions" m WHERE m."Code" = 'notifications.max');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "AccountSubscriptionOptions" a
                               JOIN "SubscriptionOptions" o ON o."Id" = a."OptionId"
                               WHERE o."Code" = 'notifications.max') THEN
                        RAISE EXCEPTION 'Cycle40ChannelOptions down: notifications.max is in use by AccountSubscriptionOptions';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "ChannelOptionChangeLogs");

            migrationBuilder.DropIndex(
                name: "IX_NotificationChannels_TestPending",
                table: "NotificationChannels");

            migrationBuilder.DropColumn(
                name: "AutoTestInstanceId",
                table: "NotificationChannels");

            migrationBuilder.DropColumn(
                name: "LastTestResult",
                table: "NotificationChannels");

            migrationBuilder.DropColumn(
                name: "LastTestResultAtUtc",
                table: "NotificationChannels");

            migrationBuilder.DropColumn(
                name: "ProviderServerCountry",
                table: "NotificationChannels");

            migrationBuilder.DropColumn(
                name: "MessengerConsentAtUtc",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "MessengerConsentByUserId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "MessengerConsentVersion",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "NotifyByMessenger",
                table: "Bookings");

            migrationBuilder.Sql("""DELETE FROM "SubscriptionOptions" WHERE "Code" = 'notifications.max';""");
        }
    }
}
