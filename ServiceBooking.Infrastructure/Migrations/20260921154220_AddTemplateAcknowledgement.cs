using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateAcknowledgement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AcknowledgedAtUtc",
                table: "NotificationTemplateHistories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcknowledgedByUserId",
                table: "NotificationTemplateHistories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdMarkersHit",
                table: "NotificationTemplateHistories",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewBody",
                table: "NotificationTemplateHistories",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WarningVersion",
                table: "NotificationTemplateHistories",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // ARCHITECTURE_CYCLE5.md §44.6/§44.8 (M5): the CURRENT (latest) history row per
            // (CompanyId, Type) gets the text it should have carried all along — NotificationTemplate.Body
            // is what was actually confirmed most recently, but nothing before this migration ever wrote
            // it onto a history row. Every OLDER row keeps NewBody = '' (AddColumn's own default above) —
            // its "new" text at the time is already sitting one row later as THAT row's PreviousBody, so
            // nothing is lost, only the one field this migration can't retroactively know is left blank.
            migrationBuilder.Sql(
                """
                UPDATE "NotificationTemplateHistories" h
                   SET "NewBody" = t."Body"
                  FROM "NotificationTemplates" t
                 WHERE h."CompanyId" = t."CompanyId" AND h."Type" = t."Type"
                   AND h."ChangedAtUtc" = (
                       SELECT MAX(h2."ChangedAtUtc") FROM "NotificationTemplateHistories" h2
                        WHERE h2."CompanyId" = h."CompanyId" AND h2."Type" = h."Type"
                   );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcknowledgedAtUtc",
                table: "NotificationTemplateHistories");

            migrationBuilder.DropColumn(
                name: "AcknowledgedByUserId",
                table: "NotificationTemplateHistories");

            migrationBuilder.DropColumn(
                name: "AdMarkersHit",
                table: "NotificationTemplateHistories");

            migrationBuilder.DropColumn(
                name: "NewBody",
                table: "NotificationTemplateHistories");

            migrationBuilder.DropColumn(
                name: "WarningVersion",
                table: "NotificationTemplateHistories");
        }
    }
}
