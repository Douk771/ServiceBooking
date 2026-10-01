using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.Services.Ops;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 35 — contract check (form only) of the NEW part of /api/demo/*: status for product=orders, login of the three shop roles, the 400 for a bad
/// product, against <c>contracts/cycle35/openapi.json</c>. Cycle28DemoContractTests covers only the salon product.
/// </summary>
[Collection("Cycle28Generator")]
public class Cycle35DemoContractTests : IAsyncLifetime
{
    private static readonly OpenApiContract C35 = OpenApiContract.Load("cycle35");
    private TestClassDatabaseLease _lease = null!;
    private DemoHostFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _lease = await TestRunEnvironment.LeaseClassDatabaseAsync("demo");
        _factory = new DemoHostFactory(_lease.ConnectionString);
        _ = _factory.Services;
    }

    public async Task DisposeAsync()
    {
        try { await _factory.DisposeAsync(); }
        finally { await _lease.DropAsync(); }
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r, HttpStatusCode expected)
    {
        var text = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(expected, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    [Fact, TestCase("CY35-01")]
    public async Task DemoStatusForBothProductsAndSixRoleLogin_MatchTheCycle35Contract()
    {
        var http = _factory.CreateClient();
        var writer = new StringWriter();
        (await OpsCommandRunner.RunAsync(_factory.Services, OpsCommandLine.Parse(["ops", "demo", "reset", "--yes"])!, writer)).Should().Be(0, writer.ToString());

        foreach (var url in new[] { "/api/demo/status", "/api/demo/status?product=services", "/api/demo/status?product=orders" })
        {
            var body = await JsonAsync(await http.GetAsync(url), HttpStatusCode.OK);
            C35.AssertResponse("GET", "/api/demo/status", 200, body);
            body.GetProperty("roles").GetArrayLength().Should().Be(3, url);
        }

        foreach (var role in new[] { "owner", "master", "client", "shop-owner", "shop-staff", "shop-customer" })
        {
            var body = await JsonAsync(await http.PostAsJsonAsync("/api/demo/login", new { role }), HttpStatusCode.OK);
            C35.AssertResponse("POST", "/api/demo/login", 200, body);
        }

        (await JsonAsync(await http.GetAsync("/api/demo/status?product=ORDERS"), HttpStatusCode.OK)).GetProperty("roles").GetArrayLength().Should().Be(3); // case-insensitive
        foreach (var bad in new[] { "?product=x", "?product=", "?product=shop" })
        {
            var r = await http.GetAsync("/api/demo/status" + bad);
            if (bad == "?product=") { r.StatusCode.Should().Be(HttpStatusCode.OK); continue; } // contract: empty = services
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest, bad);
            r.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
        }
    }
}
