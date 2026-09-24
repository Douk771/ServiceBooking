using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <summary>
    /// Data-only migration, no model change (cycle-12 review, blocker 3). Before this cycle,
    /// AppUser.PhoneNumberConfirmed was set to true for every staff account CompaniesController
    /// auto-created on the owner's behalf ("added by the owner → treated as a verified number") — a
    /// harmless internal note back when the flag had no visible meaning. Cycle 14 turned it into a
    /// PUBLIC "phoneVerified" signal (ProfileDto/AuthResponseDto/MasterClientDto) that is only supposed
    /// to be true once <c>PhoneVerificationWriter</c> — the sole other writer of this column — has
    /// actually recorded a MAX-verified <c>VerifiedPhones</c> row for that phone.
    ///
    /// This backfill resets the mirror to false for every account where it is currently true but no
    /// corresponding VerifiedPhones row exists for the account's current phone — exactly the accounts
    /// the auto-create path above stamped without any real verification ever happening. An account that
    /// WAS actually verified (VerifiedPhones row present) is left untouched.
    /// </summary>
    public partial class ResetUnverifiedPhoneNumberConfirmedMirror : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "AspNetUsers" u
                SET "PhoneNumberConfirmed" = false
                WHERE u."PhoneNumberConfirmed" = true
                  AND u."PhoneNumber" IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM "VerifiedPhones" v WHERE v."Phone" = u."PhoneNumber"
                  );
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately irreversible: the original `true` values this migration clears were never a
            // verification fact to begin with (that is the bug being fixed) — restoring them on a
            // rollback would just reintroduce the same false "phone verified" badge.
        }
    }
}
