using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingsMasterDateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_MasterId",
                table: "Bookings");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_MasterId_Date",
                table: "Bookings",
                columns: new[] { "MasterId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_MasterId_Date",
                table: "Bookings");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_MasterId",
                table: "Bookings",
                column: "MasterId");
        }
    }
}
