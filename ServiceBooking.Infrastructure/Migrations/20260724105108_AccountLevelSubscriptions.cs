using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountLevelSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Plan config: rename MaxMasters → MaxEmployees, add MaxCompanies ──────────────────────
            migrationBuilder.RenameColumn(
                name: "MaxMasters",
                table: "SubscriptionPlanConfigs",
                newName: "MaxEmployees");

            migrationBuilder.AddColumn<int>(
                name: "MaxCompanies",
                table: "SubscriptionPlanConfigs",
                type: "integer",
                nullable: true);

            // ── Companies.OwnerUserId: add nullable, backfill from the CompanyOwner member, then NOT NULL ─
            migrationBuilder.AddColumn<string>(
                name: "OwnerUserId",
                table: "Companies",
                type: "text",
                nullable: true);

            // Role = 2 is UserRole.CompanyOwner. Pick the earliest owner member deterministically.
            migrationBuilder.Sql(@"
                UPDATE ""Companies"" c
                SET ""OwnerUserId"" = (
                    SELECT cm.""UserId"" FROM ""CompanyMembers"" cm
                    WHERE cm.""CompanyId"" = c.""Id"" AND cm.""Role"" = 2
                    ORDER BY cm.""Id""
                    LIMIT 1
                );");

            migrationBuilder.AlterColumn<string>(
                name: "OwnerUserId",
                table: "Companies",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            // ── AccountSubscriptions: create, then migrate one row per owner from CompanySubscriptions ──
            migrationBuilder.CreateTable(
                name: "AccountSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<string>(type: "text", nullable: false),
                    PlanConfigId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountSubscriptions_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccountSubscriptions_SubscriptionPlanConfigs_PlanConfigId",
                        column: x => x.PlanConfigId,
                        principalTable: "SubscriptionPlanConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // One account subscription per owner: prefer a company sub that has a real plan, then the
            // most recently updated. Owners with only Free companies still get a (plan-less) row.
            migrationBuilder.Sql(@"
                INSERT INTO ""AccountSubscriptions""
                    (""Id"", ""OwnerUserId"", ""PlanConfigId"", ""PaidUntil"", ""IsActive"", ""CreatedAt"", ""UpdatedAt"")
                SELECT DISTINCT ON (c.""OwnerUserId"")
                    gen_random_uuid(), c.""OwnerUserId"", cs.""PlanConfigId"", cs.""PaidUntil"",
                    cs.""IsActive"", cs.""CreatedAt"", cs.""UpdatedAt""
                FROM ""CompanySubscriptions"" cs
                JOIN ""Companies"" c ON c.""Id"" = cs.""CompanyId""
                WHERE c.""OwnerUserId"" IS NOT NULL AND c.""OwnerUserId"" <> ''
                ORDER BY c.""OwnerUserId"", (cs.""PlanConfigId"" IS NOT NULL) DESC, cs.""UpdatedAt"" DESC;");

            // ── SubscriptionChangeLogs: re-key from CompanyId to OwnerUserId ────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "OwnerUserId",
                table: "SubscriptionChangeLogs",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE ""SubscriptionChangeLogs"" l
                SET ""OwnerUserId"" = (SELECT c.""OwnerUserId"" FROM ""Companies"" c WHERE c.""Id"" = l.""CompanyId"");");

            // Any log whose company is gone (shouldn't happen — old FK was cascade) gets a placeholder.
            migrationBuilder.Sql(@"UPDATE ""SubscriptionChangeLogs"" SET ""OwnerUserId"" = '' WHERE ""OwnerUserId"" IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "OwnerUserId",
                table: "SubscriptionChangeLogs",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.DropForeignKey(
                name: "FK_SubscriptionChangeLogs_Companies_CompanyId",
                table: "SubscriptionChangeLogs");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionChangeLogs_CompanyId",
                table: "SubscriptionChangeLogs");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "SubscriptionChangeLogs");

            // ── Drop the now-migrated per-company subscription table ────────────────────────────────────
            migrationBuilder.DropTable(
                name: "CompanySubscriptions");

            // ── Indexes & FKs ───────────────────────────────────────────────────────────────────────────
            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionChangeLogs_OwnerUserId",
                table: "SubscriptionChangeLogs",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_OwnerUserId",
                table: "Companies",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountSubscriptions_OwnerUserId",
                table: "AccountSubscriptions",
                column: "OwnerUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountSubscriptions_PlanConfigId",
                table: "AccountSubscriptions",
                column: "PlanConfigId");

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_AspNetUsers_OwnerUserId",
                table: "Companies",
                column: "OwnerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Companies_AspNetUsers_OwnerUserId",
                table: "Companies");

            migrationBuilder.DropTable(
                name: "AccountSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionChangeLogs_OwnerUserId",
                table: "SubscriptionChangeLogs");

            migrationBuilder.DropIndex(
                name: "IX_Companies_OwnerUserId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "MaxCompanies",
                table: "SubscriptionPlanConfigs");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "SubscriptionChangeLogs");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "Companies");

            migrationBuilder.RenameColumn(
                name: "MaxEmployees",
                table: "SubscriptionPlanConfigs",
                newName: "MaxMasters");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "SubscriptionChangeLogs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "CompanySubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanConfigId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    PaidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanySubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanySubscriptions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompanySubscriptions_SubscriptionPlanConfigs_PlanConfigId",
                        column: x => x.PlanConfigId,
                        principalTable: "SubscriptionPlanConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionChangeLogs_CompanyId",
                table: "SubscriptionChangeLogs",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanySubscriptions_CompanyId",
                table: "CompanySubscriptions",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanySubscriptions_PlanConfigId",
                table: "CompanySubscriptions",
                column: "PlanConfigId");

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionChangeLogs_Companies_CompanyId",
                table: "SubscriptionChangeLogs",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
