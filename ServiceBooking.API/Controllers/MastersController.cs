using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.ClientNotes;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
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
        [FromQuery] Guid companyId, [FromQuery] string? search, [FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var (currentPage, currentPageSize) = Pagination.Normalize(page, pageSize);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // Check the caller actually works in this company (Master or CompanyOwner) — a plain "any
        // membership row" check would also let a Client-role member through (CompanyMember.Role can be
        // Client too), even though the bookings query below is scoped to b.MasterId == userId and would
        // just come back empty for them. Matches the CompanyMembership convention used elsewhere.
        var isMember = await CompanyMembership.IsStaffAsync(db, companyId, userId);
        if (!isMember) return Forbid();

        // Needed once, up front: whether the CALLER (not the notes' authors) is this company's owner —
        // decides `canDelete` on every note and photo below (decision Q16).
        var callerIsOwner = await CompanyMembership.IsOwnerAsync(db, companyId, userId);

        // Get all bookings for this master in this company
        var bookings = await db.Bookings
            .Include(b => b.Client)
            .Include(b => b.Service)
            .Where(b => b.MasterId == userId && b.CompanyId == companyId)
            .ToListAsync();

        // Notes are shared across the whole company: a master seeing a client (e.g. a new booking to a
        // master this client hasn't visited before) sees prior notes written by ANY colleague in the
        // same company — not just their own. Scoped to companyId so notes don't leak between businesses.
        //
        // The Take(NotesPerClient) cap is applied HERE, in the query, via a ROW_NUMBER() window
        // partitioned per client/guest — not by pulling every note (and every attached photo) the
        // company has ever accumulated into memory and slicing afterwards in NotesFor(...) below (code
        // review finding: a salon with a couple of years of history could have thousands of rows here).
        // COALESCE(ClientId, GuestPhone) mirrors exactly how the two grouping branches below identify a
        // "client" — a registered client by id, a guest by phone.
        var notes = await db.ClientNotes
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
                """)
            .Include(n => n.Master)
            .Include(n => n.Booking).ThenInclude(b => b!.Service)
            .Include(n => n.Photos).ThenInclude(p => p.UploadedBy)
            .ToListAsync();

        // The query above already caps each client/guest at NotesPerClient rows — this Take is a cheap,
        // redundant safety net (in case a future refactor of the query above ever loses the window-
        // function limit), not the actual bound.
        List<ClientNoteDto> NotesFor(IEnumerable<ClientNote> matching) => matching
            .OrderByDescending(n => n.CreatedAt)
            .Take(NotesPerClient)
            .Select(n => MapNoteToDto(n, userId, callerIsOwner))
            .ToList();

        // Group by registered clients
        var registeredClients = bookings
            .Where(b => b.ClientId != null)
            .GroupBy(b => b.ClientId!)
            .Select(g =>
            {
                var lastBooking = g.OrderByDescending(b => b.Date).ThenByDescending(b => b.EndTime).First();
                var client = lastBooking.Client;
                return new MasterClientDto(
                    ClientId: g.Key,
                    GuestPhone: null,
                    Name: client != null ? $"{client.FirstName} {client.LastName}".Trim() : "Unknown",
                    // The "contact visible only 24h after visit" rule (decision Q10, cycle A) is removed:
                    // it was half-implemented (no re-hide, no UI to unlock early) and served no
                    // protection — a master who serves a client already has their phone/notes from the
                    // booking flow.
                    Phone: client?.PhoneNumber,
                    Email: client?.Email,
                    LastVisitDate: lastBooking.Date,
                    TotalVisits: g.Count(),
                    Notes: NotesFor(notes.Where(n => n.ClientId == g.Key)),
                    BookingSummaries: g.OrderByDescending(b => b.Date).ThenByDescending(b => b.StartTime)
                        .Select(b => new BookingSummaryDto(b.Date, b.Service.Name, b.Status.ToString()))
                        .ToList(),
                    // ARCHITECTURE_CYCLE14.md §149.2 (Q17): ZERO extra queries — Client is already
                    // Include()d above, so PhoneNumberConfirmed rides along with the name/phone that were
                    // already being read from the same row.
                    PhoneVerified: client?.PhoneNumberConfirmed
                );
            });

        // Group by guest phone
        var guestClients = bookings
            .Where(b => b.ClientId == null && b.GuestPhone != null)
            .GroupBy(b => b.GuestPhone!)
            .Select(g =>
            {
                var lastBooking = g.OrderByDescending(b => b.Date).ThenByDescending(b => b.EndTime).First();
                return new MasterClientDto(
                    ClientId: null,
                    GuestPhone: g.Key,
                    Name: lastBooking.GuestName ?? "Guest",
                    Phone: g.Key,
                    Email: lastBooking.GuestEmail,
                    LastVisitDate: lastBooking.Date,
                    TotalVisits: g.Count(),
                    Notes: NotesFor(notes.Where(n => n.GuestPhone == g.Key)),  // SUBJECT-PHONE-GATE: staff-scoped — company staff viewing THEIR OWN company's clients, not an account-scoped "my own data" query; no sewing of guest↔account identity happens here (ARCHITECTURE_CYCLE16.md §245.3)
                    BookingSummaries: g.OrderByDescending(b => b.Date).ThenByDescending(b => b.StartTime)
                        .Select(b => new BookingSummaryDto(b.Date, b.Service.Name, b.Status.ToString()))
                        .ToList(),
                    // §149.2: a guest with no account — "not applicable", not "unverified". null, never false.
                    PhoneVerified: null
                );
            });

        // US-49: pagination applies to THIS list — the clients grouped from the master's bookings in
        // this company, already fully materialized above (existing shape, not restructured by this
        // cycle) — not to the ROW_NUMBER() note-capping query above it, which stays exactly as it was
        // (ARCHITECTURE.md §15: "GET /api/masters/clients содержит ROW_NUMBER()-запрос через
        // FromSqlInterpolated" is a warning about not disturbing that query, not a description of how
        // pagination itself is implemented). Ordered by last visit date DESC, tie-broken by the same
        // client key GetClients/AddNote use everywhere else: registered client id, or guest phone.
        var allClients = registeredClients.Concat(guestClients)
            .OrderByDescending(c => c.LastVisitDate)
            .ThenBy(c => c.ClientId ?? c.GuestPhone)
            .ToList();

        // US-49 regression fix (QA cycle C): search must filter the FULL client list before pagination,
        // not just the page the frontend happens to already have in hand — otherwise a client on page 3
        // is simply invisible to a search typed on page 1. This list is already fully materialized in
        // memory above (existing shape, see comment on the block above), so filtering here is a plain
        // LINQ-to-objects Where, not a second SQL round trip.
        //
        // Same phone-vs-name heuristic as AdminController.GetUsers (ARCHITECTURE.md §11.2): a search
        // string that looks like a phone number (≥5 digits, no letters) is normalized through
        // PhoneNormalizer the same way phones are stored, so "+7 999 123-45-67", "8 999 123 45 67" and
        // "79991234567" all match the same canonical client regardless of how the caller typed it.
        // Anything else is matched against the client's display name, case-insensitively.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var digitCount = search.Count(char.IsDigit);
            var looksLikePhone = digitCount >= 5 && !search.Any(char.IsLetter);
            var phoneSearch = looksLikePhone ? PhoneNormalizer.Normalize(search) : search;

            allClients = allClients.Where(c =>
                c.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (phoneSearch.Length > 0 && c.Phone is not null && c.Phone.Contains(phoneSearch)))
                .ToList();
        }

        var total = allClients.Count;
        var pageItems = allClients.Skip((currentPage - 1) * currentPageSize).Take(currentPageSize).ToList();

        return Ok(Pagination.Create(pageItems, currentPage, currentPageSize, total));
    }

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
