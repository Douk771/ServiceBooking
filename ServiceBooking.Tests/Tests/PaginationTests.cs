using System.Net.Http.Json;
using FluentAssertions;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.ClientNotes;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>US-49 (SPEC.md §6.3, ARCHITECTURE.md §15, API_CONTRACT.md §11) — the shared Paged&lt;T&gt; envelope
/// across the four selections it applies to.</summary>
public class PaginationTests(ApiDatabaseFixture fixture) : ApiTestBase(fixture)
{
    [Fact, TestCase("PAG-001")]
    public async Task AdminCompanies_SecondPage_DoesNotOverlapFirst_AndTotalsAreConsistent()
    {
        var tag = Unique("pagtag");
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);

        const int count = 25;
        for (var i = 0; i < count; i++)
        {
            var owner = await RegisterAsync();
            await CreateCompanyAsync(owner.Token, name: $"{tag} Company {i:D2}");
        }

        var page1 = await (await adminClient.GetAsync($"/api/admin/companies?search={tag}&page=1&pageSize=10"))
            .Content.ReadJsonAsync<PagedResult<AdminCompanyDto>>();
        var page2 = await (await adminClient.GetAsync($"/api/admin/companies?search={tag}&page=2&pageSize=10"))
            .Content.ReadJsonAsync<PagedResult<AdminCompanyDto>>();
        var page3 = await (await adminClient.GetAsync($"/api/admin/companies?search={tag}&page=3&pageSize=10"))
            .Content.ReadJsonAsync<PagedResult<AdminCompanyDto>>();

        page1!.Total.Should().Be(count);
        page1.Items.Should().HaveCount(10);
        page2!.Items.Should().HaveCount(10);
        page3!.Items.Should().HaveCount(5);

        var page1Ids = page1.Items.Select(c => c.Id).ToHashSet();
        var page2Ids = page2.Items.Select(c => c.Id).ToHashSet();
        var page3Ids = page3.Items.Select(c => c.Id).ToHashSet();
        page1Ids.Intersect(page2Ids).Should().BeEmpty("page 2 must not reshow anything page 1 already showed");
        page2Ids.Intersect(page3Ids).Should().BeEmpty();
        page1Ids.Intersect(page3Ids).Should().BeEmpty();
        (page1Ids.Count + page2Ids.Count + page3Ids.Count).Should().Be(count, "every row must appear exactly once across all pages");

