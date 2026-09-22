using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <summary>
    /// Cycle 5, stage 3 of 6 (ARCHITECTURE_CYCLE5.md §43.4/§47, US-73's "no company/number loses
    /// anything" guarantee extended to numbers). Every existing <c>NotificationChannel</c> points at
    /// its OWNER's billing account — the same account <c>BackfillBillingAccounts</c> already
    /// provisioned/pointed every <c>Company</c>/<c>AccountSubscription</c> at, by the same
    /// <c>OwnerUserId</c> join. No new <c>BillingAccount</c> rows are created here: a channel's owner
    /// is, today, always also a company owner (channels are only ever created by
    /// <c>NotificationChannelsController.Create</c>, gated on <c>IsAnyCompanyOwnerAsync</c>), so an
    /// account already exists for every channel row by the time this runs.
    ///
    /// Additive and reversible (§54.1): no NOT NULL, no composite alt-key. Idempotent (re-derives the
    /// same join every time; safe to re-run Up after a Down).
    /// </summary>
    public partial class BackfillChannelBillingAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "NotificationChannels" nc
                SET "BillingAccountId" = ba."Id"
                FROM "BillingAccounts" ba
                WHERE ba."OwnerUserId" = nc."OwnerUserId"
                  AND nc."BillingAccountId" IS DISTINCT FROM ba."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "NotificationChannels" SET "BillingAccountId" = NULL WHERE "BillingAccountId" IS NOT NULL;
                """);
        }
    }
}
