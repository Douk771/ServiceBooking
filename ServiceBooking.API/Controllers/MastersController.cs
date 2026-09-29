using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.ClientNotes;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Companies;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/masters")]
[Authorize]
public class MastersController(AppDbContext db, FileStorage storage) : ControllerBase
{
    // Notes shown per client are capped so a long-lived client's history can't turn this into an
    // unbounded fetch (NFR §9 p.6, ARCHITECTURE.md §21.5) — newest first, most useful ones kept.
    private const int NotesPerClient = 50;

    [HttpGet("clients")]
    public async Task<ActionResult<PagedResult<MasterClientDto>>> GetClients(
        [FromQuery] Guid companyId, [FromQuery] string? search, [FromQuery] int? page, [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var (currentPage, currentPageSize) = Pagination.Normalize(page, pageSize);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // Check the caller actually works in this company (Master or CompanyOwner) — a plain "any
        // membership row" check would also let a Client-role member through (CompanyMember.Role can be
        // Client too), even though the bookings query below is scoped to b.MasterId == userId and would
        // just come back empty for them. Matches the CompanyMembership convention used elsewhere.
        var isMember = await CompanyMembership.IsStaffAsync(db, companyId, userId);
        if (!isMember) return Forbid();
        // §389.2: salon-only route (rights first, kind second).
        if (await CompanyKindGuard.RejectShopAsync(db, companyId, ct) is { } shopRefusal) return shopRefusal;

        // Needed once, up front: whether the CALLER (not the notes' authors) is this company's owner —
        // decides `canDelete` on every note and photo below (decision Q16).
        var callerIsOwner = await CompanyMembership.IsOwnerAsync(db, companyId, userId);

        // Cycle 22 (§375 F1/F2, closes §9.17): the master's bookings are GROUPED IN SQL — one row per
        // client (registered by ClientId, guest by GuestPhone) with the last visit date and the visit
        // count — instead of loading every booking of the master in the company (with Client and Service)
        // to group in memory. Booking summaries and notes are then read only for the clients on the
        // requested page.
        var masterBookings = db.Bookings.AsNoTracking()
            .Where(b => b.MasterId == userId && b.CompanyId == companyId);

        // Registered clients. Name/phone/email come from the client's own row — the same row the old
        // in-memory code reached through lastBooking.Client (every booking of a group has the same
        // ClientId); a missing row still yields "Unknown" and nulls.
        var registered = await (
            from g in masterBookings
                .Where(b => b.ClientId != null)
                .GroupBy(b => b.ClientId!)
                .Select(g => new { ClientId = g.Key, LastVisitDate = g.Max(b => b.Date), TotalVisits = g.Count() })
            join u in db.Users on g.ClientId equals u.Id into users
            from u in users.DefaultIfEmpty()
            select new
            {
                g.ClientId,
                g.LastVisitDate,
                g.TotalVisits,
                HasUser = u != null,
                FirstName = u != null ? u.FirstName : null,
                LastName = u != null ? u.LastName : null,
                Phone = u != null ? u.PhoneNumber : null,
                Email = u != null ? u.Email : null,
                PhoneVerified = u != null ? (bool?)u.PhoneNumberConfirmed : null,
            })
            .ToListAsync(ct);

        // Guests, keyed by phone. Name/email are the LAST visit's (latest Date, then latest EndTime) —
        // exactly what the in-memory lastBooking picked.
        var guests = await masterBookings
            .Where(b => b.ClientId == null && b.GuestPhone != null)
            .GroupBy(b => b.GuestPhone!)
            .Select(g => new
            {
                GuestPhone = g.Key,
                LastVisitDate = g.Max(b => b.Date),
                TotalVisits = g.Count(),
                Latest = g.OrderByDescending(b => b.Date).ThenByDescending(b => b.EndTime)
                    .Select(b => new { b.GuestName, b.GuestEmail })
                    .First(),
            })
            .ToListAsync(ct);

        var rows = registered
            .Select(r => new ClientRow(
                ClientId: r.ClientId,
                GuestPhone: null,
                Name: r.HasUser ? $"{r.FirstName} {r.LastName}".Trim() : "Unknown",
                // The "contact visible only 24h after visit" rule (decision Q10, cycle A) is removed:
                // it was half-implemented (no re-hide, no UI to unlock early) and served no
                // protection — a master who serves a client already has their phone/notes from the
                // booking flow.
                Phone: r.Phone,
                Email: r.Email,
                LastVisitDate: r.LastVisitDate,
                TotalVisits: r.TotalVisits,
                // ARCHITECTURE_CYCLE14.md §149.2 (Q17): read off the same user row as the name/phone.
                PhoneVerified: r.PhoneVerified))
            .Concat(guests.Select(g => new ClientRow(
                ClientId: null,
                GuestPhone: g.GuestPhone,
                Name: g.Latest.GuestName ?? "Guest",
                Phone: g.GuestPhone,
                Email: g.Latest.GuestEmail,
                LastVisitDate: g.LastVisitDate,
                TotalVisits: g.TotalVisits,
                // §149.2: a guest with no account — "not applicable", not "unverified". null, never false.
                PhoneVerified: null)))
            // US-49: ordered by last visit date DESC, tie-broken by the same client key GetClients/AddNote
            // use everywhere else: registered client id, or guest phone. Ordering, search and the page
            // slice run over these per-CLIENT rows in memory on purpose (cycle 22 decision, §375 F1): the
            // tie-break uses .NET's string comparer and the name search is OrdinalIgnoreCase — Postgres'
            // ORDER BY/ILIKE depend on the database collation/ctype and are not guaranteed to agree, and
            // the response must stay byte-identical (CY22-06…08).
            .OrderByDescending(c => c.LastVisitDate)
            .ThenBy(c => c.ClientId ?? c.GuestPhone)
            .ToList();

        // US-49 regression fix (QA cycle C): search must filter the FULL client list before pagination,
        // not just the page the frontend happens to already have in hand.
        //
        // Same phone-vs-name heuristic as AdminController.GetUsers (ARCHITECTURE.md §11.2): a search
        // string that looks like a phone number is normalized the way phones are stored, so
        // "+7 999 123-45-67", "8 999 123 45 67" and "79991234567" all match the same canonical client.
        // Anything else is matched against the client's display name, case-insensitively.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var phoneSearch = PhoneNormalizer.ParseSearch(search).Term;

            rows = rows.Where(c =>
                c.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (phoneSearch.Length > 0 && c.Phone is not null && c.Phone.Contains(phoneSearch)))
                .ToList();
        }

        var total = rows.Count;
        var pageRows = rows.Skip((currentPage - 1) * currentPageSize).Take(currentPageSize).ToList();

        var pageClientIds = pageRows.Where(r => r.ClientId != null).Select(r => r.ClientId!).ToArray();
        var pageGuestPhones = pageRows.Where(r => r.ClientId == null).Select(r => r.GuestPhone!).ToArray();

        // Booking summaries — for the page's clients only.
        var summaries = pageRows.Count == 0
            ? []
            : await masterBookings
                .Where(b => (b.ClientId != null && pageClientIds.Contains(b.ClientId))
                    || (b.ClientId == null && b.GuestPhone != null && pageGuestPhones.Contains(b.GuestPhone)))
                .Select(b => new { b.ClientId, b.GuestPhone, b.Date, b.StartTime, ServiceName = b.Service.Name, b.Status })
                .ToListAsync(ct);
        var summariesByClient = summaries.Where(b => b.ClientId != null).ToLookup(b => b.ClientId!);
        var summariesByGuest = summaries.Where(b => b.ClientId == null).ToLookup(b => b.GuestPhone!);

        // Notes are shared across the whole company: a master seeing a client (e.g. a new booking to a
        // master this client hasn't visited before) sees prior notes written by ANY colleague in the
        // same company — not just their own. Scoped to companyId so notes don't leak between businesses.
        //
        // The Take(NotesPerClient) cap is applied HERE, in the query, via a ROW_NUMBER() window
        // partitioned per client/guest — not by pulling every note (and every attached photo) the
        // company has ever accumulated into memory and slicing afterwards in NotesFor(...) below (code
        // review finding: a salon with a couple of years of history could have thousands of rows here).
        // COALESCE(ClientId, GuestPhone) mirrors exactly how the two grouping branches above identify a
        // "client" — a registered client by id, a guest by phone. Cycle 22 (§375 F1): the ranking still
        // runs over the whole company (unchanged partitions), but only the page's clients' notes are
        // returned — the filter sits OUTSIDE the window, matching the per-client lookups below.
        var notes = pageRows.Count == 0
            ? []
            : await db.ClientNotes
                .FromSqlInterpolated($"""
                    SELECT ranked.* FROM (
                        SELECT cn.*,
                               ROW_NUMBER() OVER (
                                   PARTITION BY COALESCE(cn."ClientId", cn."GuestPhone")
                                   ORDER BY cn."CreatedAt" DESC
                               ) AS "Rn"
                        FROM "ClientNotes" cn
                        WHERE cn."CompanyId" = {companyId}
                    ) ranked
                    WHERE ranked."Rn" <= {NotesPerClient}
                      AND (ranked."ClientId" = ANY({pageClientIds}) OR ranked."GuestPhone" = ANY({pageGuestPhones}))
                    """)
                .AsNoTracking()
                .Include(n => n.Master)
                .Include(n => n.Booking).ThenInclude(b => b!.Service)
                .Include(n => n.Photos).ThenInclude(p => p.UploadedBy)
                .ToListAsync(ct);
        var notesByClient = notes.Where(n => n.ClientId != null).ToLookup(n => n.ClientId!);
        var notesByGuest = notes.Where(n => n.GuestPhone != null).ToLookup(n => n.GuestPhone!);  // SUBJECT-PHONE-GATE: staff-scoped — company staff viewing THEIR OWN company's clients, not an account-scoped "my own data" query; no sewing of guest↔account identity happens here (ARCHITECTURE_CYCLE16.md §245.3)

        // The query above already caps each client/guest at NotesPerClient rows — this Take is a cheap,
        // redundant safety net (in case a future refactor of the query above ever loses the window-
        // function limit), not the actual bound.
        List<ClientNoteDto> NotesFor(IEnumerable<ClientNote> matching) => matching
            .OrderByDescending(n => n.CreatedAt)
            .Take(NotesPerClient)
            .Select(n => MapNoteToDto(n, userId, callerIsOwner))
            .ToList();

        var pageItems = pageRows.Select(r =>
        {
            var visits = r.ClientId != null ? summariesByClient[r.ClientId] : summariesByGuest[r.GuestPhone!];
            return new MasterClientDto(
                ClientId: r.ClientId,
                GuestPhone: r.GuestPhone,
                Name: r.Name,
                Phone: r.Phone,
                Email: r.Email,
                LastVisitDate: r.LastVisitDate,
                TotalVisits: r.TotalVisits,
                Notes: NotesFor(r.ClientId != null ? notesByClient[r.ClientId] : notesByGuest[r.GuestPhone!]),
                BookingSummaries: visits.OrderByDescending(b => b.Date).ThenByDescending(b => b.StartTime)
                    .Select(b => new BookingSummaryDto(b.Date, b.ServiceName, b.Status.ToString()))
                    .ToList(),
                PhoneVerified: r.PhoneVerified);
        }).ToList();

        return Ok(Pagination.Create(pageItems, currentPage, currentPageSize, total));
    }

