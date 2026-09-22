using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionChangeLogTransferColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BillingAccountId",
                table: "SubscriptionChangeLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ChangeKind",
                table: "SubscriptionChangeLogs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "SubscriptionChangeLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewOptionsSummary",
                table: "SubscriptionChangeLogs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OldOptionsSummary",
                table: "SubscriptionChangeLogs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionChangeLogs_BillingAccountId",
                table: "SubscriptionChangeLogs",
                column: "BillingAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionChangeLogs_CompanyId",
                table: "SubscriptionChangeLogs",
                column: "CompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionChangeLogs_BillingAccounts_BillingAccountId",
                table: "SubscriptionChangeLogs",
                column: "BillingAccountId",
                principalTable: "BillingAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionChangeLogs_Companies_CompanyId",
                table: "SubscriptionChangeLogs",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SubscriptionChangeLogs_BillingAccounts_BillingAccountId",
                table: "SubscriptionChangeLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_SubscriptionChangeLogs_Companies_CompanyId",
                table: "SubscriptionChangeLogs");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionChangeLogs_BillingAccountId",
                table: "SubscriptionChangeLogs");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionChangeLogs_CompanyId",
                table: "SubscriptionChangeLogs");

            migrationBuilder.DropColumn(
                name: "BillingAccountId",
                table: "SubscriptionChangeLogs");

            migrationBuilder.DropColumn(
                name: "ChangeKind",
                table: "SubscriptionChangeLogs");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "SubscriptionChangeLogs");

            migrationBuilder.DropColumn(
                name: "NewOptionsSummary",
                table: "SubscriptionChangeLogs");

            migrationBuilder.DropColumn(
                name: "OldOptionsSummary",
                table: "SubscriptionChangeLogs");
        }
    }
}
