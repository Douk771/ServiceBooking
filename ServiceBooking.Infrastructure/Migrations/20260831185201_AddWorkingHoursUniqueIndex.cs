using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkingHoursUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkingHours_MasterId",
                table: "WorkingHours");

            migrationBuilder.CreateIndex(
                name: "IX_WorkingHours_MasterId_CompanyId_Date",
                table: "WorkingHours",
                columns: new[] { "MasterId", "CompanyId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkingHours_MasterId_CompanyId_Date",
                table: "WorkingHours");

            migrationBuilder.CreateIndex(
                name: "IX_WorkingHours_MasterId",
                table: "WorkingHours",
                column: "MasterId");
        }
    }
}
