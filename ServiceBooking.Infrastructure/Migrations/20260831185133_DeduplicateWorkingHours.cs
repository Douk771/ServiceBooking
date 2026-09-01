using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DeduplicateWorkingHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Removes duplicate (MasterId, CompanyId, Date) rows created by the pre-fix check-then-act
            // race in WorkingHoursController.Upsert (audit B3). Keeps the row with the numerically
            // largest Id — an arbitrary but deterministic tiebreak, NOT "most recently created": Id is
            // Guid.NewGuid(), which has no chronological ordering. Must run BEFORE
            // AddWorkingHoursUniqueIndex, or that migration fails on any surviving duplicate. The
            // service is not in production (SPEC Q12), so no data-preservation strategy for the
            // discarded duplicates is needed beyond "keep one, drop the rest".
            migrationBuilder.Sql(
                "DELETE FROM \"WorkingHours\" wh USING \"WorkingHours\" dup " +
                "WHERE wh.\"MasterId\" = dup.\"MasterId\" AND wh.\"CompanyId\" = dup.\"CompanyId\" " +
                "AND wh.\"Date\" = dup.\"Date\" AND wh.\"Id\" < dup.\"Id\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: deleted duplicate rows cannot be reconstructed from what remains.
        }
    }
}
