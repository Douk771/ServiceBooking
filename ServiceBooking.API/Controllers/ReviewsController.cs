using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        if (booking.ClientId != null && booking.ClientId != userId) return Forbid();

        var alreadyReviewed = await db.Reviews.AnyAsync(r => r.BookingId == request.BookingId);
        if (alreadyReviewed) return Conflict("Review already exists for this booking.");

        var user = await db.Users.FindAsync(userId);

        var review = new Review
        {
            Id = Guid.NewGuid(),
            BookingId = request.BookingId,
            CompanyId = booking.CompanyId,
            MasterId = booking.MasterId,
            ClientId = booking.ClientId,
            ReviewerName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : booking.GuestName,
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
    public async Task<IActionResult> GetCompanyReviews(Guid companyId)
    {
        var reviews = await db.Reviews
            .Include(r => r.Master)
            .Include(r => r.Booking)
                .ThenInclude(b => b.Service)
            .Where(r => r.CompanyId == companyId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id,
                r.Rating,
                r.Comment,
                r.ReviewerName,
                masterName = r.Master.FirstName + " " + r.Master.LastName,
                serviceName = r.Booking.Service.Name,
                r.CreatedAt
            })
            .ToListAsync();

        return Ok(reviews);
    }
}

public record CreateReviewRequest(Guid BookingId, int Rating, string? Comment);
