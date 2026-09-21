using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// US-42 (SPEC.md §5.2, ARCHITECTURE.md §9, API_CONTRACT.md §12) — each test gets its own
/// <see cref="RateLimitTestFactory"/> instance (own in-memory rate-limiter state), independent of the
/// shared "Api" collection (whose Testing config deliberately raises every limit to 10000/min so the
/// other 344 tests are never throttled).
/// </summary>
public class RateLimitingTests
{
    private static string RandomNumericPhone()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsDigit).Take(9).ToArray();
        return $"+79{new string(digits).PadRight(9, '1')}";
    }

    // CYCLE5-BREAKING (compile-only adaptation, see ApiTestBase.RegisterAsync's own note): the anonymous
    // registration payloads below need a `legal` object now, read from the same host they're posting to.
    private static async Task<object> CurrentLegalPayloadAsync(HttpClient client)
    {
        var manifest = await client.GetFromJsonAsync<ServiceBooking.API.Controllers.LegalManifestDto>("/api/legal/documents");
        return new
        {
            privacyAcknowledgedVersion = manifest!.Documents.First(d => d.Type == "Privacy").Version,
            termsAcceptedVersion = manifest.Documents.First(d => d.Type == "TermsClient").Version
        };
    }

    // ── auth-login: basic trip ───────────────────────────────────────────────

    [Fact, TestCase("SEC-040")]
    public async Task Login_ExceedingPermitLimit_ReturnsTooManyRequests()
    {
        await using var factory = new RateLimitTestFactory(authLoginPermitLimit: 3, trustedNetworks: ["127.0.0.1/32"]);
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new { phone = "+79990000000", password = "wrong" });
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "wrong credentials, but not yet rate-limited");
        }

        var fourth = await client.PostAsJsonAsync("/api/auth/login", new { phone = "+79990000000", password = "wrong" });
        fourth.StatusCode.Should().Be((HttpStatusCode)429);
        (await fourth.Content.ReadAsStringAsync()).Should().NotBeNullOrWhiteSpace("a 429 body must be non-empty so the frontend mapper can distinguish it from 403");
    }

    // ── Per-address partitioning via a TRUSTED X-Forwarded-For ───────────────

    [Fact, TestCase("SEC-041")]
    public async Task Login_DifferentForwardedForAddresses_AreRateLimitedIndependently_WhenProxyIsTrusted()
    {
        await using var factory = new RateLimitTestFactory(authLoginPermitLimit: 2, trustedNetworks: ["127.0.0.1/32"]);
        var client = factory.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            client.DefaultRequestHeaders.Remove("X-Forwarded-For");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.10");
            (await client.PostAsJsonAsync("/api/auth/login", new { phone = "+79990000001", password = "wrong" }))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.10");
        (await client.PostAsJsonAsync("/api/auth/login", new { phone = "+79990000001", password = "wrong" }))
            .StatusCode.Should().Be((HttpStatusCode)429, "this address has exhausted its own quota");

        // A DIFFERENT forwarded address must not be affected by the first one's exhausted quota.
        client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.20");
        (await client.PostAsJsonAsync("/api/auth/login", new { phone = "+79990000001", password = "wrong" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a different real client IP must get its own, fresh quota");
    }

    // ── Untrusted proxy: the header must be ignored entirely — SPEC.md §5.2 п.2, US-42 ──────
    //
    // Both tests below originally used bare RateLimitTestFactory.CreateClient(), which left
    // HttpContext.Connection.RemoteIpAddress null on every request (TestHost never populates it). That
    // is significant here specifically: ASP.NET Core's ForwardedHeadersMiddleware only runs its trust
    // check (against ForwardedHeaders:TrustedNetworks) when RemoteIpAddress is non-null — with it null,
    // the middleware applies X-Forwarded-For UNCONDITIONALLY, which made both tests fail for the wrong
    // reason (confirmed with a standalone TestServer probe during this QA cycle, see final report).
    // RateLimitTestFactory now stamps a fixed, fake "real TCP peer" onto every request via an
    // IStartupFilter (see its ConfigureWebHost) specifically so this trust check has a real, non-null
    // address to evaluate — matching what a real Kestrel connection behind nginx would provide.

    [Fact, TestCase("SEC-042")]
    public async Task Login_ForwardedForHeader_IsHonoredUnconditionally_WhenTrustedNetworksIsEmpty()
    {
        // Verified fact (standalone TestServer probe, this QA cycle): with ForwardedHeaders:TrustedNetworks
        // empty, ASP.NET Core's ForwardedHeadersMiddleware does NOT ignore X-Forwarded-For — it honors it
        // from ANY real peer unconditionally (KnownNetworks.Count == 0 && KnownProxies.Count == 0 skips
        // the trust check entirely, rather than failing it). This contradicts the "everyone gets grouped
        // under nginx's own address" rationale in ARCHITECTURE.md §9.1/Program.cs's fail-fast comment —
        // the real risk of an empty list is arbitrary IP spoofing, which is a *worse* outcome, not a
        // merely-imprecise one. It does not matter in Production, because Program.cs's fail-fast check
        // refuses to start there with an empty TrustedNetworks (US-42 п.2/US-48) — but that fail-fast is
        // itself unverified by any automated test (see final QA report: no Staging/empty-TrustedNetworks
        // startup test exists), and empty TrustedNetworks stays reachable, and this dangerous, in Testing
        // and Development. This test documents the real, verified behavior rather than the wrong
        // expectation the previous QA pass wrote before actually confirming it against ASP.NET Core.
        await using var factory = new RateLimitTestFactory(authLoginPermitLimit: 2, trustedNetworks: []);
        var client = factory.CreateClient();

        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.10");
        for (var i = 0; i < 2; i++)
            (await client.PostAsJsonAsync("/api/auth/login", new { phone = "+79990000002", password = "wrong" }))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Quota exhausted under 203.0.113.10 (honored despite the empty trust list). A DIFFERENT
        // forwarded value grants a FRESH quota — proving the header is trusted from anyone, not ignored.
        client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.20");
        var response = await client.PostAsJsonAsync("/api/auth/login", new { phone = "+79990000002", password = "wrong" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "with an empty trusted-networks list, ASP.NET Core's ForwardedHeadersMiddleware honors X-Forwarded-For from anyone — " +
            "this is exactly why Program.cs fail-fasts on an empty list outside Development/Testing (US-42 п.2)");
    }

    [Fact, TestCase("SEC-042b")]
    public async Task Login_ForwardedForHeader_IsIgnored_WhenRealPeerIsOutsideTrustedNetworks()
    {
        // TrustedNetworks IS configured here, but to a range that deliberately does not include the
        // simulated real TCP peer (RateLimitTestFactory's default fake peer, 127.0.0.1) — the scenario an
        // operator who forgot to update ForwardedHeaders:TrustedNetworks after moving nginx would actually
        // hit. This is the scenario that is actually reachable in Production (unlike SEC-042's empty
        // list, which Production's fail-fast forbids outright).
        await using var factory = new RateLimitTestFactory(authLoginPermitLimit: 2, trustedNetworks: ["10.0.0.0/8"]);
        var client = factory.CreateClient();

        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.10");
        for (var i = 0; i < 2; i++)
            (await client.PostAsJsonAsync("/api/auth/login", new { phone = "+79990000097", password = "wrong" }))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.20");
        var response = await client.PostAsJsonAsync("/api/auth/login", new { phone = "+79990000097", password = "wrong" });
        response.StatusCode.Should().Be((HttpStatusCode)429,
            "the real peer (127.0.0.1) is not inside the configured 10.0.0.0/8, so its forwarded header must be ignored regardless of its content");
    }

    // ── auth-register: same mechanism, separate policy ───────────────────────

    [Fact, TestCase("SEC-043")]
    public async Task Register_ExceedingPermitLimit_ReturnsTooManyRequests()
    {
        await using var factory = new RateLimitTestFactory(authRegisterPermitLimit: 2, trustedNetworks: ["127.0.0.1/32"]);
        var client = factory.CreateClient();
        var legal = await CurrentLegalPayloadAsync(client);

        for (var i = 0; i < 2; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/register", new
            {
                firstName = "Т", lastName = "Т", phone = RandomNumericPhone(), password = "Password123!", legal
            });
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }

        var third = await client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Т", lastName = "Т", phone = RandomNumericPhone(), password = "Password123!", legal
        });
        third.StatusCode.Should().Be((HttpStatusCode)429);
    }

    // ── Health checks are never rate-limited ─────────────────────────────────

    [Fact, TestCase("SEC-044")]
    public async Task HealthChecks_AreNeverRateLimited()
    {
        await using var factory = new RateLimitTestFactory(authLoginPermitLimit: 1, trustedNetworks: ["127.0.0.1/32"]);
        var client = factory.CreateClient();

        for (var i = 0; i < 20; i++)
            (await client.GetAsync("/api/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
