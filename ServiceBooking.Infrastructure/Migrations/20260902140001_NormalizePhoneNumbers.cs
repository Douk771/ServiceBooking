using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <summary>
    /// US-26 / ARCHITECTURE.md §14.3. Brings every phone number already in the database to the same
    /// canonical form <c>PhoneNormalizer.Normalize</c> produces going forward (SQL transliteration of
    /// that exact rule — shown here so the two can be diffed side by side in review, per §14.3 R4).
    /// The SQL strips only the ASCII digit class (<c>[^0-9]</c>), matching <c>PhoneNormalizer.Normalize</c>'s
    /// explicit ASCII-only filter — NOT Postgres's locale-sensitive <c>\D</c>, which can disagree with
    /// C#'s <c>char.IsDigit</c> on non-ASCII decimal digits (code review finding).
    ///
    /// Deliberately the LAST migration of the cycle and its own PR: it depends on
    /// ClientNotePhotos existing (ClientNotePhoto.UploadedByUserId needs its SetNull FK in place before
    /// accounts start getting deleted — ARCHITECTURE.md §14.2), and it is the one migration in this
    /// cycle that is genuinely destructive, so it needs to be runnable and reviewable on its own.
    ///
    /// Collision rule (SPEC US-26 p.8): when two-or-more accounts normalize to the same number, the
    /// account with the most related rows (bookings as client + bookings as master + company
    /// memberships) survives; ties break on the earliest CreatedAt.
    ///
    /// WHAT HAPPENS TO A LOSING ACCOUNT'S DATA, BY FOREIGN KEY (audited against AppDbContext.cs, not
    /// assumed — this is exactly what a first pass at this migration got wrong, code review finding):
    ///   - Bookings.ClientId, ClientNotes.ClientId, ClientNotePhotos.UploadedByUserId: ON DELETE SET
    ///     NULL — survive the delete untouched, just lose an attribution.
    ///   - ClientNotes.MasterId, Reviews.MasterId, MailLogs.SentById: ON DELETE CASCADE or RESTRICT in
    ///     the schema, but these all represent "which member of the COMPANY's staff did this", not a
    ///     fact about the specific account identity (the same reasoning already applied to
    ///     ClientNote.MasterId's own doc comment and to RemoveMember, US-20 p.4) — so before deleting a
    ///     losing account, its rows in these three tables are REASSIGNED to the surviving account. A
    ///     note/review/mail-log a losing account authored is not lost, and does not block the DELETE.
    ///   - Companies.OwnerUserId (RESTRICT) and Bookings.MasterId (RESTRICT): these are NOT reassigned —
    ///     company ownership is a billing/administration fact this migration has no business changing
    ///     silently, and Bookings.MasterId is the historical record of who actually did the work, which
    ///     reassigning would falsify. Instead, the migration STOPS with a descriptive error naming the
    ///     offending row(s) if the account that ranking would delete owns a company, or is a master with
    ///     existing bookings — checked for every losing account individually (not just "2 or more in the
    ///     group", which would miss the asymmetric case where only one loser — not the winner — holds
    ///     one of these; that case would otherwise reach a bare Postgres FK-violation instead of the
    ///     descriptive error SPEC asks for).
    ///   - CompanyMembers, WorkingHours, MasterServices, AccountSubscriptions: cascade away with the
    ///     losing account (default EF behavior for required relationships, or explicit Cascade). Left as
    ///     cascade deliberately: these represent the losing identity's OWN employment/schedule/
    ///     subscription, which does not make sense to merge into the winner's (their schedules could
    ///     collide, and CompanyMembers is already one of the signals the ranking counts).
    /// </summary>
    public partial class NormalizePhoneNumbers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                -- Session-scoped helper so the normalization rule is written exactly once in this file
                -- and reused by every UPDATE below, instead of repeating the CASE expression several
                -- times. '[^0-9]' (not '\D') is deliberate — see the class doc.
                CREATE OR REPLACE FUNCTION pg_temp.canon_phone(raw text) RETURNS text AS $func$
                    SELECT CASE
                        WHEN length(regexp_replace(COALESCE(raw, ''), '[^0-9]', '', 'g')) = 11
                             AND left(regexp_replace(COALESCE(raw, ''), '[^0-9]', '', 'g'), 1) = '8'
                            THEN '7' || substring(regexp_replace(COALESCE(raw, ''), '[^0-9]', '', 'g') FROM 2)
                        WHEN length(regexp_replace(COALESCE(raw, ''), '[^0-9]', '', 'g')) = 10
                             AND left(regexp_replace(COALESCE(raw, ''), '[^0-9]', '', 'g'), 1) = '9'
                            THEN '7' || regexp_replace(COALESCE(raw, ''), '[^0-9]', '', 'g')
                        ELSE regexp_replace(COALESCE(raw, ''), '[^0-9]', '', 'g')
                    END;
                $func$ LANGUAGE sql IMMUTABLE;

                DO $$
                DECLARE
                    rec RECORD;
                    bad_ids TEXT;
                BEGIN
                    CREATE TEMP TABLE phone_canon ON COMMIT DROP AS
                    SELECT u."Id" AS user_id, u."CreatedAt" AS created_at,
                           pg_temp.canon_phone(u."PhoneNumber") AS canonical
                    FROM "AspNetUsers" u;

                    CREATE TEMP TABLE phone_rank ON COMMIT DROP AS
                    SELECT
                        pc.user_id, pc.canonical, pc.created_at,
                        (
                            (SELECT COUNT(*) FROM "Bookings" b WHERE b."ClientId" = pc.user_id) +
                            (SELECT COUNT(*) FROM "Bookings" b WHERE b."MasterId" = pc.user_id) +
                            (SELECT COUNT(*) FROM "CompanyMembers" cm WHERE cm."UserId" = pc.user_id)
                        ) AS row_count,
                        EXISTS (SELECT 1 FROM "Companies" c WHERE c."OwnerUserId" = pc.user_id) AS owns_company,
                        EXISTS (SELECT 1 FROM "Bookings" b WHERE b."MasterId" = pc.user_id) AS is_master_with_bookings
                    FROM phone_canon pc;

                    -- One row per account, with rn=1 marking the deterministic winner of its collision
                    -- group (ties broken by earliest CreatedAt, then id for total determinism). Accounts
                    -- with an empty canonical (no phone at all — not reachable through this app's own
                    -- registration flow, but excluded defensively) never collide with anyone.
                    CREATE TEMP TABLE phone_group ON COMMIT DROP AS
                    SELECT pr.*,
                        CASE WHEN pr.canonical = '' THEN 1 ELSE
                            ROW_NUMBER() OVER (PARTITION BY pr.canonical ORDER BY pr.row_count DESC, pr.created_at ASC, pr.user_id ASC)
                        END AS rn
                    FROM phone_rank pr;

                    -- Stop-condition pass over every collision group, BEFORE anything is written, so a
                    -- violation aborts the whole migration with nothing touched (SPEC US-26 p.8). Checked
                    -- per LOSING account (rn > 1), not "2 or more in the group" — a single losing account
                    -- that owns a company, or is a master with bookings, is just as unsafe to delete as
                    -- two of them (see class doc for why these two are not reassigned like the others).
                    FOR rec IN
                        SELECT canonical FROM phone_group
                        WHERE canonical <> '' GROUP BY canonical HAVING COUNT(*) > 1
                    LOOP
                        SELECT string_agg(user_id, ', ') INTO bad_ids FROM phone_group
                        WHERE canonical = rec.canonical AND rn > 1 AND owns_company;
                        IF bad_ids IS NOT NULL THEN
                            RAISE EXCEPTION
                                'Phone normalization stopped: canonical number % would delete account(s) % which own a company (Companies.OwnerUserId) — resolve manually (reassign or transfer ownership), then re-run.',
                                rec.canonical, bad_ids;
                        END IF;

                        SELECT string_agg(user_id, ', ') INTO bad_ids FROM phone_group
                        WHERE canonical = rec.canonical AND rn > 1 AND is_master_with_bookings;
                        IF bad_ids IS NOT NULL THEN
                            RAISE EXCEPTION
                                'Phone normalization stopped: canonical number % would delete account(s) % which are a master with existing bookings (Bookings.MasterId) — resolve manually, then re-run.',
                                rec.canonical, bad_ids;
                        END IF;
                    END LOOP;

                    -- Maps every losing account to the winner of its collision group — used both to
                    -- reassign authorship below and to drive the DELETE itself, so the two can never
                    -- disagree about who is being merged into whom.
                    CREATE TEMP TABLE phone_merge ON COMMIT DROP AS
                    SELECT loser.user_id AS loser_id, winner.user_id AS winner_id
                    FROM phone_group loser
                    JOIN phone_group winner ON winner.canonical = loser.canonical AND winner.rn = 1
                    WHERE loser.rn > 1 AND loser.canonical <> '';

                    -- Reassign company-owned content authored by a losing account to the survivor —
                    -- these three are "who on the company's staff did this", not a fact about the
                    -- specific account identity (see class doc). No-op for accounts that authored none.
                    UPDATE "ClientNotes" cn SET "MasterId" = pm.winner_id
                    FROM phone_merge pm WHERE cn."MasterId" = pm.loser_id;

                    UPDATE "Reviews" r SET "MasterId" = pm.winner_id
                    FROM phone_merge pm WHERE r."MasterId" = pm.loser_id;

                    UPDATE "MailLogs" ml SET "SentById" = pm.winner_id
                    FROM phone_merge pm WHERE ml."SentById" = pm.loser_id;

                    -- Delete every losing account. By this point the only remaining RESTRICT references
                    -- (Companies.OwnerUserId, Bookings.MasterId) have already been ruled out above, and
                    -- the CASCADE/SET NULL ones need no further preparation.
                    DELETE FROM "AspNetUsers" u USING phone_merge pm WHERE u."Id" = pm.loser_id;

                    -- Canonicalize what's left. UPPER() on a digits-only string is a no-op — the
                    -- NormalizedUserName invariant Identity relies on is preserved for free.
                    UPDATE "AspNetUsers" u
                    SET "PhoneNumber" = pc.canonical,
                        "UserName" = pc.canonical,
                        "NormalizedUserName" = UPPER(pc.canonical)
                    FROM phone_canon pc
                    WHERE pc.user_id = u."Id" AND pc.canonical <> '';

                    UPDATE "Bookings" SET "GuestPhone" = pg_temp.canon_phone("GuestPhone")
                    WHERE "GuestPhone" IS NOT NULL;

                    UPDATE "ClientNotes" SET "GuestPhone" = pg_temp.canon_phone("GuestPhone")
                    WHERE "GuestPhone" IS NOT NULL;
                END $$;

                DROP FUNCTION pg_temp.canon_phone(text);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty, like DeduplicateWorkingHours before it: accounts this migration
            // deleted cannot be un-deleted, and the phones/authorship it rewrote in place have no
            // "original form" worth restoring (the whole point of the cycle is that the original form
            // was the problem).
        }
    }
}
