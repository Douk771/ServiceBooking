using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <summary>
    /// Proход B, first migration (ARCHITECTURE_CYCLE9.md §104.3, US-119). Widens the cycle-7 co-tenancy
    /// composite key on <c>NotificationChannels</c> from <c>(Id, BillingAccountId)</c> to
    /// <c>(Id, BillingAccountId, Transport)</c>, and gives <c>ChannelCompanyAssignments</c> its own
    /// (denormalized, FK-pinned) <c>Transport</c> column — turning "a company may be assigned to at most
    /// one channel of EACH transport" into a schema property (unique index on
    /// <c>(CompanyId, Transport)</c>) instead of the old cycle-4 "one channel at all"
    /// (<c>IX_ChannelCompanyAssignments_CompanyId</c>).
    ///
    /// <c>Transport</c> is added with <c>defaultValue: 0</c> (<see cref="Core.Enums.NotificationTransport.WhatsApp"/>)
    /// and NO backfill statement: every assignment that exists before this migration points at a channel
    /// that is, by construction, WhatsApp (MAX doesn't exist yet), so the default is already correct data
    /// — there is nothing to compute from <c>NotificationChannels.Transport</c> that the column's own
    /// default doesn't already give for free.
    ///
    /// ⚠️ This is a BREAKING schema change, and it is deliberate (§104.3): it is not reverted casually.
    /// <see cref="Down"/> only ever restores the old single-column form when the data can still fit it —
    /// see its own guard.
    /// </summary>
    public partial class AddTransportToAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChannelCompanyAssignments_NotificationChannels_ChannelId_Bi~",
                table: "ChannelCompanyAssignments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_NotificationChannels_Id_BillingAccountId",
                table: "NotificationChannels");

            migrationBuilder.DropIndex(
                name: "IX_ChannelCompanyAssignments_ChannelId_BillingAccountId",
                table: "ChannelCompanyAssignments");

            migrationBuilder.DropIndex(
                name: "IX_ChannelCompanyAssignments_CompanyId",
                table: "ChannelCompanyAssignments");

            migrationBuilder.AddColumn<int>(
                name: "Transport",
                table: "ChannelCompanyAssignments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_NotificationChannels_Id_BillingAccountId_Transport",
                table: "NotificationChannels",
                columns: new[] { "Id", "BillingAccountId", "Transport" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelCompanyAssignments_ChannelId_BillingAccountId_Transp~",
                table: "ChannelCompanyAssignments",
                columns: new[] { "ChannelId", "BillingAccountId", "Transport" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelCompanyAssignments_CompanyId_Transport",
                table: "ChannelCompanyAssignments",
                columns: new[] { "CompanyId", "Transport" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelCompanyAssignments_NotificationChannels_ChannelId_Bi~",
                table: "ChannelCompanyAssignments",
                columns: new[] { "ChannelId", "BillingAccountId", "Transport" },
                principalTable: "NotificationChannels",
                principalColumns: new[] { "Id", "BillingAccountId", "Transport" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <summary>
        /// ARCHITECTURE_CYCLE9.md §104.3: "Down этой миграции возвращает старую форму только если у
        /// каждой компании не больше одного назначения — иначе Down падает с описательной ошибкой, а не
        /// молча теряет строки." Same convention <c>NormalizePhoneNumbers</c> and
        /// <c>AddCoTenancyConstraints</c> already established for this project: the guard runs FIRST,
        /// before a single destructive statement, and a violation stops the migration with a message an
        /// operator can act on rather than an opaque unique-constraint violation once the old
        /// single-column index is recreated over data that no longer fits it.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    companies_with_multiple_assignments int;
                BEGIN
                    SELECT count(*) INTO companies_with_multiple_assignments FROM (
                        SELECT "CompanyId" FROM "ChannelCompanyAssignments"
                        GROUP BY "CompanyId" HAVING count(*) > 1
                    ) dup;

                    IF companies_with_multiple_assignments > 0 THEN
                        RAISE EXCEPTION 'AddTransportToAssignments Down: % company(ies) are assigned to more than one channel (one per transport) — this migration only reverts to the old "one channel per company, ever" schema when every company still fits it. Unassign the extra channel(s) first (see ARCHITECTURE_CYCLE9.md §104.3).', companies_with_multiple_assignments;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_ChannelCompanyAssignments_NotificationChannels_ChannelId_Bi~",
                table: "ChannelCompanyAssignments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_NotificationChannels_Id_BillingAccountId_Transport",
                table: "NotificationChannels");

            migrationBuilder.DropIndex(
                name: "IX_ChannelCompanyAssignments_ChannelId_BillingAccountId_Transp~",
                table: "ChannelCompanyAssignments");

            migrationBuilder.DropIndex(
                name: "IX_ChannelCompanyAssignments_CompanyId_Transport",
                table: "ChannelCompanyAssignments");

            migrationBuilder.DropColumn(
                name: "Transport",
                table: "ChannelCompanyAssignments");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_NotificationChannels_Id_BillingAccountId",
                table: "NotificationChannels",
                columns: new[] { "Id", "BillingAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelCompanyAssignments_ChannelId_BillingAccountId",
                table: "ChannelCompanyAssignments",
                columns: new[] { "ChannelId", "BillingAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelCompanyAssignments_CompanyId",
                table: "ChannelCompanyAssignments",
                column: "CompanyId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelCompanyAssignments_NotificationChannels_ChannelId_Bi~",
                table: "ChannelCompanyAssignments",
                columns: new[] { "ChannelId", "BillingAccountId" },
                principalTable: "NotificationChannels",
                principalColumns: new[] { "Id", "BillingAccountId" },
                onDelete: ReferentialAction.Cascade);
        }
    }
}
