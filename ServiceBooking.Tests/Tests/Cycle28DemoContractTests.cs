using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Services.Demo;
using ServiceBooking.API.Services.Ops;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 28, pass B — contract check of /api/demo/* and of the demo-only answers (403 DemoRestricted, 503 DemoResetting) against
/// <c>contracts/cycle28/openapi.yaml</c> (bundled to openapi.json). Form only; behaviour is covered by the CY28-* scenarios.
/// A real host in demo mode on its OWN database "sbtest_&lt;key&gt;_demo" (lock 1 demands a name ending in _demo), no shared stand.
/// </summary>
public sealed class DemoHostFactory(string connectionString, IReadOnlyDictionary<string, string?>? overrides = null) : WebApplicationFactory<Program>
{
    public TestHostIdentity Identity { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Identity = TestHostSettings.Apply(builder, "demo", connectionString);
        builder.UseSetting("Notifications:EncryptionKey", NotificationDispatchTestFactory.TestEncryptionKeyBase64);
        builder.UseSetting("Trial:PhoneKeyHmac", "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");
        builder.UseSetting("Trial:PhoneKeyId", "qa-test-key");
        builder.UseSetting("DemoMode:Enabled", "true");
        builder.UseSetting("DemoMode:MaintenanceFlagPath", Path.Combine(Identity.StateRoot, "demo-resetting"));
        builder.UseSetting("PublicSites:ServicesBaseUrl", "https://demo.visit.ezbook.ru");
        // ARCHITECTURE_CYCLE35.md §35.5.1: the demo lock also demands an explicit demo address of "Orders" (empty = the production goods).
        builder.UseSetting("PublicSites:OrdersBaseUrl", "https://demo.zakaz.ezbook.ru");
        builder.UseSetting("AllowedOrigins", "https://demo.visit.ezbook.ru,https://demo.zakaz.ezbook.ru");
        builder.UseSetting("Jwt:Issuer", "ServiceBooking.Demo");
        builder.UseSetting("Notifications:Provider", "logging");
        builder.UseSetting("Notifications:StaffPush:Provider", "logging");
        builder.UseSetting("PhoneVerification:Provider", "stub");
        // The scenario classes (Cycle28DemoScenarioTests, Cycle28DemoLocksTests) bend one setting at a time; null removes the value.
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
            builder.UseSetting(key, value);
    }
}

// One database slot "demo" exists per run (lock 1 demands a name ending in _demo, the slot grammar has no underscores): every demo class runs in this collection, one at a time.
[Collection("Cycle28Generator")]
public class Cycle28DemoContractTests : IAsyncLifetime
{
    // ARCHITECTURE_CYCLE35.md §35.15: GET /api/demo/status gained "siteUrls" and POST /api/demo/login six roles, so the form of both is checked against
    // the cycle 35 schema (the strict cycle 28 one rejects the new field). The 403/503 answers keep their cycle 28 contract, which is unchanged.
    private static readonly OpenApiContract C35 = OpenApiContract.Load("cycle35");

    private TestClassDatabaseLease _lease = null!;
    private DemoHostFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _lease = await TestRunEnvironment.LeaseClassDatabaseAsync("demo");
        _factory = new DemoHostFactory(_lease.ConnectionString);
        _ = _factory.Services; // boots the host: lock 1 (configuration) and lock 2 (data) must pass
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

    [Fact, TestCase("CY28-38")]
    public async Task DemoStatusLoginRestrictedAndResetting_MatchTheCycle35Contract()
    {
        var http = _factory.CreateClient();

        // before the first reset: status answers, login is 409 text/plain
        var status = await http.GetAsync("/api/demo/status");
        C35.AssertResponse("GET", "/api/demo/status", 200, await JsonAsync(status, HttpStatusCode.OK));
        status.Headers.GetValues("X-Robots-Tag").Single().Should().Contain("noindex");

        var notSeeded = await http.PostAsJsonAsync("/api/demo/login", new { role = "owner" });
        notSeeded.StatusCode.Should().Be(HttpStatusCode.Conflict);
        notSeeded.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");

        // first fill, exactly like the operator: `ops demo reset --yes`
        var writer = new StringWriter();
        var exit = await OpsCommandRunner.RunAsync(_factory.Services, OpsCommandLine.Parse(["ops", "demo", "reset", "--yes"])!, writer);
        exit.Should().Be(0, writer.ToString());

        // login: the three roles, AuthResponseDto
        var tokens = new Dictionary<string, string>();
        foreach (var role in new[] { "owner", "master", "client" })
        {
            var body = await JsonAsync(await http.PostAsJsonAsync("/api/demo/login", new { role }), HttpStatusCode.OK);
            C35.AssertResponse("POST", "/api/demo/login", 200, body);
            tokens[role] = body.GetProperty("token").GetString()!;
        }

        var unknown = await http.PostAsJsonAsync("/api/demo/login", new { role = "admin" });
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unknown.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");

        // the 7 restricted actions: 403 + X-Demo-Restricted + text/plain body, even with an empty body
        var company = Guid.NewGuid();
        var routes = new (HttpMethod Method, string Url)[]
        {
            (HttpMethod.Post, "/api/profile/change-password"),
            (HttpMethod.Post, "/api/profile/change-phone"),
            (HttpMethod.Post, "/api/profile/delete-account"),
            (HttpMethod.Post, "/api/billing/subscription/request"),
            (HttpMethod.Post, "/api/billing/trial"),
            (HttpMethod.Put, $"/api/admin/companies/{company}/owner"),
            (HttpMethod.Post, $"/api/admin/companies/{company}/transfer"),
        };
        foreach (var (method, url) in routes)
        {
            using var req = new HttpRequestMessage(method, url) { Content = JsonContent.Create(new { }) };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens["owner"]);
            var res = await http.SendAsync(req);
            var text = await res.Content.ReadAsStringAsync();
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{method} {url}: {text}");
            // SuperAdmin-only route: a demo role (never SuperAdmin) is stopped by authorization first — the plain empty 403 of every other role.
            // The [DemoForbidden] filter on it is a second line of defence that a demo token cannot reach in practice.
            if (url.StartsWith("/api/admin/", StringComparison.Ordinal)) { text.Should().BeEmpty(); continue; }
            res.Headers.Contains("X-Demo-Restricted").Should().BeTrue($"{method} {url}");
            res.Content.Headers.ContentType!.MediaType.Should().Be("text/plain", $"{method} {url}");
            text.Should().Be("В демо-версии это действие недоступно.", $"{method} {url}");
        }

        // during a reset: every /api/* (except health and GET status) is 503 text/plain + headers; status is 200 with resetting = true
        // (the flag is cached for a second in-process, so it is raised through the host's own service, which drops the cache)
        var flag = _factory.Services.GetRequiredService<DemoMaintenanceFlag>();
        flag.Begin(DateTime.UtcNow);
        try
        {
            var busy = await http.GetAsync("/api/companies");
            busy.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            busy.Headers.GetValues("Retry-After").Should().NotBeEmpty();
            busy.Headers.GetValues("X-Demo-Resetting").Should().NotBeEmpty();
            busy.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");

            (await http.PostAsJsonAsync("/api/demo/login", new { role = "owner" })).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

            var during = await JsonAsync(await http.GetAsync("/api/demo/status"), HttpStatusCode.OK);
            C35.AssertResponse("GET", "/api/demo/status", 200, during);
            during.GetProperty("resetting").GetBoolean().Should().BeTrue();
        }
        finally
        {
            flag.End();
        }
    }
}

/// <summary>Outside demo mode (the regular test host = the production shape of the flag) /api/demo/* are empty 404s.</summary>
public class Cycle28DemoOffContractTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    [Fact, TestCase("CY28-39")]
    public async Task OutsideDemoMode_DemoRoutesAreEmpty404_AndNoDemoHeaders()
    {
        var http = AnonymousClient();

        var status = await http.GetAsync("/api/demo/status");
        status.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await status.Content.ReadAsStringAsync()).Should().BeEmpty();
        status.Headers.Contains("X-Robots-Tag").Should().BeFalse();

        var login = await http.PostAsJsonAsync("/api/demo/login", new { role = "owner" });
        login.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await login.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact, TestCase("CY28-54")]
    public async Task OutsideDemoMode_NightlyDemoResetIsNotEvenListed_AndNoDemoAnswersAppear()
    {
        var admin = await LoginAsSuperAdminAsync();
        var tasks = await AuthedClient(admin.Token).GetAsync("/api/admin/scheduled-tasks");
        tasks.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await tasks.Content.ReadAsStringAsync());
        doc.RootElement.EnumerateArray().Select(t => t.GetProperty("name").GetString()).Should().NotContain("demo-reset");

        // the demo-only refusal never appears on a production configuration: the same route answers as before (not 403 + X-Demo-Restricted)
        var user = await RegisterAsync();
        var own = await AuthedClient(user.Token).PostAsJsonAsync("/api/profile/change-password", new { currentPassword = "wrong-password", newPassword = "Password123!2" });
        own.Headers.Contains("X-Demo-Restricted").Should().BeFalse();
        (await AnonymousClient().GetAsync("/api/companies/public?pageSize=1")).Headers.Contains("X-Robots-Tag").Should().BeFalse("production pages stay indexable");
    }
}
