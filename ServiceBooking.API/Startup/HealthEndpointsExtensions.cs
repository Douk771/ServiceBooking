using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ServiceBooking.API.Services.Health;

namespace ServiceBooking.API.Startup;

/// <summary>
/// Health checks: registration and the two anonymous endpoints. Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): moved verbatim out of Program.cs, whose call order is unchanged.
/// </summary>
internal static class HealthEndpointsExtensions
{
    public static void AddServiceBookingHealthChecks(this WebApplicationBuilder builder)
    {
    // Health checks (US-43, ARCHITECTURE.md §10): "live" never touches anything and always answers 200 —
    // it just proves the process is up and can accept HTTP. "ready" additionally proves the database is
    // reachable and migrated, tagged "ready" so MapHealthChecks below can select just this one check.
    builder.Services.AddHealthChecks()
        .AddCheck<DatabaseReadyHealthCheck>("database", tags: ["ready"]);
    }

    public static void MapServiceBookingHealthChecks(this WebApplication app)
    {
    // Health checks (US-43, ARCHITECTURE.md §10.1). Deliberately NOT [EnableRateLimiting] anywhere near
    // these two routes: monitoring must not be able to lock itself out, and there is no global limiter in
    // this project (app.UseRateLimiter() above is a no-op except where the attribute is applied), so simply
    // never applying the attribute here is the whole mechanism (§9.3). Both are anonymous — a health probe
    // cannot authenticate.
    //
    // Custom two-field ResponseWriter for both endpoints: the framework's default JSON payload includes
    // each check's exception message, which for "database" would leak a connection string fragment or a
    // driver error straight onto a public, unauthenticated endpoint (US-43 p.3).
    app.MapHealthChecks("/api/health/live", new HealthCheckOptions
    {
        Predicate = _ => false, // no checks run at all — this route never touches the database
        ResponseWriter = WriteHealthResponse
    }).AllowAnonymous();

    app.MapHealthChecks("/api/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = WriteHealthResponse
    }).AllowAnonymous();
    }

    // Own JSON body for both health endpoints, two fields only (US-43 p.3): the framework's default
    // UIResponseWriter serializes every check's exception message, which for "database" would put a
    // connection-string fragment or driver error onto a public, unauthenticated endpoint. "failed" carries
    // whichever check's HealthCheckResult.Unhealthy(description) fired first — "database" or "migrations"
    // (DatabaseReadyHealthCheck), the two values API_CONTRACT.md §10.2 documents.
    private static Task WriteHealthResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        if (report.Status == HealthStatus.Healthy)
            return context.Response.WriteAsync("""{"status":"Healthy"}""");

        var failed = report.Entries.Values.FirstOrDefault(e => e.Status != HealthStatus.Healthy).Description ?? "database";
        return context.Response.WriteAsJsonAsync(new { status = "Unhealthy", failed });
    }
}
