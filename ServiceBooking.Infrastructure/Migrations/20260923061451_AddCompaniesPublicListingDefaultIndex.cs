using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompaniesPublicListingDefaultIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Companies_PublicListing_Default",
                table: "Companies",
                columns: new[] { "ShowInPublicListing", "Name", "Id" },
                filter: "\"IsActive\" AND \"ShowInPublicListing\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Companies_PublicListing_Default",
                table: "Companies");
        }
    }
}
