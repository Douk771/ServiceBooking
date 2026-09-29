using System.Net;
using FluentAssertions;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Cycle 20 merge review, finding 3: date filters bound from the query string arrive as
/// Kind=Unspecified (plain date) or Kind=Local (ISO with 'Z' — the MVC binder converts to local).
/// Npgsql 6+ writes only Kind=Utc into a timestamptz parameter, so an unnormalized filter is a 500.
/// </summary>
public class Cycle20DateFilterTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    [Theory, TestCase("CY20-DF-01")]
    [InlineData("?from=2026-09-01")]
    [InlineData("?from=2026-09-01T00:00:00Z&to=2026-09-30T23:59:59Z")]
    [InlineData("?to=2026-09-30T12:00:00%2B05:00")]
    public async Task GuestDataGateEvents_DateFilters_DoNotFail(string query)
    {
        var admin = await LoginAsSuperAdminAsync();

        var response = await AuthedClient(admin.Token).GetAsync("/api/admin/guest-data-gate-events" + query);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
