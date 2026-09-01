using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingCommissionSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CommissionPercent",
                table: "Bookings",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            // Backfill existing rows with the master's CURRENT commission rate for that company, since
            // it's the best available approximation of the rate at booking time for data that predates
            // this snapshot column (same approach AddBookingPriceSnapshot used for Price). If a master's
            // membership in that company no longer exists, the booking keeps the 0 default rather than
            // failing the migration — there is nothing left to backfill from.
            migrationBuilder.Sql(
                "UPDATE \"Bookings\" b SET \"CommissionPercent\" = cm.\"CommissionPercent\" " +
                "FROM \"CompanyMembers\" cm " +
                "WHERE cm.\"CompanyId\" = b.\"CompanyId\" AND cm.\"UserId\" = b.\"MasterId\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommissionPercent",
                table: "Bookings");
        }
    }
}
