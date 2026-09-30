using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Cycle28ShowcaseMarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsShowcase",
                table: "Companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowcaseBookingOpen",
                table: "Companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ShowcaseKind",
                table: "Bookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsShowcase",
                table: "BillingAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsShowcase",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Companies_IsShowcase",
                table: "Companies",
                column: "Id",
                filter: "\"IsShowcase\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Companies_ShowcaseBookingOpen",
                table: "Companies",
                sql: "NOT \"ShowcaseBookingOpen\" OR \"IsShowcase\"");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_ShowcaseVisitor",
                table: "Bookings",
                column: "CreatedAt",
                filter: "\"ShowcaseKind\" = 2");

            migrationBuilder.CreateIndex(
                name: "IX_BillingAccounts_IsShowcase",
                table: "BillingAccounts",
                column: "Id",
                filter: "\"IsShowcase\"");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_IsShowcase",
                table: "AspNetUsers",
                column: "Id",
                filter: "\"IsShowcase\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Companies_IsShowcase",
                table: "Companies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Companies_ShowcaseBookingOpen",
                table: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_ShowcaseVisitor",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_BillingAccounts_IsShowcase",
                table: "BillingAccounts");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_IsShowcase",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IsShowcase",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "ShowcaseBookingOpen",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "ShowcaseKind",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "IsShowcase",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "IsShowcase",
                table: "AspNetUsers");
        }
    }
}
