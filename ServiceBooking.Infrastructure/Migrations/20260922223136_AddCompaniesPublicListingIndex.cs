using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompaniesPublicListingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Companies_PublicListing",
                table: "Companies",
                columns: new[] { "ShowInPublicListing", "CityId", "Name" },
                filter: "\"IsActive\" AND \"ShowInPublicListing\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Companies_PublicListing",
                table: "Companies");
        }
    }
}