        page1.HasNext.Should().BeTrue();
        page2.HasNext.Should().BeTrue();
        page3.HasNext.Should().BeFalse("the last page must report no next page");
    }

    [Fact, TestCase("PAG-002")]
    public async Task AdminCompanies_PageSizeAboveCeiling_IsSilentlyClampedTo100_NotRejected()
    {
        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).GetAsync("/api/admin/companies?pageSize=99999");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, "US-49 п.6: an oversized pageSize is clamped, not a 400");
        var page = await response.Content.ReadJsonAsync<PagedResult<AdminCompanyDto>>();
        page!.PageSize.Should().Be(100);
    }

    [Fact, TestCase("PAG-003")]
    public async Task AdminUsers_NoQueryParameters_DefaultsToPageOneSizeTwenty()
    {
        var admin = await LoginAsSuperAdminAsync();
        var page = await (await AuthedClient(admin.Token).GetAsync("/api/admin/users"))
            .Content.ReadJsonAsync<PagedResult<AdminUserDto>>();
        page!.Page.Should().Be(1);
        page.PageSize.Should().Be(20);
        page.Items.Should().HaveCountLessOrEqualTo(20);
    }

    [Fact, TestCase("PAG-004")]
    public async Task AdminUsers_SecondPage_DoesNotOverlapFirst()
    {
        var tag = Unique("useremailtag");
        var admin = await LoginAsSuperAdminAsync();

        const int count = 12;
        var createdIds = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var user = await RegisterAsync(email: $"{tag}{i}@test.local");
            createdIds.Add(user.UserId);
        }

        var page1 = await (await AuthedClient(admin.Token).GetAsync($"/api/admin/users?search={tag}&page=1&pageSize=5"))
            .Content.ReadJsonAsync<PagedResult<AdminUserDto>>();
        var page2 = await (await AuthedClient(admin.Token).GetAsync($"/api/admin/users?search={tag}&page=2&pageSize=5"))
            .Content.ReadJsonAsync<PagedResult<AdminUserDto>>();
        var page3 = await (await AuthedClient(admin.Token).GetAsync($"/api/admin/users?search={tag}&page=3&pageSize=5"))
            .Content.ReadJsonAsync<PagedResult<AdminUserDto>>();

        page1!.Total.Should().Be(count);
        var allIds = page1.Items.Select(u => u.Id)
            .Concat(page2!.Items.Select(u => u.Id))
            .Concat(page3!.Items.Select(u => u.Id))
            .ToList();
        allIds.Should().OnlyHaveUniqueItems("no user should appear on two different pages");
        allIds.Should().BeEquivalentTo(createdIds);
    }

    [Fact, TestCase("PAG-005")]
    public async Task CompanyReviews_SecondPage_DoesNotOverlapFirst()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);

        const int count = 7;
        for (var i = 0; i < count; i++)
        {
            var date = NextWeekday().AddDays(i); // distinct days, one company-wide working-hours/booking per day
            await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
            var client = await RegisterAsync();
            var bookingResponse = await AuthedClient(client.Token).PostAsJsonAsync("/api/bookings",
                new ServiceBooking.API.DTOs.Bookings.CreateBookingDto(
                    company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
            bookingResponse.EnsureSuccessStatusCode();
            var booking = await bookingResponse.Content.ReadJsonAsync<ServiceBooking.API.DTOs.Bookings.BookingDto>();

            (await AuthedClient(owner.Token).PatchAsync($"/api/bookings/{booking!.Id}/complete", null)).EnsureSuccessStatusCode();
            (await AuthedClient(client.Token).PostAsJsonAsync("/api/reviews",
                new { bookingId = booking.Id, rating = 5, comment = $"review-{i}" })).EnsureSuccessStatusCode();
        }

        var page1 = await (await AnonymousClient().GetAsync($"/api/companies/{company.Id}/reviews?page=1&pageSize=3"))
            .Content.ReadJsonAsync<PagedResult<ReviewDto>>();
        var page2 = await (await AnonymousClient().GetAsync($"/api/companies/{company.Id}/reviews?page=2&pageSize=3"))
            .Content.ReadJsonAsync<PagedResult<ReviewDto>>();
        var page3 = await (await AnonymousClient().GetAsync($"/api/companies/{company.Id}/reviews?page=3&pageSize=3"))
            .Content.ReadJsonAsync<PagedResult<ReviewDto>>();

        page1!.Total.Should().Be(count);
        var allIds = page1.Items.Select(r => r.Id)
            .Concat(page2!.Items.Select(r => r.Id))
            .Concat(page3!.Items.Select(r => r.Id)).ToList();
        allIds.Should().OnlyHaveUniqueItems();
        allIds.Should().HaveCount(count);
        page3.HasNext.Should().BeFalse();
    }

    // Product regression fix (US-49, QA cycle C): the average rating on the public company page used to
    // be derived from whichever page of reviews happened to be loaded, so it visibly changed as a
    // visitor paged through reviews. The aggregate must come from a query over ALL reviews, independent
    // of which review page is being viewed.
    [Fact, TestCase("PAG-010")]
    public async Task CompanyAverageRating_DoesNotChangeAcrossReviewPages()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);

        // Deliberately lopsided ratings so a page-derived average would visibly differ from the true one.
        int[] ratings = { 5, 5, 5, 1, 1, 1, 3 };
        for (var i = 0; i < ratings.Length; i++)
        {
            var date = NextWeekday().AddDays(i);
            await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
            var client = await RegisterAsync();
            var bookingResponse = await AuthedClient(client.Token).PostAsJsonAsync("/api/bookings",
                new ServiceBooking.API.DTOs.Bookings.CreateBookingDto(
                    company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
            bookingResponse.EnsureSuccessStatusCode();
            var booking = await bookingResponse.Content.ReadJsonAsync<ServiceBooking.API.DTOs.Bookings.BookingDto>();
            (await AuthedClient(owner.Token).PatchAsync($"/api/bookings/{booking!.Id}/complete", null)).EnsureSuccessStatusCode();
            (await AuthedClient(client.Token).PostAsJsonAsync("/api/reviews",
                new { bookingId = booking.Id, rating = ratings[i], comment = $"rating-{i}" })).EnsureSuccessStatusCode();
        }

        var expectedAverage = ratings.Average();

        // Fetch two different review pages, and re-read the company on each "view" — the rating must be
        // identical both times and must equal the true full-set average, not either page's own average.
        var companyOnPage1 = await (await AnonymousClient().GetAsync($"/api/companies/{company.Slug}"))
            .Content.ReadJsonAsync<CompanyDto>();
        await AnonymousClient().GetAsync($"/api/companies/{company.Id}/reviews?page=1&pageSize=3");
        var companyOnPage2 = await (await AnonymousClient().GetAsync($"/api/companies/{company.Slug}"))
            .Content.ReadJsonAsync<CompanyDto>();
        await AnonymousClient().GetAsync($"/api/companies/{company.Id}/reviews?page=2&pageSize=3");
        var companyAfterPaging = await (await AnonymousClient().GetAsync($"/api/companies/{company.Slug}"))
            .Content.ReadJsonAsync<CompanyDto>();

        companyOnPage1!.AverageRating.Should().BeApproximately(expectedAverage, 0.001);
        companyOnPage2!.AverageRating.Should().Be(companyOnPage1.AverageRating,
            "the rating must not shift depending on which review page was viewed");
        companyAfterPaging!.AverageRating.Should().Be(companyOnPage1.AverageRating);
    }

    [Fact, TestCase("PAG-006")]
    public async Task MasterClients_SecondPage_DoesNotOverlapFirst()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);

        const int count = 7;
        var clientIds = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var date = NextWeekday().AddDays(i);
            await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
            var client = await RegisterAsync();
            clientIds.Add(client.UserId);
            var bookingResponse = await AuthedClient(client.Token).PostAsJsonAsync("/api/bookings",
                new ServiceBooking.API.DTOs.Bookings.CreateBookingDto(
                    company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
            bookingResponse.EnsureSuccessStatusCode();
        }

        var masterClient = AuthedClient(master.Token);
        var page1 = await (await masterClient.GetAsync($"/api/masters/clients?companyId={company.Id}&page=1&pageSize=3"))
            .Content.ReadJsonAsync<PagedResult<MasterClientDto>>();
        var page2 = await (await masterClient.GetAsync($"/api/masters/clients?companyId={company.Id}&page=2&pageSize=3"))
            .Content.ReadJsonAsync<PagedResult<MasterClientDto>>();
        var page3 = await (await masterClient.GetAsync($"/api/masters/clients?companyId={company.Id}&page=3&pageSize=3"))
            .Content.ReadJsonAsync<PagedResult<MasterClientDto>>();

        page1!.Total.Should().Be(count);
        var allClientIds = page1.Items.Select(c => c.ClientId)
            .Concat(page2!.Items.Select(c => c.ClientId))
            .Concat(page3!.Items.Select(c => c.ClientId)).ToList();
        allClientIds.Should().OnlyHaveUniqueItems();
        allClientIds.Should().BeEquivalentTo(clientIds);
    }

    // Product regression fix (US-49, QA cycle C): search used to filter only the current page rather
    // than the full client list, so a client who happened to sit on page 2/3 was invisible to a search
    // typed while looking at page 1. These pin that search reaches across the WHOLE database — by name
    // and by phone typed in a different format than it was stored in.
    [Fact, TestCase("PAG-008")]
    public async Task MasterClients_SearchByName_FindsClientOnSecondPage_NotJustFirstPage()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);

        var tag = Unique("srchname");
        const int count = 7; // with pageSize=3, index 2 (see ordering note below) lands on page 2
        string? targetClientId = null;
        for (var i = 0; i < count; i++)
        {
            var date = NextWeekday().AddDays(i);
            await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
            // GetClients orders by LastVisitDate DESCENDING, so index 2 (of 0..6) is the 5th-most-recent
            // visit — squarely on page 2 of a pageSize=3 listing (page1: idx 6,5,4; page2: idx 3,2,1).
            var isTarget = i == 2;
            var client = await RegisterAsync(firstName: isTarget ? tag : "Test", lastName: "User");
            if (isTarget) targetClientId = client.UserId;
            var bookingResponse = await AuthedClient(client.Token).PostAsJsonAsync("/api/bookings",
                new ServiceBooking.API.DTOs.Bookings.CreateBookingDto(
                    company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
            bookingResponse.EnsureSuccessStatusCode();
        }

        var page1WithSearch = await (await AuthedClient(master.Token)
                .GetAsync($"/api/masters/clients?companyId={company.Id}&search={tag}&page=1&pageSize=3"))
            .Content.ReadJsonAsync<PagedResult<MasterClientDto>>();

        page1WithSearch!.Items.Should().ContainSingle(c => c.ClientId == targetClientId,
            "search must filter the full database before pagination, not just the page currently being viewed");
        page1WithSearch.Total.Should().Be(1);
    }

    [Fact, TestCase("PAG-009")]
    public async Task MasterClients_SearchByPhone_MatchesRegardlessOfInputFormat()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);

        // A distinctive, fixed digit sequence so we can reformat it below without colliding with
        // UniquePhone()'s own randomness.
        var digits = "9991234567";
        var phone = "+7" + digits;
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var client = await RegisterAsync(phone: phone);
        var bookingResponse = await AuthedClient(client.Token).PostAsJsonAsync("/api/bookings",
            new ServiceBooking.API.DTOs.Bookings.CreateBookingDto(
                company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
        bookingResponse.EnsureSuccessStatusCode();

        // Same canonical number, three different ways a staff member might type it into the search box.
        string[] searchFormats = { $"8{digits}", $"+7 999 123-45-67", $"7{digits}" };
        foreach (var format in searchFormats)
        {
            var result = await (await AuthedClient(master.Token)
                    .GetAsync($"/api/masters/clients?companyId={company.Id}&search={Uri.EscapeDataString(format)}"))
                .Content.ReadJsonAsync<PagedResult<MasterClientDto>>();

            result!.Items.Should().ContainSingle(c => c.ClientId == client.UserId,
                $"search '{format}' must match the client's canonically-stored phone");
        }
    }

    // Code review finding (Blocker B3): page=int.MaxValue used to flow through unclamped and overflow
    // the (page - 1) * pageSize offset computation, producing a negative OFFSET — a 500 on this
    // endpoint specifically, because it is the one PUBLIC, ANONYMOUS paginated selection (the other
    // three all require an authenticated/staff caller). Pinning the fix at the HTTP boundary, not just
    // in the Pagination.Normalize unit tests, on the exact endpoint the finding named.
    [Theory, TestCase("PAG-007")]
    [InlineData(int.MaxValue, null)]
    [InlineData(0, null)]
    [InlineData(1, null)]
    [InlineData(1, 0)]
    [InlineData(1, 1000)]
    public async Task CompanyReviews_ExtremePageAndPageSize_NeverReturnsServerError(int page, int? pageSize)
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var client = await RegisterAsync();
        var bookingResponse = await AuthedClient(client.Token).PostAsJsonAsync("/api/bookings",
            new ServiceBooking.API.DTOs.Bookings.CreateBookingDto(
                company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        bookingResponse.EnsureSuccessStatusCode();
        var booking = await bookingResponse.Content.ReadJsonAsync<ServiceBooking.API.DTOs.Bookings.BookingDto>();
        (await AuthedClient(owner.Token).PatchAsync($"/api/bookings/{booking!.Id}/complete", null)).EnsureSuccessStatusCode();
        (await AuthedClient(client.Token).PostAsJsonAsync("/api/reviews",
            new { bookingId = booking.Id, rating = 5, comment = "extreme-pagination-review" })).EnsureSuccessStatusCode();

        var query = $"page={page}" + (pageSize is null ? "" : $"&pageSize={pageSize}");
        var response = await AnonymousClient().GetAsync($"/api/companies/{company.Id}/reviews?{query}");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK,
            $"a public, anonymous, caller-controlled page/pageSize must never 500 (got {(int)response.StatusCode})");
        var page1 = await response.Content.ReadJsonAsync<PagedResult<ReviewDto>>();
        page1!.Items.Should().NotBeNull();
        page1.PageSize.Should().BeLessOrEqualTo(Pagination.MaxPageSize);
        page1.Page.Should().BeGreaterOrEqualTo(1);
    }
}
