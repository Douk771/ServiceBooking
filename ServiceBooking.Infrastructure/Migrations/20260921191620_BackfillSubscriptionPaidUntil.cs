using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillSubscriptionPaidUntil : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ARCHITECTURE_CYCLE6.md §43.5: subscriptions with no end date become "paid one year from
            // now" rather than staying open-ended, because §43.3.6 makes an end date mandatory going
            // forward and "PaidUntil IS NULL" must stop being a state the rest of the system has to
            // reason about. Deliberately not reversible in Down (`SPEC_CYCLE6_BOOKING_FIXES.md` §0.1: this cycle's data is
            // test data, no rollback plan is required) — reversing would have to guess which rows were
            // genuinely NULL before this ran.
            migrationBuilder.Sql(
                "UPDATE \"AccountSubscriptions\" SET \"PaidUntil\" = (now() AT TIME ZONE 'UTC') + interval '1 year' " +
                "WHERE \"PaidUntil\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
