using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BillingAccountId",
                table: "Companies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BillingAccountId",
                table: "AccountSubscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BillingAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    GrandfatheredEmployeeBonus = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingAccounts_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompanyOwnerChangeLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    OldOwnerUserId = table.Column<string>(type: "text", nullable: false),
                    NewOwnerUserId = table.Column<string>(type: "text", nullable: false),
                    ChangedByUserId = table.Column<string>(type: "text", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    WithTransfer = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyOwnerChangeLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyOwnerChangeLogs_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Companies_BillingAccountId",
                table: "Companies",
                column: "BillingAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountSubscriptions_BillingAccountId",
                table: "AccountSubscriptions",
                column: "BillingAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingAccounts_OwnerUserId",
                table: "BillingAccounts",
                column: "OwnerUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyOwnerChangeLogs_CompanyId",
                table: "CompanyOwnerChangeLogs",
                column: "CompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountSubscriptions_BillingAccounts_BillingAccountId",
                table: "AccountSubscriptions",
                column: "BillingAccountId",
                principalTable: "BillingAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_BillingAccounts_BillingAccountId",
                table: "Companies",
                column: "BillingAccountId",
                principalTable: "BillingAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccountSubscriptions_BillingAccounts_BillingAccountId",
                table: "AccountSubscriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_Companies_BillingAccounts_BillingAccountId",
                table: "Companies");

            migrationBuilder.DropTable(
                name: "BillingAccounts");

            migrationBuilder.DropTable(
                name: "CompanyOwnerChangeLogs");

            migrationBuilder.DropIndex(
                name: "IX_Companies_BillingAccountId",
                table: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_AccountSubscriptions_BillingAccountId",
                table: "AccountSubscriptions");

            migrationBuilder.DropColumn(
                name: "BillingAccountId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "BillingAccountId",
                table: "AccountSubscriptions");
        }
    }
}
