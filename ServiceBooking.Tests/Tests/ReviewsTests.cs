using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

public class ReviewsTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    private record CreateReviewResponse(Guid Id);

    private record CompanyReviewEntry(
        Guid Id, int Rating, string? Comment, string? ReviewerName,
        string MasterName, string ServiceName, DateTime CreatedAt);

    /// <summary>
    /// Full setup: owner + company + master + service + working day + a client who books and
    /// whose booking is then marked Completed by the master. Returns everything a review test needs.
    /// </summary>
    private async Task<(AuthResponseDto Owner, ServiceBooking.API.DTOs.Companies.CompanyDto Company, AuthResponseDto Master, AuthResponseDto ClientUser, BookingDto Booking)>
        CreateCompletedBookingAsync()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var createResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = (await createResponse.Content.ReadJsonAsync<BookingDto>())!;

        var completeResponse = await AuthedClient(master.Token).PatchAsync($"/api/bookings/{booking.Id}/complete", null);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        return (owner, company, master, clientUser, booking);
    }

    // ── POST /api/reviews ────────────────────────────────────────────────────

    [Fact, TestCase("RV-001")]
    public async Task Create_ForCompletedBookingByBookingOwner_Succeeds()
    {
        var (_, _, _, clientUser, booking) = await CreateCompletedBookingAsync();

        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/reviews",
            new CreateReviewRequest(booking.Id, 5, "Great service"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CreateReviewResponse>();
        body!.Id.Should().NotBeEmpty();
    }

    [Fact, TestCase("RV-002")]
    public async Task Create_ForPendingBooking_ReturnsBadRequest()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var createResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        var booking = (await createResponse.Content.ReadJsonAsync<BookingDto>())!;

        // Booking was just created — Status is Confirmed, not Completed.
        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/reviews",
            new CreateReviewRequest(booking.Id, 4, "Too early"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("RV-003")]
    public async Task Create_ForUnknownBookingId_ReturnsNotFound()
    {
        var clientUser = await RegisterAsync();

        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/reviews",
            new CreateReviewRequest(Guid.NewGuid(), 3, "No such booking"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("RV-004")]
    public async Task Create_TwiceForSameBooking_ReturnsConflictOnSecond()
    {
        var (_, _, _, clientUser, booking) = await CreateCompletedBookingAsync();

        var first = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/reviews",
            new CreateReviewRequest(booking.Id, 5, "First review"));
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/reviews",
            new CreateReviewRequest(booking.Id, 2, "Second attempt"));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("RV-005")]
    public async Task Create_ByDifferentClientThanBookingOwner_ReturnsForbidden()
    {
        var (_, _, _, _, booking) = await CreateCompletedBookingAsync();
        // booking.ClientId is set (a real, non-guest booking) — a different authenticated client tries to review it.
        var otherClient = await RegisterAsync();

        var response = await AuthedClient(otherClient.Token).PostAsJsonAsync("/api/reviews",
            new CreateReviewRequest(booking.Id, 1, "Not my booking"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory, TestCase("RV-006")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(100)]
    public async Task Create_WithRatingOutsideOneToFive_ReturnsBadRequest(int rating)
    {
        var (_, _, _, clientUser, booking) = await CreateCompletedBookingAsync();

        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/reviews",
            new CreateReviewRequest(booking.Id, rating, "Out of range"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory, TestCase("RV-007")]
    [InlineData(1)]
    [InlineData(5)]
    public async Task Create_WithRatingAtBoundary_Succeeds(int rating)
    {
        var (_, _, _, clientUser, booking) = await CreateCompletedBookingAsync();

        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/reviews",
            new CreateReviewRequest(booking.Id, rating, "Boundary value"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── GET /api/reviews/can-review ──────────────────────────────────────────

    [Fact, TestCase("RV-008")]
    public async Task CanReview_ForBrandNewUser_ReturnsEmptyList()
    {
        var user = await RegisterAsync();

        var response = await AuthedClient(user.Token).GetAsync("/api/reviews/can-review");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ids = await response.Content.ReadFromJsonAsync<List<Guid>>();
        ids.Should().BeEmpty();
    }

    [Fact, TestCase("RV-009")]
    public async Task CanReview_AfterCompletedBooking_ContainsBookingId_ThenDisappearsAfterReview()
    {
        var (_, _, _, clientUser, booking) = await CreateCompletedBookingAsync();

        var beforeResponse = await AuthedClient(clientUser.Token).GetAsync("/api/reviews/can-review");
        var before = await beforeResponse.Content.ReadFromJsonAsync<List<Guid>>();
        before.Should().Contain(booking.Id);

        var reviewResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/reviews",
            new CreateReviewRequest(booking.Id, 5, "Loved it"));
        reviewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterResponse = await AuthedClient(clientUser.Token).GetAsync("/api/reviews/can-review");
        var after = await afterResponse.Content.ReadFromJsonAsync<List<Guid>>();
        after.Should().NotContain(booking.Id);
    }

    // ── GET /api/companies/{companyId}/reviews ───────────────────────────────

    [Fact, TestCase("RV-010")]
    public async Task GetCompanyReviews_ForCompanyWithNoReviews_ReturnsEmptyArray()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();

        var response = await AnonymousClient().GetAsync($"/api/companies/{company.Id}/reviews");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var reviews = await response.Content.ReadFromJsonAsync<List<CompanyReviewEntry>>();
        reviews.Should().BeEmpty();
    }

    [Fact, TestCase("RV-011")]
    public async Task GetCompanyReviews_IsPublic_AndOrdersNewestFirst()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        // First completed booking + review.
        var client1 = await RegisterAsync();
        var create1 = await AuthedClient(client1.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        var booking1 = (await create1.Content.ReadJsonAsync<BookingDto>())!;
        (await AuthedClient(master.Token).PatchAsync($"/api/bookings/{booking1.Id}/complete", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var review1 = await AuthedClient(client1.Token).PostAsJsonAsync("/api/reviews",
            new CreateReviewRequest(booking1.Id, 3, "First"));
        review1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Small delay so the second review's CreatedAt is strictly later.
        await Task.Delay(50);

        // Second completed booking + review, on a different slot the same day.
        var client2 = await RegisterAsync();
        var create2 = await AuthedClient(client2.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0), null, null, null, null, null));
        var booking2 = (await create2.Content.ReadJsonAsync<BookingDto>())!;
        (await AuthedClient(master.Token).PatchAsync($"/api/bookings/{booking2.Id}/complete", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var review2 = await AuthedClient(client2.Token).PostAsJsonAsync("/api/reviews",
            new CreateReviewRequest(booking2.Id, 5, "Second"));
        review2.StatusCode.Should().Be(HttpStatusCode.OK);

        // No auth header at all — endpoint must be public.
        var response = await AnonymousClient().GetAsync($"/api/companies/{company.Id}/reviews");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var reviews = await response.Content.ReadFromJsonAsync<List<CompanyReviewEntry>>();
        reviews.Should().HaveCount(2);
        reviews![0].Comment.Should().Be("Second");
        reviews[1].Comment.Should().Be("First");
        reviews[0].CreatedAt.Should().BeAfter(reviews[1].CreatedAt);
        reviews[0].MasterName.Should().Be($"{master.FirstName} {master.LastName}");
        reviews[0].ServiceName.Should().Be(service.Name);
    }

    [Fact, TestCase("RV-012")]
    public async Task GetCompanyReviews_ReturnsOnlyThatCompanysReviews_NotOthers()
    {
        // A review left at one company must never leak into another company's public review list.
        var (_, _, _, clientA, bookingA) = await CreateCompletedBookingAsync();
        var reviewA = await AuthedClient(clientA.Token).PostAsJsonAsync("/api/reviews",
            new CreateReviewRequest(bookingA.Id, 4, "Review for company A"));
        reviewA.StatusCode.Should().Be(HttpStatusCode.OK);

        var (_, companyB, _, clientB, bookingB) = await CreateCompletedBookingAsync();
        var reviewB = await AuthedClient(clientB.Token).PostAsJsonAsync("/api/reviews",
            new CreateReviewRequest(bookingB.Id, 2, "Review for company B"));
        reviewB.StatusCode.Should().Be(HttpStatusCode.OK);

        var reviews = await (await AnonymousClient().GetAsync($"/api/companies/{companyB.Id}/reviews"))
            .Content.ReadFromJsonAsync<List<CompanyReviewEntry>>();

        reviews.Should().ContainSingle();
        reviews![0].Comment.Should().Be("Review for company B");
        reviews.Should().NotContain(r => r.Comment == "Review for company A");
    }

    // ── Guest bookings never accept a review (audit A6) ──────────────────────

    [Fact, TestCase("RV-013")]
    public async Task Create_ForCompletedGuestBooking_ReturnsForbidden_ByAnyone()
    {
        // Guest bookings carry no client identity (ClientId is null), so nobody can prove they were
        // the visitor. Before the fix, `ClientId != null && ClientId != userId` skipped the ownership
        // check entirely for guest bookings, letting anyone who knew the bookingId (returned to the
        // guest by POST /api/bookings) post a review to a stranger's business in their own name.
        var (owner, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: true);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        await SetSubscriptionAsync(company.Id);

        var createResponse = await AnonymousClient().PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, "Guest Name", "+79990001122", null, null));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = (await createResponse.Content.ReadJsonAsync<BookingDto>())!;

        var completeResponse = await AuthedClient(master.Token).PatchAsync($"/api/bookings/{booking.Id}/complete", null);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var someUser = await RegisterAsync();
        var response = await AuthedClient(someUser.Token).PostAsJsonAsync("/api/reviews",
            new CreateReviewRequest(booking.Id, 5, "I was not even there"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
