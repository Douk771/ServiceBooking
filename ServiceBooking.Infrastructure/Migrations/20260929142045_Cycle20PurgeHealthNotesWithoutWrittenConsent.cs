using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <summary>
    /// ARCHITECTURE_CYCLE20.md §402.6 (US-20-01, LEGAL_REVIEW_CYCLE20.md Т20-03 п. 7) — a ONE-TIME data
    /// migration, not a schema change. At the moment this runs, no <c>ConsentRecords</c> row with
    /// <c>Source = PaperForm</c> exists anywhere yet (this migration is committed and applied before any
    /// code that can write one), so "delete health notes without a written-consent mark" and "delete
    /// every health note" are the same operation — deleting all of them is correct, not a shortcut.
    ///
    /// ⚠️ PRECONDITION, checked by devops before this runs on production (§402.6, ARCHITECTURE_CYCLE20.md
    /// §413 П-1): no real subject exists yet. All data on production at the time this cycle ships is test
    /// data (LEGAL_DECISIONS_CYCLE20.md §5). If a real client has appeared by the time of deployment, this
    /// migration must NOT be applied — the question goes back to legal-counsel and the customer.
    /// </summary>
    public partial class Cycle20PurgeHealthNotesWithoutWrittenConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM \"ClientHealthNotes\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty — irreversible by customer decision (П1, LEGAL_DECISIONS_CYCLE20.md §5).
            // The deleted rows cannot be reconstructed; there is nothing a Down() could restore.
        }
    }
}
