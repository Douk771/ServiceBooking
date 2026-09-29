using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Cycle20LegalClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReasonCode",
                table: "SubscriptionChangeLogs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReasonDetails",
                table: "SubscriptionChangeLogs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Channel",
                table: "SubjectRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RegisteredByUserId",
                table: "SubjectRequests",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FormId",
                table: "ConsentRecords",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevokedByUserId",
                table: "ConsentRecords",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConsentOperatorAddress",
                table: "BillingAccounts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConsentOperatorFullName",
                table: "BillingAccounts",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConsentOperatorInn",
                table: "BillingAccounts",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GuestDataGateEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Operation = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuestDataGateEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformNotices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    AudienceType = table.Column<int>(type: "integer", nullable: false),
                    AudiencePlanIds = table.Column<Guid[]>(type: "uuid[]", nullable: true),
                    TargetBillingAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    TemplateVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LinkUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AttachmentTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AttachmentHtml = table.Column<string>(type: "text", nullable: true),
                    AttachmentSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VisibleUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RevokeReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformNotices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformNoticeAcknowledgements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NoticeId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    BillingAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformNoticeAcknowledgements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformNoticeAcknowledgements_PlatformNotices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "PlatformNotices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GuestDataGateEvents_OccurredAtUtc",
                table: "GuestDataGateEvents",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_GuestDataGateEvents_UserId_OccurredAtUtc",
                table: "GuestDataGateEvents",
                columns: new[] { "UserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformNoticeAcknowledgements_NoticeUser",
                table: "PlatformNoticeAcknowledgements",
                columns: new[] { "NoticeId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformNotices_VisibleUntil",
                table: "PlatformNotices",
                column: "VisibleUntilUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuestDataGateEvents");

            migrationBuilder.DropTable(
                name: "PlatformNoticeAcknowledgements");

            migrationBuilder.DropTable(
                name: "PlatformNotices");

            migrationBuilder.DropColumn(
                name: "ReasonCode",
                table: "SubscriptionChangeLogs");

            migrationBuilder.DropColumn(
                name: "ReasonDetails",
                table: "SubscriptionChangeLogs");

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "SubjectRequests");

            migrationBuilder.DropColumn(
                name: "RegisteredByUserId",
                table: "SubjectRequests");

            migrationBuilder.DropColumn(
                name: "FormId",
                table: "ConsentRecords");

            migrationBuilder.DropColumn(
                name: "RevokedByUserId",
                table: "ConsentRecords");

            migrationBuilder.DropColumn(
                name: "ConsentOperatorAddress",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "ConsentOperatorFullName",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "ConsentOperatorInn",
                table: "BillingAccounts");
        }
    }
}
