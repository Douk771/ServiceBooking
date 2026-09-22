using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <summary>
    /// Cycle 7, stage 6 of 6 — the point of no return (ARCHITECTURE_CYCLE7.md §43.6, B5-13, US-73).
    ///
    /// Everything up to here (<c>AddBillingAccounts</c>, <c>BackfillBillingAccounts</c>,
    /// <c>AddChannelBillingAccountAndSubscriptionOptions</c>, <c>BackfillChannelBillingAccounts</c>,
    /// <c>AddSubscriptionChangeLogTransferColumns</c>, <c>AddPlanOptionRulesAndSubscriptionRequestColumns</c>)
    /// left <c>Company.BillingAccountId</c>, <c>AccountSubscription.BillingAccountId</c> and
    /// <c>NotificationChannel.BillingAccountId</c> nullable, with no composite keys — additive and
    /// reversible. This migration is not: it makes those three columns <c>NOT NULL</c>, adds the
    /// alternate keys <c>(Id, BillingAccountId)</c> on <c>Companies</c>/<c>NotificationChannels</c>, adds
    /// <c>ChannelCompanyAssignment.BillingAccountId</c>, and replaces its two single-column FKs with two
    /// composite FKs pinned to those alternate keys. Once deployed against real data, tightening these
    /// constraints back down is not a decision <c>Down</c> should make silently — see the guard below.
    ///
    /// <c>deploy/checks/billing-precheck.sql</c> is meant to be run against the target database BEFORE
    /// this migration, and should already have caught anything that would make the guard below fire. On
    /// this environment (staging never shipped the notifications feature to real traffic) neither script
    /// is expected to find anything; both exist for the environments where that stops being true.
    /// </summary>
    public partial class AddCoTenancyConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Step 1: the new column, added nullable so it can be backfilled before any constraint
            // is asked to hold it (mirrors how BackfillBillingAccounts treated the other three columns).
            migrationBuilder.AddColumn<Guid>(
                name: "BillingAccountId",
                table: "ChannelCompanyAssignments",
                type: "uuid",
                nullable: true);

            // ── Step 2: backfill. An assignment's account is, by construction, its company's account
            // (§43.6 — the whole point of this migration is to make that a database-enforced invariant
            // rather than an assumption).
            migrationBuilder.Sql(
                """
                UPDATE "ChannelCompanyAssignments" a
                SET "BillingAccountId" = c."BillingAccountId"
                FROM "Companies" c
                WHERE c."Id" = a."CompanyId";
                """);

            // ── Step 3: the guard. Everything from here down assumes the data is already clean — the
            // same assumption deploy/checks/billing-precheck.sql verifies before this migration ever
            // runs on a real environment. If any of these conditions fire, this migration is written to
            // fail loudly with a clear message rather than let AlterColumn/AddForeignKey fail on a raw
            // constraint violation with no context for whoever is watching the deploy.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    companies_without_account int;
                    subscriptions_without_account int;
                    channels_without_account int;
                    assignments_without_account int;
                    cross_account_assignments int;
                BEGIN
                    SELECT count(*) INTO companies_without_account
                        FROM "Companies" WHERE "BillingAccountId" IS NULL;
                    SELECT count(*) INTO subscriptions_without_account
                        FROM "AccountSubscriptions" WHERE "BillingAccountId" IS NULL;
                    SELECT count(*) INTO channels_without_account
                        FROM "NotificationChannels" WHERE "BillingAccountId" IS NULL;
                    SELECT count(*) INTO assignments_without_account
                        FROM "ChannelCompanyAssignments" WHERE "BillingAccountId" IS NULL;

                    IF companies_without_account > 0 THEN
                        RAISE EXCEPTION 'AddCoTenancyConstraints: % Companies row(s) still have a NULL BillingAccountId; run/fix BackfillBillingAccounts first (see deploy/checks/billing-precheck.sql)', companies_without_account;
                    END IF;
                    IF subscriptions_without_account > 0 THEN
                        RAISE EXCEPTION 'AddCoTenancyConstraints: % AccountSubscriptions row(s) still have a NULL BillingAccountId; run/fix BackfillBillingAccounts first (see deploy/checks/billing-precheck.sql)', subscriptions_without_account;
                    END IF;
                    IF channels_without_account > 0 THEN
                        RAISE EXCEPTION 'AddCoTenancyConstraints: % NotificationChannels row(s) still have a NULL BillingAccountId; run/fix BackfillChannelBillingAccounts first (see deploy/checks/billing-precheck.sql)', channels_without_account;
                    END IF;
                    IF assignments_without_account > 0 THEN
                        RAISE EXCEPTION 'AddCoTenancyConstraints: % ChannelCompanyAssignments row(s) still have a NULL BillingAccountId after backfill — their Company row itself has no BillingAccountId (see companies_without_account above)', assignments_without_account;
                    END IF;

                    -- The composite FK to NotificationChannels(Id, BillingAccountId) can only be created
                    -- if every assignment's BillingAccountId (= its company's) matches its channel's.
                    -- A mismatch here means someone is already being served by a number that belongs to
                    -- a different paying account — a "company one account, number another" state that
                    -- SPEC §3.3 p.5 says must never exist. This is exactly the finding
                    -- deploy/checks/billing-precheck.sql is designed to surface, in advance, on a real
                    -- environment.
                    SELECT count(*) INTO cross_account_assignments
                        FROM "ChannelCompanyAssignments" a
                        JOIN "NotificationChannels" n ON n."Id" = a."ChannelId"
                        WHERE a."BillingAccountId" <> n."BillingAccountId";

                    IF cross_account_assignments > 0 THEN
                        RAISE EXCEPTION 'AddCoTenancyConstraints: % ChannelCompanyAssignments row(s) assign a company to a number belonging to a DIFFERENT billing account — fix these assignments (unassign or transfer the company first) before this migration can create the co-tenancy composite FKs. Run deploy/checks/billing-precheck.sql for the exact rows.', cross_account_assignments;
                    END IF;
                END $$;
                """);

            // ── Step 4: only after the guard passed do we tighten anything. No defaultValue on any of
            // these — a NULL surviving to this point is a bug in the guard above, not something to paper
            // over with a placeholder Guid.
            migrationBuilder.AlterColumn<Guid>(
                name: "BillingAccountId",
                table: "Companies",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "BillingAccountId",
                table: "AccountSubscriptions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "BillingAccountId",
                table: "NotificationChannels",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "BillingAccountId",
                table: "ChannelCompanyAssignments",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            // ── Step 5: the alternate keys the composite FKs pin to.
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Companies_Id_BillingAccountId",
                table: "Companies",
                columns: new[] { "Id", "BillingAccountId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_NotificationChannels_Id_BillingAccountId",
                table: "NotificationChannels",
                columns: new[] { "Id", "BillingAccountId" });

            // ── Step 6: swap the single-column FKs for composite ones.
            migrationBuilder.DropForeignKey(
                name: "FK_ChannelCompanyAssignments_Companies_CompanyId",
                table: "ChannelCompanyAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_ChannelCompanyAssignments_NotificationChannels_ChannelId",
                table: "ChannelCompanyAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelCompanyAssignments_ChannelId_BillingAccountId",
                table: "ChannelCompanyAssignments",
                columns: new[] { "ChannelId", "BillingAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelCompanyAssignments_CompanyId_BillingAccountId",
                table: "ChannelCompanyAssignments",
                columns: new[] { "CompanyId", "BillingAccountId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelCompanyAssignments_Companies_CompanyId_BillingAccoun~",
                table: "ChannelCompanyAssignments",
                columns: new[] { "CompanyId", "BillingAccountId" },
                principalTable: "Companies",
                principalColumns: new[] { "Id", "BillingAccountId" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelCompanyAssignments_NotificationChannels_ChannelId_Bi~",
                table: "ChannelCompanyAssignments",
                columns: new[] { "ChannelId", "BillingAccountId" },
                principalTable: "NotificationChannels",
                principalColumns: new[] { "Id", "BillingAccountId" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Structurally reversible (loosening constraints back up loses no data) and kept working for
            // local development/CI, where the test database is torn down and recreated on every run
            // anyway (CURRENT_STATE.md §7). It is NOT meant to be run against a real environment after
            // this migration has shipped and the app has started relying on these invariants elsewhere
            // (CompanyTransferService's ordering, §43.6) — that is the "single irreversible step" this
            // migration is documented as being, in the operational sense, even though the DDL itself can
            // be undone.
            migrationBuilder.DropForeignKey(
                name: "FK_ChannelCompanyAssignments_Companies_CompanyId_BillingAccoun~",
                table: "ChannelCompanyAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_ChannelCompanyAssignments_NotificationChannels_ChannelId_Bi~",
                table: "ChannelCompanyAssignments");

            migrationBuilder.DropIndex(
                name: "IX_ChannelCompanyAssignments_ChannelId_BillingAccountId",
                table: "ChannelCompanyAssignments");

            migrationBuilder.DropIndex(
                name: "IX_ChannelCompanyAssignments_CompanyId_BillingAccountId",
                table: "ChannelCompanyAssignments");

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelCompanyAssignments_Companies_CompanyId",
                table: "ChannelCompanyAssignments",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelCompanyAssignments_NotificationChannels_ChannelId",
                table: "ChannelCompanyAssignments",
                column: "ChannelId",
                principalTable: "NotificationChannels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Companies_Id_BillingAccountId",
                table: "Companies");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_NotificationChannels_Id_BillingAccountId",
                table: "NotificationChannels");

            migrationBuilder.AlterColumn<Guid>(
                name: "BillingAccountId",
                table: "Companies",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "BillingAccountId",
                table: "AccountSubscriptions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "BillingAccountId",
                table: "NotificationChannels",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.DropColumn(
                name: "BillingAccountId",
                table: "ChannelCompanyAssignments");
        }
    }
}
