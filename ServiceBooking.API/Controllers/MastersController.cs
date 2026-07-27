using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/masters")]
[Authorize]
public class MastersController(AppDbContext db) : ControllerBase
{
    [HttpGet("clients")]
    public async Task<IActionResult> GetClients([FromQuery] Guid companyId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // Check master is a member of the company
        var isMember = await db.CompanyMembers
            .AnyAsync(cm => cm.UserId == userId && cm.CompanyId == companyId);
        if (!isMember) return Forbid();

        var cutoff = DateTime.UtcNow.AddHours(-24);

        // Get all bookings for this master in this company
        var bookings = await db.Bookings
            .Include(b => b.Client)
            .Include(b => b.Service)
            .Where(b => b.MasterId == userId && b.CompanyId == companyId)
            .ToListAsync();

        // Notes are shared across the whole company: a master seeing a client (e.g. a new booking to a
        // master this client hasn't visited before) sees prior notes written by ANY colleague in the
        // same company — not just their own. Scoped to companyId so notes don't leak between businesses.
        var notes = await db.ClientNotes
            .Where(n => n.CompanyId == companyId)
            .ToListAsync();

        // Group by registered clients
        var registeredClients = bookings
            .Where(b => b.ClientId != null)
            .GroupBy(b => b.ClientId!)
            .Select(g =>
            {
                var lastBooking = g.OrderByDescending(b => b.Date).ThenByDescending(b => b.EndTime).First();
                var lastVisitEnd = lastBooking.Date.ToDateTime(lastBooking.EndTime);
                var showContact = lastVisitEnd.ToUniversalTime() >= cutoff;
                var client = lastBooking.Client;
                var clientNotes = notes.Where(n => n.ClientId == g.Key).Select(n => n.Note).ToList();
                return new
                {
                    clientId = g.Key,
                    guestPhone = (string?)null,
                    name = client != null ? $"{client.FirstName} {client.LastName}".Trim() : "Unknown",
                    phone = showContact ? client?.PhoneNumber : null,
                    email = showContact ? client?.Email : null,
                    lastVisitDate = lastBooking.Date,
                    totalVisits = g.Count(),
                    notes = clientNotes,
                    bookingSummaries = g.OrderByDescending(b => b.Date).ThenByDescending(b => b.StartTime)
                        .Select(b => new { date = b.Date, serviceName = b.Service.Name, status = b.Status.ToString() })
                        .ToList()
                };
            });

        // Group by guest phone
        var guestClients = bookings
            .Where(b => b.ClientId == null && b.GuestPhone != null)
            .GroupBy(b => b.GuestPhone!)
            .Select(g =>
            {
                var lastBooking = g.OrderByDescending(b => b.Date).ThenByDescending(b => b.EndTime).First();
                var lastVisitEnd = lastBooking.Date.ToDateTime(lastBooking.EndTime);
                var showContact = lastVisitEnd.ToUniversalTime() >= cutoff;
                var clientNotes = notes.Where(n => n.GuestPhone == g.Key).Select(n => n.Note).ToList();
                return new
                {
                    clientId = (string?)null,
                    guestPhone = g.Key,
                    name = lastBooking.GuestName ?? "Guest",
                    phone = showContact ? g.Key : null,
                    email = showContact ? lastBooking.GuestEmail : null,
                    lastVisitDate = lastBooking.Date,
                    totalVisits = g.Count(),
                    notes = clientNotes,
                    bookingSummaries = g.OrderByDescending(b => b.Date).ThenByDescending(b => b.StartTime)
                        .Select(b => new { date = b.Date, serviceName = b.Service.Name, status = b.Status.ToString() })
                        .ToList()
                };
            });

        var result = registeredClients.Cast<object>().Concat(guestClients.Cast<object>()).ToList();
        return Ok(result);
    }

    [HttpPost("clients/notes")]
    public async Task<IActionResult> AddNote([FromBody] AddNoteRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // The author must be a member of the company the note is filed under — otherwise anyone could
        // seed notes into a company's shared client history.
        var isMember = await db.CompanyMembers
            .AnyAsync(cm => cm.UserId == userId && cm.CompanyId == request.CompanyId);
        if (!isMember) return Forbid();

        var note = new ClientNote
        {
            Id = Guid.NewGuid(),
            CompanyId = request.CompanyId,
            MasterId = userId,
            ClientId = request.ClientId,
            GuestPhone = request.GuestPhone,
            Note = request.Note,
            CreatedAt = DateTime.UtcNow
        };

        db.ClientNotes.Add(note);
        await db.SaveChangesAsync();

        return Ok(new { note.Id });
    }

    [HttpDelete("clients/notes/{id:guid}")]
    public async Task<IActionResult> DeleteNote(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var note = await db.ClientNotes.FirstOrDefaultAsync(n => n.Id == id && n.MasterId == userId);
        if (note == null) return NotFound();

        db.ClientNotes.Remove(note);
        await db.SaveChangesAsync();

        return NoContent();
    }
}

public record AddNoteRequest(Guid CompanyId, string? ClientId, string? GuestPhone, string Note);