    /// <summary>One client of <see cref="GetClients"/> before its page's notes and summaries are read.</summary>
    private sealed record ClientRow(
        string? ClientId, string? GuestPhone, string Name, string? Phone, string? Email,
        DateOnly LastVisitDate, int TotalVisits, bool? PhoneVerified);

    [HttpPost("clients/notes")]
    [RequiresOwnerTerms]
    public async Task<IActionResult> AddNote([FromBody] AddNoteRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // The author must actually work here (Master or CompanyOwner) — tightened from "any membership
        // row" (which let a Client-role member of the company seed notes into its shared client history)
        // to match the CompanyMembership convention used everywhere else (US-07 p.2).
        var isStaff = await CompanyMembership.IsStaffAsync(db, request.CompanyId, userId);
        if (!isStaff) return Forbid();
        // §389.2: salon-only route (rights first, kind second).
        if (await CompanyKindGuard.RejectShopAsync(db, request.CompanyId) is { } shopRefusal) return shopRefusal;

        if (string.IsNullOrWhiteSpace(request.Note))
            return BadRequest("Note text is required.");
        if (request.Note.Length > 2000)
            return BadRequest("Note must be 2000 characters or fewer.");

        var guestPhone = request.GuestPhone;
        if (!string.IsNullOrWhiteSpace(guestPhone))
        {
            if (!PhoneNormalizer.TryNormalize(guestPhone, out var canonicalGuestPhone))
                return BadRequest("Phone number must contain 10 to 15 digits.");
            guestPhone = canonicalGuestPhone;
        }

        // A note with neither identifier attaches to nothing GetClients can ever group it under (that
        // query only surfaces notes via COALESCE(ClientId, GuestPhone) over this company's bookings) —
        // it would be written successfully and then be permanently invisible (code review finding).
        if (string.IsNullOrWhiteSpace(request.ClientId) && string.IsNullOrWhiteSpace(guestPhone))
            return BadRequest("Either clientId or guestPhone is required.");

        if (!string.IsNullOrWhiteSpace(request.ClientId))
        {
            // ClientId is caller-supplied and otherwise unchecked — without this, any staff member could
            // attach a note to an arbitrary AppUser id who has never booked with this company (code
            // review finding). "Associated with the company" mirrors exactly how GetClients above decides
            // who counts as this company's registered client: at least one booking here.
            var clientHasBookingHere = await db.Bookings
                .AnyAsync(b => b.CompanyId == request.CompanyId && b.ClientId == request.ClientId);
            if (!clientHasBookingHere)
                return BadRequest("Client is not associated with this company.");
        }

        if (request.BookingId is { } bookingId)
        {
            var bookingCompanyId = await db.Bookings
                .Where(b => b.Id == bookingId)
                .Select(b => (Guid?)b.CompanyId)
                .FirstOrDefaultAsync();
            if (bookingCompanyId != request.CompanyId)
                return BadRequest("Booking does not belong to this company.");
        }

        var note = new ClientNote
        {
            Id = Guid.NewGuid(),
            CompanyId = request.CompanyId,
            MasterId = userId,
            ClientId = request.ClientId,
            GuestPhone = guestPhone,
            Note = request.Note,
            BookingId = request.BookingId,
            CreatedAt = DateTime.UtcNow
        };

        db.ClientNotes.Add(note);
        await db.SaveChangesAsync();

        var author = await db.Users.FindAsync(userId);
        note.Master = author!;
        // BookingId is validated above but the navigation isn't loaded by SaveChangesAsync — fetch it
        // so the response can include bookingDate/bookingServiceName (API_CONTRACT.md §2.1) without a
        // second round trip to the client.
        if (request.BookingId is { } linkedBookingId)
            note.Booking = await db.Bookings.Include(b => b.Service).FirstOrDefaultAsync(b => b.Id == linkedBookingId);

        // A freshly-created note is always deletable by its own author, and never has photos yet.
        return StatusCode(StatusCodes.Status201Created, MapNoteToDto(note, userId, callerIsOwner: false));
    }

