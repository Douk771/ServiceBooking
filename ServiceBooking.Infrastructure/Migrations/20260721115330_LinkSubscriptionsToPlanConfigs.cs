using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkSubscriptionsToPlanConfigs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Notes",
                table: "CompanySubscriptions");

            migrationBuilder.DropColumn(
                name: "Plan",
                table: "CompanySubscriptions");

            migrationBuilder.AddColumn<Guid>(
                name: "PlanConfigId",
                table: "CompanySubscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubscriptionChangeLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedByUserId = table.Column<string>(type: "text", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OldPlanConfigId = table.Column<Guid>(type: "uuid", nullable: true),
                    NewPlanConfigId = table.Column<Guid>(type: "uuid", nullable: true),
                    OldPaidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NewPaidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OldIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    NewIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionChangeLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionChangeLogs_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanySubscriptions_PlanConfigId",
                table: "CompanySubscriptions",
                column: "PlanConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionChangeLogs_CompanyId",
                table: "SubscriptionChangeLogs",
                column: "CompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_CompanySubscriptions_SubscriptionPlanConfigs_PlanConfigId",
                table: "CompanySubscriptions",
                column: "PlanConfigId",
                principalTable: "SubscriptionPlanConfigs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CompanySubscriptions_SubscriptionPlanConfigs_PlanConfigId",
                table: "CompanySubscriptions");

            migrationBuilder.DropTable(
                name: "SubscriptionChangeLogs");

            migrationBuilder.DropIndex(
                name: "IX_CompanySubscriptions_PlanConfigId",
                table: "CompanySubscriptions");

            migrationBuilder.DropColumn(
                name: "PlanConfigId",
                table: "CompanySubscriptions");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "CompanySubscriptions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Plan",
                table: "CompanySubscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
