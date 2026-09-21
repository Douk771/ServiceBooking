using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPricingCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Highlights",
                table: "SubscriptionPlanConfigs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "SubscriptionPlanConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSystemFree",
                table: "SubscriptionPlanConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "SubscriptionPlanConfigs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "SubscriptionOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    CapabilityKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PricePerMonth = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    UnitName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    MaxQuantity = table.Column<int>(type: "integer", nullable: true),
                    UnitPriceText = table.Column<string>(type: "text", nullable: true),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionOptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlanConfigs_IsSystemFree",
                table: "SubscriptionPlanConfigs",
                column: "IsSystemFree",
                unique: true,
                filter: "\"IsSystemFree\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionOptions_Code",
                table: "SubscriptionOptions",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionOptions");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPlanConfigs_IsSystemFree",
                table: "SubscriptionPlanConfigs");

            migrationBuilder.DropColumn(
                name: "Highlights",
                table: "SubscriptionPlanConfigs");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "SubscriptionPlanConfigs");

            migrationBuilder.DropColumn(
                name: "IsSystemFree",
                table: "SubscriptionPlanConfigs");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "SubscriptionPlanConfigs");
        }
    }
}
