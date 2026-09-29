using System.Diagnostics;
using System.Security.Claims;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using ServiceBooking.API.Services;

namespace ServiceBooking.API.Startup;

/// <summary>
/// Serilog host logger and the per-request log line. Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): moved verbatim out of Program.cs, whose call order is unchanged.
/// </summary>
internal static class LoggingExtensions
{
    public static void AddServiceBookingSerilog(this WebApplicationBuilder builder)
    {
    // Serilog replaces the host logger entirely (US-45, ARCHITECTURE.md §11.1) — both stdout (docker logs)
    // and a rolling file (survives container recreation, docker logs doesn't). CompactJsonFormatter on
    // both, so a log line is one JSON object whether it's read live or grepped from disk a day later.
    // PhoneMaskingEnricher is the second/third rung of §11.3's defence; it self-limits to Warning+/exception
    // events, so it costs nothing on the Information-level "request completed" line every request produces.
    // Cycle 8 phase 2 (ARCHITECTURE_CYCLE8_PHASE2.md §96): preserveStaticLogger defaults to false, which
    // makes THIS host's logger win the process-wide static Log.Logger — harmless with one host per process,
    // but under per-test-class parallelism (several WebApplicationFactory hosts alive at once, ARCHITECTURE_
    // CYCLE8_PHASE2.md §92.4) the last host to start "wins" the static logger for every other host's writes,
    // and a host's own DisposeAsync can close a Log.Logger some OTHER still-running host is still using.
    // Scoped strictly to the Testing environment so Development/Production keep today's behavior byte-for-
    // byte, including Log.CloseAndFlush's shutdown-time flush semantics that depend on it.
    builder.Host.UseSerilog((context, services, loggerConfig) =>
    {
        loggerConfig
            .MinimumLevel.Information()
            // EF Core logs every SQL statement (with parameter values — i.e. phone numbers, names) at
            // Information by default; without this override that alone would be both a wall of noise and a
            // PII leak that bypasses the enricher (enrichers see the RENDERED event, but EF's own query
            // logger writes parameter values into properties the enricher would still catch — this override
            // means it never has to).
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.With<PhoneMaskingEnricher>()
            .WriteTo.Console(new CompactJsonFormatter())
            .WriteTo.File(new CompactJsonFormatter(), Path.Combine(LogDirectory(context.Configuration), "app-.json"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14,
                fileSizeLimitBytes: 100 * 1024 * 1024, rollOnFileSizeLimit: true);

        // Sink to GlitchTip via the Sentry protocol (ARCHITECTURE.md §11.4). Empty DSN → sink not
        // registered at all, same pattern as CaptchaService.IsEnforced: Development/Testing carry no DSN by
        // configuration, so nothing leaves the process there, and a Production deployment that hasn't set
        // one up yet still starts and runs (US-45 pp. 8–9).
        var sentryDsn = context.Configuration["Sentry:Dsn"];
        if (!string.IsNullOrWhiteSpace(sentryDsn))
        {
            loggerConfig.WriteTo.Sentry(o =>
            {
                o.Dsn = sentryDsn;
                o.MinimumEventLevel = LogEventLevel.Error; // only real failures become GlitchTip issues
                o.MinimumBreadcrumbLevel = LogEventLevel.Warning;
                o.SendDefaultPii = false;
                o.Environment = context.HostingEnvironment.EnvironmentName;
                o.Release = context.Configuration["Sentry:Release"];
                // Same masking rule as the log pipeline (§11.3 p.2), applied to the top-level free-text
                // message — the one place Sentry's own SDK doesn't go through Serilog's property pipeline.
                // Individual exception frame messages/stack traces are not scanned (out of scope for this
                // cycle's pass); by construction they should never carry raw request data in the first
                // place, since .NET stack traces contain source locations, not caught values.
                o.SetBeforeSend((sentryEvent, _) =>
                {
                    if (sentryEvent.Message?.Message is { } message)
                        sentryEvent.Message.Message = LogMasking.MaskPhoneSequences(message);
                    return sentryEvent;
                });
            });
        }
    }, preserveStaticLogger: builder.Environment.IsEnvironment("Testing"));
    }

    public static void UseServiceBookingRequestLogging(this WebApplication app)
    {
    // One line per request (US-45, ARCHITECTURE.md §11.1) — method, path, status, duration for free, plus
    // traceId/userId via EnrichDiagnosticContext. Runs BEFORE UseExceptionHandler (architecture decision:
    // the request-completed log line must exist even for the request that trips the exception handler,
    // carrying the SAME traceId the 500 response's problem+json puts in front of the operator — that
    // pairing is the whole point of "найди по traceId" in DEPLOY.md).
    // Code review note (US-45 p.2/§11.3): the logged RequestPath below never carries the query string, so
    // e.g. GET /api/admin/users?search=<телефон> does NOT leak a phone number here — Serilog.AspNetCore's
    // RequestLoggingOptions.IncludeQueryInRequestPath defaults to false, and it stays false; it is NOT set
    // here on purpose. Do not "fix" this by turning it on: PhoneMaskingEnricher (§11.3 point 3) only scans
    // events at Warning+ and events with an exception, and this middleware's own request-completed line is
    // logged at Information for every normal 2xx/4xx — an enabled query string would ride straight past the
    // enricher into the log in the clear. If a future need arises to see query strings, it must go through
    // the same masking as anything else with a phone in it, not through this switch.
    app.UseSerilogRequestLogging(opts =>
    {
        // Cycle 8 phase 2 (ARCHITECTURE_CYCLE8_PHASE2.md §96): RequestLoggingMiddleware defaults to the
        // STATIC Serilog.Log.Logger when opts.Logger is left null — the one fallback in this pipeline that
        // preserveStaticLogger (set above on builder.Host.UseSerilog) does not route around by itself. Under
        // Testing's per-host preserveStaticLogger=true, that default would mean this middleware's own
        // request-completed line goes to whichever OTHER host most recently claimed the static logger (or
        // nowhere, if none has) instead of THIS host's own file sink — resolved here by pointing it
        // explicitly at the Serilog.ILogger this exact host's DI container built (registered by UseSerilog
        // regardless of preserveStaticLogger). No behavior change outside Testing: in Development/Production
        // there is only ever one host per process, so this is the SAME logger the static field would have
        // pointed at anyway.
        opts.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
        // Every DELIBERATE 4xx (400/402/403/404/409/429/451) logs at Information, never Warning/Error
        // (US-45 p.5) — they are normal traffic, not incidents. Only an unhandled exception or a 5xx is
        // Warning/Error-worthy from the request-logging middleware's point of view; background-task and
        // infrastructure-level Warning/Error events (§11.2) are logged separately, by their own code, not
        // through this line.
        // UseExceptionHandler (registered below) already catches the exception and writes the 500 body
        // before this middleware runs its own logging (it wraps everything AFTER it, this line included),
        // so `ex` is normally null here even for a 500 — the status code is the reliable signal.
        opts.GetLevel = (httpContext, _, ex) =>
            ex is not null || httpContext.Response.StatusCode >= 500 ? LogEventLevel.Error : LogEventLevel.Information;
        opts.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            // The SAME value the 500 handler puts into problem+json's traceId — if these two ever diverge,
            // "найди по traceId" in DEPLOY.md stops working, which is the whole point of this line existing.
            diagnosticContext.Set("traceId", Activity.Current?.Id ?? httpContext.TraceIdentifier);
            diagnosticContext.Set("userId", httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier));
        };
        // N1 (review round 2): RequestPath masking does NOT belong in EnrichDiagnosticContext — this
        // middleware builds the final LogEvent's properties as collectedProperties.Concat([RequestMethod,
        // RequestPath, StatusCode, Elapsed]) and applies them via AddOrUpdateProperty IN THAT ORDER, so the
        // middleware's OWN RequestPath (added last) overwrites whatever EnrichDiagnosticContext set under the
        // same name — a previous version of this code relied on diagnosticContext.Set("RequestPath", ...)
        // winning, which it does not; §37's "ноль совпадений в логах приложения" was not actually met, and
        // the unsubscribe token (a signed phone number) was reaching Information-level logs, upstream of
        // PhoneMaskingEnricher (Warning+ only). GetMessageTemplateProperties exists in Serilog.AspNetCore
        // specifically for this — it's what BUILDS RequestMethod/RequestPath/StatusCode/Elapsed in the first
        // place, so masking here is authoritative rather than racing the middleware for the last write.
        opts.GetMessageTemplateProperties = (httpContext, requestPath, elapsedMs, statusCode) =>
        {
            var maskedPath = MaskSensitiveRequestPath(requestPath) ?? requestPath;
            return
            [
                new LogEventProperty("RequestMethod", new ScalarValue(httpContext.Request.Method)),
                new LogEventProperty("RequestPath", new ScalarValue(maskedPath)),
                new LogEventProperty("StatusCode", new ScalarValue(statusCode)),
                new LogEventProperty("Elapsed", new ScalarValue(elapsedMs)),
            ];
        };
    });
    }

    // Log directory is configurable (ARCHITECTURE_CYCLE8.md §71.4) so each test-run/slot/factory can point
    // it at its own temp folder instead of colliding on a repo-relative "logs" — the sole behavioural change
    // cycle 8 makes to this file. Default is "logs", exactly as it always was: Development/Production
    // behaviour is unchanged byte-for-byte when Logs:Directory isn't set.
    private static string LogDirectory(IConfiguration configuration) =>
        configuration["Logs:Directory"] is { } configured && configured.Trim().Length > 0 ? configured : "logs";

    // B2/I5: mask the {token} segment of the two notification routes that embed a secret in the URL, so
    // the request-completed log line (Information) never carries it. Returns null for every other path —
    // callers only override RequestPath when this returns non-null.
    internal static string? MaskSensitiveRequestPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        const string webhookPrefix = "/api/notifications/provider-webhook/";
        const string unsubscribePrefix = "/api/notifications/unsubscribe/";
        // ARCHITECTURE_CYCLE14.md §146.1 (R12) — same coordinated fix as the two above, same incident this
        // cycle explicitly avoids repeating (cycle 9's nginx access-log leak, §9 N9-*). This app-level
        // masking covers what THIS process logs; deploy/nginx/ezbook.conf's own map/log_format (added in the
        // SAME commit) is what stops nginx's access log from writing the token before the request even
        // reaches here — neither alone is sufficient.
        const string maxWebhookPrefix = "/api/phone-verification/max/webhook/";

        // ARCHITECTURE_CYCLE23.md §398.6 — the 256-bit order token is a secret of access to the order (name, phone mask, content);
        // it sits in the path of the order page API and of the cancellation. The tail after the token ("/cancel") is kept.
        const string orderPublicPrefix = "/api/orders/public/";

        if (path.StartsWith(orderPublicPrefix, StringComparison.Ordinal) && path.Length > orderPublicPrefix.Length)
        {
            var slash = path.IndexOf('/', orderPublicPrefix.Length);
            return orderPublicPrefix + "***" + (slash < 0 ? string.Empty : path[slash..]);
        }
        if (path.StartsWith(webhookPrefix, StringComparison.Ordinal) && path.Length > webhookPrefix.Length)
            return webhookPrefix + "***";
        if (path.StartsWith(unsubscribePrefix, StringComparison.Ordinal) && path.Length > unsubscribePrefix.Length)
            return unsubscribePrefix + "***";
        if (path.StartsWith(maxWebhookPrefix, StringComparison.Ordinal) && path.Length > maxWebhookPrefix.Length)
            return maxWebhookPrefix + "***";

        return null;
    }
}
