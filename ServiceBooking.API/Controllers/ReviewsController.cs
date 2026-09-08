using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/reviews")]
public class ReviewsController(AppDbContext db) : ControllerBase
{
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateReviewRequest request)
    {
        if (request.Rating is < 1 or > 5) return BadRequest("Rating must be between 1 and 5.");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var booking = await db.Bookings
            .Include(b => b.Service)
            .FirstOrDefaultAsync(b => b.Id == request.BookingId);

        if (booking == null) return NotFound("Booking not found.");
        if (booking.Status != BookingStatus.Completed) return BadRequest("Booking is not completed.");
        // Guest bookings carry no client identity (ClientId is null for the offline/walk-in/manual-booking
        // flow), so nobody can prove they were the one who actually visited — the old
        // `booking.ClientId != null && ...` check skipped this comparison entirely for every guest
        // booking, letting anyone who learned the bookingId (it's returned to the guest by
        // POST /api/bookings) post a review to a stranger's business in their own name (audit A6).
        if (booking.ClientId != userId) return Forbid();

        var alreadyReviewed = await db.Reviews.AnyAsync(r => r.BookingId == request.BookingId);
        if (alreadyReviewed) return Conflict("Review already exists for this booking.");

        // The `booking.ClientId != userId` check above already proved this booking belongs to the
        // authenticated caller, so it can't be a guest booking (those have ClientId == null) — and
        // [Authorize] plus Program.cs's OnTokenValidated already proved `userId` names a real, currently
        // existing user. The old `user != null ? ... : booking.GuestName` fallback was unreachable dead
        // code that made a reader think guest reviews were still supported.
        var user = await db.Users.FindAsync(userId);

        var review = new Review
        {
            Id = Guid.NewGuid(),
            BookingId = request.BookingId,
            CompanyId = booking.CompanyId,
            MasterId = booking.MasterId,
            ClientId = booking.ClientId,
            ReviewerName = $"{user!.FirstName} {user.LastName}".Trim(),
            Rating = request.Rating,
            Comment = request.Comment,
            CreatedAt = DateTime.UtcNow
        };

        db.Reviews.Add(review);
        await db.SaveChangesAsync();

        return Ok(new { review.Id });
    }

    [HttpGet("can-review")]
    [Authorize]
    public async Task<IActionResult> CanReview()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var reviewedBookingIds = await db.Reviews
            .Where(r => r.ClientId == userId)
            .Select(r => r.BookingId)
            .ToListAsync();

        var bookingIds = await db.Bookings
            .Where(b => b.ClientId == userId
                && b.Status == BookingStatus.Completed
                && !reviewedBookingIds.Contains(b.Id))
            .Select(b => b.Id)
            .ToListAsync();

        return Ok(bookingIds);
    }
}

[ApiController]
[Route("api/companies")]
public class CompanyReviewsController(AppDbContext db) : ControllerBase
{
    [HttpGet("{companyId}/reviews")]
    public async Task<ActionResult<PagedResult<ReviewDto>>> GetCompanyReviews(
        Guid companyId, [FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var (currentPage, currentPageSize) = Pagination.Normalize(page, pageSize);
        var query = db.Reviews.Where(r => r.CompanyId == companyId);

        var total = await query.CountAsync();
        // US-49 p.6: CreatedAt DESC, then Id — the tie-break PostgreSQL needs to guarantee page 2 never
        // reshows a row page 1 already showed when two reviews share a timestamp.
        var reviews = await query
            .Include(r => r.Master)
            .Include(r => r.Booking).ThenInclude(b => b.Service)
            .OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id)
            .Skip((currentPage - 1) * currentPageSize).Take(currentPageSize)
            .Select(r => new ReviewDto(
                r.Id, r.Rating, r.Comment, r.ReviewerName,
                r.Master.FirstName + " " + r.Master.LastName, r.Booking.Service.Name, r.CreatedAt))
            .ToListAsync();

        return Ok(Pagination.Create(reviews, currentPage, currentPageSize, total));
    }
}

public record CreateReviewRequest(Guid BookingId, int Rating, string? Comment);

// Named record replacing the previous anonymous-object shape (US-49 BREAKING № 2, API_CONTRACT.md §11)
// — an anonymous type can't be the T in PagedResult<T>.
public record ReviewDto(
    Guid Id, int Rating, string? Comment, string? ReviewerName, string MasterName, string ServiceName, DateTime CreatedAt);
