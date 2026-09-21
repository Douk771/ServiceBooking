using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConsentJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsentRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    SubjectPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DocumentVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DocumentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: true),
                    Act = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    GrantedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RecordedByUserId = table.Column<string>(type: "text", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokeReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsentRecords_AspNetUsers_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ConsentRecords_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ConsentRecords_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsentRecords_CompanyId",
                table: "ConsentRecords",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentRecords_CurrentBySubject",
                table: "ConsentRecords",
                columns: new[] { "SubjectPhone", "CompanyId", "DocumentKey", "GrantedAtUtc" },
                descending: new[] { false, false, false, true },
                filter: "\"RevokedAtUtc\" IS NULL AND \"SubjectPhone\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentRecords_CurrentByUser",
                table: "ConsentRecords",
                columns: new[] { "UserId", "DocumentKey", "Purpose", "GrantedAtUtc" },
                descending: new[] { false, false, false, true },
                filter: "\"RevokedAtUtc\" IS NULL AND \"UserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentRecords_RecordedByUserId",
                table: "ConsentRecords",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentRecords_Retention",
                table: "ConsentRecords",
                column: "GrantedAtUtc");

            // ARCHITECTURE_CYCLE5.md §44.2 p.7 / §44.8 (M1): every existing UserConsents row is carried
            // forward as a journal entry rather than silently dropped, so dev/test databases that already
            // have data don't lose it. Values are NOT invented — Act is Accepted (the closest single
            // value cycle 3's "current state" row supported; it never distinguished Acknowledged from
            // Accepted), Source is Migrated (a value that is NEVER written by application code, only by
            // this migration — §44.2 p.7 and API_CONTRACT_CYCLE5.md §38.4), DocumentHash/IpAddress/
            // UserAgent are simply absent (that information never existed on UserConsent to begin with).
            // On production this INSERT...SELECT affects zero rows: the database is wiped before the
            // first real salon (§44.8), so UserConsents is empty by the time this migration ever runs
            // there — this path exists purely for dev/test databases where losing existing data would be
            // needless (memory note: ServiceBooking is not in production yet).
            migrationBuilder.Sql(
                """
                INSERT INTO "ConsentRecords"
                    ("Id", "UserId", "DocumentKey", "DocumentVersion", "DocumentHash", "Act", "Source", "GrantedAtUtc")
                SELECT
                    gen_random_uuid(),
                    "UserId",
                    CASE "DocumentType" WHEN 0 THEN 'Privacy' WHEN 1 THEN 'TermsClient' END,
                    "Version",
                    '',
                    1, -- ConsentAct.Accepted
                    9, -- ConsentSource.Migrated
                    "AcceptedAtUtc"
                FROM "UserConsents";
                """);

            migrationBuilder.DropTable(
                name: "UserConsents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsentRecords");

            migrationBuilder.CreateTable(
                name: "UserConsents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserConsents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserConsents_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserConsents_UserId_DocumentType",
                table: "UserConsents",
                columns: new[] { "UserId", "DocumentType" },
                unique: true);
        }
    }
}
