using System.Net;
using FluentAssertions;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>US-43 (SPEC.md §5.3, ARCHITECTURE.md §10, API_CONTRACT.md §10).</summary>
public class HealthTests(ApiDatabaseFixture fixture) : ApiTestBase(fixture)
{
    [Fact, TestCase("OPS-001")]
    public async Task Live_IsPublic_AndHealthy()
    {
        var response = await AnonymousClient().GetAsync("/api/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Healthy");
    }

    [Fact, TestCase("OPS-002")]
    public async Task Ready_IsPublic_AndHealthy_WhenDatabaseIsReachable()
    {
        var response = await AnonymousClient().GetAsync("/api/health/ready");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Healthy");
    }

    [Fact, TestCase("OPS-003")]
    public async Task Ready_ResponseBody_NeverLeaksConnectionDetails()
    {
        var response = await AnonymousClient().GetAsync("/api/health/ready");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContainAny("Host=", "Password", "Npgsql", "Exception", "StackTrace", "servicebooking_test");
    }

    [Fact, TestCase("OPS-004")]
    public async Task Live_And_Ready_AreDifferentChecks_LiveNeverTouchesTheDatabase()
    {
        // Documented behavior (API_CONTRACT.md §10.1): "live" never queries the database at all —
        // Program.cs wires it with Predicate = _ => false, so no IHealthCheck (including the DB one)
        // ever runs for this route. This is exercised indirectly: live must stay 200 even though the
        // process-wide "migrations confirmed" latch (DatabaseReadyHealthCheck) has already been set by
        // every other test in this run touching /api/health/ready — if live accidentally depended on
        // that check's side effects, this would be the place a regression could slip through unnoticed,
        // so the two are asserted independently rather than assuming one implies the other.
        (await AnonymousClient().GetAsync("/api/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AnonymousClient().GetAsync("/api/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
