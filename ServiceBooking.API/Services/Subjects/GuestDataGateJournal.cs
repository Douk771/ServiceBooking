using System.Diagnostics;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Subjects;

/// <summary>
/// ARCHITECTURE_CYCLE20.md §406.2 (US-20-05, Т20-06) — the single writer for
/// <see cref="GuestDataGateEvent"/>, replacing the four inline <c>logger.LogInformation("guest-data gate
/// applied: …")</c> calls in <see cref="Controllers.ProfileController"/> (the Information-level log line
/// itself is untouched — it carries no phone either and lives by the ordinary technical-log retention,
/// this journal is the NEW, separate, one-year record).
///
/// All four call sites invoke this BEFORE opening their own transaction (§406.2), so this method does its
/// own <c>SaveChangesAsync</c> rather than relying on a caller's later commit — the
/// <see cref="Debug.Assert(bool)"/> below is the documented, checked version of that precondition, not a
/// defensive guess. A failure here must never fail the subject's own operation (their rights matter more
/// than the completeness of a service-side journal), so any exception is caught and logged instead of
/// propagated.
/// </summary>
public class GuestDataGateJournal(AppDbContext db, ILogger<GuestDataGateJournal> logger)
{
    public async Task RecordAsync(string userId, GuestDataGateOperation operation, string? traceId = null, CancellationToken ct = default)
    {
        Debug.Assert(!db.ChangeTracker.HasChanges(),
            "GuestDataGateJournal.RecordAsync must run before the caller opens its own unit of work — " +
            "see this class's own doc comment (ARCHITECTURE_CYCLE20.md §406.2).");

        var entry = db.GuestDataGateEvents.Add(new GuestDataGateEvent
        {
            OccurredAtUtc = DateTime.UtcNow,
            UserId = userId,
            Operation = operation,
            Outcome = GuestDataGateOutcome.Applied,
            TraceId = traceId,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // §406.2 — a journal write failure must not block the subject's own operation. No PII: the
            // exception message itself must never carry a phone/name, which it can't here (this method
            // never receives one).
            logger.LogError(ex, "Failed to record guest-data-gate journal entry for operation {Operation}", operation);

            // Detach ONLY the row this method added — NOT ChangeTracker.Clear(), which would also
            // detach whatever the CALLER's own ASP.NET Identity/EF entities this shared, per-request
            // DbContext is already tracking (e.g. the AppUser UserManager loaded before calling here) and
            // break their later SaveChangesAsync with an "already tracked" conflict. On success, nothing
            // to clean up: the added row is simply Unchanged, like any other freshly-saved entity.
            entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        }
    }
}