    [HttpDelete("clients/notes/{id:guid}")]
    public async Task<IActionResult> DeleteNote(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var note = await db.ClientNotes
            .Include(n => n.Photos)
            .FirstOrDefaultAsync(n => n.Id == id);

        // Cross-tenant isolation (decision, ARCHITECTURE.md §21.4): a caller who isn't staff of this
        // note's company gets 404 either way, so the existence of someone else's note is never
        // confirmed by a 403 instead. A caller who IS staff here but not the author/owner gets 403 —
        // that rule is meant to be visible and testable, not hidden behind a blanket 404.
        if (note is null) return NotFound();
        var isStaffHere = await CompanyMembership.IsStaffAsync(db, note.CompanyId, userId);
        if (!isStaffHere) return NotFound();

        var isAuthor = note.MasterId == userId;
        var isOwner = await CompanyMembership.IsOwnerAsync(db, note.CompanyId, userId);
        if (!isAuthor && !isOwner) return Forbid();

        var photoKeys = note.Photos.Select(p => (p.StoragePath, p.ThumbnailPath)).ToList();

        db.ClientNotes.Remove(note); // cascades ClientNotePhotos rows (AppDbContext)
        await db.SaveChangesAsync();

        // Files are deleted only AFTER the DB commit succeeds (ARCHITECTURE.md §1.4): the worst outcome
        // of a crash between the two steps is an orphaned file, which the retention cleanup task picks
        // up later — a live row pointing at a missing file is the state that must never happen.
        foreach (var (full, thumb) in photoKeys)
        {
            storage.DeletePrivate(full);
            storage.DeletePrivate(thumb);
        }

        return NoContent();
    }

    private static ClientNoteDto MapNoteToDto(ClientNote n, string callerId, bool callerIsOwner)
    {
        var noteCanDelete = callerIsOwner || n.MasterId == callerId;
        var authorName = n.Master is not null ? $"{n.Master.FirstName} {n.Master.LastName}".Trim() : "";

        var photos = n.Photos
            .OrderBy(p => p.CreatedAt)
            .Select(p => new ClientNotePhotoDto(
                p.Id,
                $"/api/client-notes/photos/{p.Id}",
                $"/api/client-notes/photos/{p.Id}/thumb",
                p.Width, p.Height, p.SizeBytes, p.CreatedAt,
                p.UploadedBy is not null ? $"{p.UploadedBy.FirstName} {p.UploadedBy.LastName}".Trim() : null,
                CanDelete: noteCanDelete || p.UploadedByUserId == callerId))
            .ToList();

        return new ClientNoteDto(
            n.Id, n.Note, n.CreatedAt, n.MasterId, authorName,
            n.BookingId, n.Booking?.Date, n.Booking?.Service?.Name,
            noteCanDelete, photos);
    }
}
