using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanPhotoLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PhotoQuotaMb",
                table: "SubscriptionPlanConfigs",
                type: "integer",
                nullable: true);

            // PhotoRetention defaults to 0 (SixMonths) for existing rows via the column default below;
            // the backfill Sql right after overrides that default for plans that already existed before
            // this migration, per API_CONTRACT.md §11.1 ("service isn't in production, values are
            // adjusted by the super-admin afterwards" — this is a deliberate, documented starting point,
            // not a guess at what any specific plan should cost).
            migrationBuilder.AddColumn<int>(
                name: "PhotoRetention",
                table: "SubscriptionPlanConfigs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Backfill: every plan that existed before this migration gets a non-trivial starting point
            // (1 GB / 12 months) instead of silently inheriting the Free baseline's 100 MB / 6 months —
            // a paid tariff that suddenly matches Free would look like a regression to whoever notices it
            // first. New plans created after this migration get the entity's own defaults instead.
            migrationBuilder.Sql(
                """UPDATE "SubscriptionPlanConfigs" SET "PhotoQuotaMb" = 1024, "PhotoRetention" = 1;""");

            // US-22 / code review finding: AppUser.CommissionPercent was left dead after ProfileDto and
            // AdminUserDto stopped exposing it (commission became per-company on CompanyMember back in
            // cycle A) — dropped here rather than in a sixth migration, since this is still the earliest
            // point in the cycle's migration chain where the column's only remaining readers are already
            // gone.
            migrationBuilder.DropColumn(
                name: "CommissionPercent",
                table: "AspNetUsers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CommissionPercent",
                table: "AspNetUsers",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.DropColumn(
                name: "PhotoQuotaMb",
                table: "SubscriptionPlanConfigs");

            migrationBuilder.DropColumn(
                name: "PhotoRetention",
                table: "SubscriptionPlanConfigs");
        }
    }
}
