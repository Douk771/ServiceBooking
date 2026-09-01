using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DeduplicateCompanyMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Removes duplicate (CompanyId, UserId) rows created by the pre-fix check-then-act race in
            // CompaniesController.AddMember (same class of bug as WorkingHours, audit B3). Must run
            // BEFORE AddCompanyMemberUniqueIndex, or that migration fails on any surviving duplicate.
            // Keeps the row with the highest CommissionPercent, so a nonzero rate set on one duplicate
            // is never silently discarded in favor of an untouched 0% one — Id is a random Guid
            // (Guid.NewGuid()), not a creation-order sequence, so "keep the largest/smallest Id" would
            // not be a meaningful tiebreaker. The service is not in production (SPEC Q12), so no
            // data-preservation strategy for the discarded duplicates is needed beyond "keep one, drop
            // the rest".
            migrationBuilder.Sql(
                "DELETE FROM \"CompanyMembers\" cm USING \"CompanyMembers\" dup " +
                "WHERE cm.\"CompanyId\" = dup.\"CompanyId\" AND cm.\"UserId\" = dup.\"UserId\" " +
                "AND (cm.\"CommissionPercent\" < dup.\"CommissionPercent\" " +
                "     OR (cm.\"CommissionPercent\" = dup.\"CommissionPercent\" AND cm.\"Id\" < dup.\"Id\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: deleted duplicate rows cannot be reconstructed from what remains.
        }
    }
}
