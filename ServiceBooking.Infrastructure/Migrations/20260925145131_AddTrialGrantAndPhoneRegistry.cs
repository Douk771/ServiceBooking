using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrialGrantAndPhoneRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrialGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BillingAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanConfigId = table.Column<Guid>(type: "uuid", nullable: true),
                    GrantedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: false),
                    MailingWindowDays = table.Column<int>(type: "integer", nullable: false),
                    WarningThresholdsDays = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    GrantedByUserId = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TermsVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TermsTextSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TermsShownAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TermsAcknowledgedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrialGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrialGrants_BillingAccounts_BillingAccountId",
                        column: x => x.BillingAccountId,
                        principalTable: "BillingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrialPhoneRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneKeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RegisteredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    KeyId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrialPhoneRegistrations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrialGrants_GrantedAtUtc",
                table: "TrialGrants",
                column: "GrantedAtUtc");

            migrationBuilder.CreateIndex(
                name: "UX_TrialGrants_OnePerAccount",
                table: "TrialGrants",
                column: "BillingAccountId",
                unique: true,
                filter: "\"Source\" <> 2");

            migrationBuilder.CreateIndex(
                name: "IX_TrialPhoneRegistrations_KeyId",
                table: "TrialPhoneRegistrations",
                column: "KeyId");

            migrationBuilder.CreateIndex(
                name: "IX_TrialPhoneRegistrations_RegisteredAtUtc",
                table: "TrialPhoneRegistrations",
                column: "RegisteredAtUtc");

            migrationBuilder.CreateIndex(
                name: "UX_TrialPhoneRegistrations_Key",
                table: "TrialPhoneRegistrations",
                column: "PhoneKeyHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrialGrants");

            migrationBuilder.DropTable(
                name: "TrialPhoneRegistrations");
        }
    }
}
