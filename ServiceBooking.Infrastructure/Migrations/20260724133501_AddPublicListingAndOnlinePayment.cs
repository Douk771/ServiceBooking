using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicListingAndOnlinePayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowOnlinePayment",
                table: "SubscriptionPlanConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Defaults to true (not the scaffolded false) — existing plans/companies must stay visible
            // in the public directory after this migration, not silently vanish from it.
            migrationBuilder.AddColumn<bool>(
                name: "AllowPublicListing",
                table: "SubscriptionPlanConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowInPublicListing",
                table: "Companies",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowOnlinePayment",
                table: "SubscriptionPlanConfigs");

            migrationBuilder.DropColumn(
                name: "AllowPublicListing",
                table: "SubscriptionPlanConfigs");

            migrationBuilder.DropColumn(
                name: "ShowInPublicListing",
                table: "Companies");
        }
    }
}
