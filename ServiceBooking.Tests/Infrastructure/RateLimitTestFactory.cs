using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// US-42 (SPEC.md §5.2, ARCHITECTURE.md §9, API_CONTRACT.md §12) — a dedicated host with tight,
/// explicit rate-limit overrides. The shared "Api" collection factory raises every RateLimits:* value
/// to 10000/min in Testing precisely so the other 344 pre-existing functional tests are never limited
/// by it (appsettings.Testing.json) — which means the limiter's own behavior (429 on the Nth+1 call, per-
/// address partitioning, untrusted-proxy-header handling) can only be exercised against a host that
/// overrides those defaults back down. One instance per test (not shared) — each test gets its own
/// exhausted/fresh rate limiter state.
/// </summary>
public sealed class RateLimitTestFactory : WebApplicationFactory<Program>
{
    private readonly int? _authLoginPermitLimit;
    private readonly int? _authRegisterPermitLimit;
    private readonly int? _dataExportPermitLimit;
    private readonly int? _bookingCreateAnonymousPermitLimit;
    private readonly string[]? _trustedNetworks;
    private readonly string _simulatedRealPeer;
    private readonly string _connectionString;

    public RateLimitTestFactory(
        string connectionString,
        int? authLoginPermitLimit = null,
        int? authRegisterPermitLimit = null,
        int? dataExportPermitLimit = null,
        int? bookingCreateAnonymousPermitLimit = null,
        string[]? trustedNetworks = null,
        string simulatedRealPeer = "127.0.0.1")
    {
        _connectionString = connectionString;
        _authLoginPermitLimit = authLoginPermitLimit;
        _authRegisterPermitLimit = authRegisterPermitLimit;
        _dataExportPermitLimit = dataExportPermitLimit;
        _bookingCreateAnonymousPermitLimit = bookingCreateAnonymousPermitLimit;
        _trustedNetworks = trustedNetworks;
        _simulatedRealPeer = simulatedRealPeer;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Microsoft.AspNetCore.TestHost never populates HttpContext.Connection.RemoteIpAddress — it
        // stays null on every request unless something sets it. That matters here specifically because
        // ForwardedHeadersMiddleware's trust check (CheckKnownAddress) only runs when RemoteIpAddress is
        // non-null; when it's null the middleware applies X-Forwarded-For UNCONDITIONALLY, bypassing
        // ForwardedHeaders:TrustedNetworks entirely — which would make every test here pass or fail for
        // the wrong reason regardless of what the app's trust configuration actually says. This
        // IStartupFilter runs a middleware before UseForwardedHeaders (registered via DI so it is
        // independent of Program.cs's own pipeline construction) that stamps a fixed, fake "real TCP
        // peer" address onto every request, so the trust check downstream has something real to compare
        // ForwardedHeaders:TrustedNetworks against — exactly like a real Kestrel connection would.
        // Confirmed empirically (see QA cycle-C session notes) against a standalone TestServer probe:
        // with RemoteIpAddress populated, ForwardedHeadersMiddleware correctly refuses to honor
        // X-Forwarded-For when the real peer falls outside the configured trusted network(s).
        builder.ConfigureServices(services =>
            services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter>(
                _ => new FakeRemoteIpStartupFilter(IPAddress.Parse(_simulatedRealPeer))));


        TestHostSettings.Apply(builder, "ratelimit", _connectionString);

        // WindowMinutes intentionally left at their (long) production defaults everywhere below — only
        // PermitLimit is tightened, so a test needs a handful of calls, not a real clock, to trip 429.
        if (_authLoginPermitLimit is { } login)
            builder.UseSetting("RateLimits:auth-login:PermitLimit", login.ToString());
        if (_authRegisterPermitLimit is { } register)
            builder.UseSetting("RateLimits:auth-register:PermitLimit", register.ToString());
        if (_dataExportPermitLimit is { } export)
        {
            builder.UseSetting("RateLimits:data-export:PermitLimit", export.ToString());
            builder.UseSetting("RateLimits:data-export:WindowMinutes", "1440");
        }
        if (_bookingCreateAnonymousPermitLimit is { } bookingAnon)
            builder.UseSetting("RateLimits:booking-create:AnonymousPermitLimit", bookingAnon.ToString());

        for (var i = 0; i < (_trustedNetworks?.Length ?? 0); i++)
            builder.UseSetting($"ForwardedHeaders:TrustedNetworks:{i}", _trustedNetworks![i]);
    }
}

/// <summary>Stamps a fixed, fake "real TCP peer" address onto every request before Program.cs's own
/// pipeline (including UseForwardedHeaders) runs — see the long comment in
/// <see cref="RateLimitTestFactory.ConfigureWebHost"/> for why this is necessary under TestServer.</summary>
file sealed class FakeRemoteIpStartupFilter(IPAddress remotePeer) : Microsoft.AspNetCore.Hosting.IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (ctx, nextMiddleware) =>
        {
            ctx.Connection.RemoteIpAddress = remotePeer;
            await nextMiddleware();
        });
        next(app);
    };
}
