using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyMemberCommission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CommissionPercent",
                table: "CompanyMembers",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            // Seed every existing membership from the account-level value it used to share
            // (AppUser.CommissionPercent). A master who moonlights at several companies gets the SAME
            // starting value in all of them — that's the best available default without a manual data
            // review, which the service being pre-production (SPEC Q12) means we don't need to do.
            // Owners can adjust per-company afterward via PUT .../members/{memberId}/commission.
            migrationBuilder.Sql(
                "UPDATE \"CompanyMembers\" cm SET \"CommissionPercent\" = u.\"CommissionPercent\" " +
                "FROM \"AspNetUsers\" u WHERE u.\"Id\" = cm.\"UserId\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommissionPercent",
                table: "CompanyMembers");
        }
    }
}
