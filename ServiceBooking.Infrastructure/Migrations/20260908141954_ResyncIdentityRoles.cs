using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ResyncIdentityRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data migration, not a schema change (US-46, ARCHITECTURE.md §8.5/§16.1). Before this
            // cycle, CompaniesController.RemoveMember never revoked a Master/CompanyOwner Identity role
            // it no longer corresponded to, and AdminController.SetOwner never revoked the old owner's —
            // so dev/staging databases can already carry stale roles that IdentityRoleSync (added this
            // cycle, called from every membership-changing endpoint going forward) will never touch on
            // its own, because it only runs on the NEXT membership change for a given user. This is a
            // one-time reconciliation to bring existing rows to the state IdentityRoleSync would have
            // produced had it always been there. Client and SuperAdmin are never touched — out of scope
            // for both this migration and IdentityRoleSync (US-46 pp. 3, 4).
            //
            // CompanyMembers."Role" is stored as the UserRole enum's int value: Client=0, Master=1,
            // CompanyOwner=2, SuperAdmin=3 (ServiceBooking.Core.Enums.UserRole) — kept in sync with that
            // enum by hand, the same way PhoneNormalizer's SQL transliteration is (ARCHITECTURE.md
            // §14.3's convention, applied here for the first time to a role instead of a phone).

            // Revoke a Master/CompanyOwner Identity role that no longer has a matching CompanyMember row.
            migrationBuilder.Sql("""
                DELETE FROM "AspNetUserRoles" ur
                USING "AspNetRoles" r
                WHERE ur."RoleId" = r."Id"
                  AND r."Name" IN ('Master', 'CompanyOwner')
                  AND NOT EXISTS (
                    SELECT 1 FROM "CompanyMembers" cm
                    WHERE cm."UserId" = ur."UserId"
                      AND cm."Role" = CASE r."Name" WHEN 'Master' THEN 1 WHEN 'CompanyOwner' THEN 2 END
                  );
                """);

            // Grant a Master/CompanyOwner Identity role for every CompanyMember row that entitles the
            // user to one but where the role was never added.
            migrationBuilder.Sql("""
                INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
                SELECT DISTINCT cm."UserId", r."Id"
                FROM "CompanyMembers" cm
                JOIN "AspNetRoles" r ON r."Name" = CASE cm."Role" WHEN 1 THEN 'Master' WHEN 2 THEN 'CompanyOwner' END
                WHERE cm."Role" IN (1, 2)
                  AND NOT EXISTS (
                    SELECT 1 FROM "AspNetUserRoles" ur
                    WHERE ur."UserId" = cm."UserId" AND ur."RoleId" = r."Id"
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op by design (ARCHITECTURE.md §16.1): "bring role assignments in line with membership
            // rows" has no meaningful inverse — there is no prior state to restore to, only the
            // (already broken) one this migration fixes.
        }
    }
}
