using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingPriceSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "Bookings",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m);

            // Backfill existing rows with the current service price, since it's the best available
            // approximation of the price at booking time for data that predates this snapshot column.
            migrationBuilder.Sql(
                "UPDATE \"Bookings\" b SET \"Price\" = s.\"Price\" FROM \"Services\" s WHERE s.\"Id\" = b.\"ServiceId\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Price",
                table: "Bookings");
        }
    }
}
