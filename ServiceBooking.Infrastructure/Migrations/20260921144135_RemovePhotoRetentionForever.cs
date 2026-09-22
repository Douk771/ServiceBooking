using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemovePhotoRetentionForever : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ARCHITECTURE_CYCLE5.md §44.7/§44.8 (M6), Q-L6: `PhotoRetention.Forever` (value 2) is removed
            // from the enum — "kept forever" cannot be a lawful retention period for personal data. The
            // column's SQL type is unchanged (still `integer`), so EF detected no schema difference here;
            // this migration's only job is the data rewrite: every existing `Forever` row becomes
            // `TwelveMonths` (value 1), the closest defined term. 🔴 This is data loss by design (§44.8's
            // migration table marks M6 irreversible) — Down() cannot know which rows were originally 2.
            migrationBuilder.Sql(
                """UPDATE "SubscriptionPlanConfigs" SET "PhotoRetention" = 1 WHERE "PhotoRetention" = 2;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally a no-op: §44.8 marks M6 irreversible ("значение утрачено") — rows that were
            // Forever before Up() are indistinguishable from rows that were always TwelveMonths after it.
        }
    }
}
