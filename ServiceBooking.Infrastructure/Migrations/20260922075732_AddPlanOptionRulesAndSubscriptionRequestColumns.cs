using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanOptionRulesAndSubscriptionRequestColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RequestedAtUtc",
                table: "BillingAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedByUserId",
                table: "BillingAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedComment",
                table: "BillingAccounts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedOptionsJson",
                table: "BillingAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedPlanId",
                table: "BillingAccounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlanOptionRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanConfigId = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Availability = table.Column<int>(type: "integer", nullable: false),
                    IncludedQuantity = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanOptionRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanOptionRules_SubscriptionOptions_OptionId",
                        column: x => x.OptionId,
                        principalTable: "SubscriptionOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlanOptionRules_SubscriptionPlanConfigs_PlanConfigId",
                        column: x => x.PlanConfigId,
                        principalTable: "SubscriptionPlanConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingAccounts_RequestedPlanId",
                table: "BillingAccounts",
                column: "RequestedPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanOptionRules_OptionId",
                table: "PlanOptionRules",
                column: "OptionId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanOptionRules_PlanConfigId_OptionId",
                table: "PlanOptionRules",
                columns: new[] { "PlanConfigId", "OptionId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BillingAccounts_SubscriptionPlanConfigs_RequestedPlanId",
                table: "BillingAccounts",
                column: "RequestedPlanId",
                principalTable: "SubscriptionPlanConfigs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BillingAccounts_SubscriptionPlanConfigs_RequestedPlanId",
                table: "BillingAccounts");

            migrationBuilder.DropTable(
                name: "PlanOptionRules");

            migrationBuilder.DropIndex(
                name: "IX_BillingAccounts_RequestedPlanId",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "RequestedAtUtc",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "RequestedByUserId",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "RequestedComment",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "RequestedOptionsJson",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "RequestedPlanId",
                table: "BillingAccounts");
        }
    }
}
