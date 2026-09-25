using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace ServiceBooking.API.Services.Signals;

/// <summary>Config surface for <see cref="GlitchTipSignalService"/> — reuses the same <c>Sentry:Dsn</c>
/// value the Serilog sink reads (Program.cs), never a second secret.</summary>
public sealed class GlitchTipSignalOptions
{
    public const string SectionName = "Sentry";
    public string Dsn { get; set; } = string.Empty;

    /// <summary>Same values the Serilog→Sentry sink stamps on every event it sends (Program.cs) — set
    /// programmatically at registration time from IHostEnvironment/Configuration, not bound from the
    /// "Sentry" section itself (there is no "Sentry:Environment" key). Lets a signal sent from a non-
    /// production deployment be told apart from a production one in the same GlitchTip project, instead
    /// of both landing as indistinguishable "new subject request" events (code review, cycle 16).</summary>
    public string? Environment { get; set; }
    public string? Release { get; set; }
}

/// <summary>
/// Production implementation of <see cref="IGlitchTipSignalService"/>: parses the DSN into GlitchTip's
/// Sentry-protocol store URL (<c>https://&lt;host&gt;/api/&lt;project_id&gt;/store/</c>, auth via the
/// <c>X-Sentry-Auth</c> header) and posts a minimal envelope — deliberately not a dependency on the
/// Sentry .NET SDK, which is already wired up for something else (the Serilog sink) and whose own
/// <c>MinimumEventLevel = Error</c> is exactly what this class exists to route around.
/// </summary>
public sealed partial class GlitchTipSignalService(
    IHttpClientFactory httpClientFactory,
    Microsoft.Extensions.Options.IOptions<GlitchTipSignalOptions> options,
    ILogger<GlitchTipSignalService> logger) : IGlitchTipSignalService
{
    // <scheme>://<key>@<host>/<project_id> — same shape backup.sh parses with sed. Scheme is captured
    // (not assumed to be https) so a self-hosted GlitchTip reachable only over plain http on a dev/
    // staging stand is not silently rejected (code review, cycle 16).
    [GeneratedRegex(@"^(?<scheme>https?)://(?<key>[^@]+)@(?<host>[^/]+)/(?<project>\d+)$")]
    private static partial Regex DsnPattern();

    /// <summary>Pure DSN parsing, split out so it can be unit-tested without any HTTP dependency (§4 of
    /// the team's own testing rules: no network, no real server, in a unit test). Returns false — never
    /// throws — for anything not in the <c>https://key@host/project_id</c> shape, including an empty
    /// string.</summary>
    internal static bool TryParseDsn(string? dsn, out string storeUrl, out string authHeader)
    {
        storeUrl = string.Empty;
        authHeader = string.Empty;
        if (string.IsNullOrWhiteSpace(dsn)) return false;

        var match = DsnPattern().Match(dsn.Trim());
        if (!match.Success) return false;

        var scheme = match.Groups["scheme"].Value;
        var key = match.Groups["key"].Value;
        var host = match.Groups["host"].Value;
        var projectId = match.Groups["project"].Value;
        storeUrl = $"{scheme}://{host}/api/{projectId}/store/";
        authHeader = $"Sentry sentry_version=7, sentry_key={key}";
        return true;
    }

    public async Task<bool> SendAsync(string message, CancellationToken ct)
    {
        if (!TryParseDsn(options.Value.Dsn, out var url, out var authHeader))
        {
            // Either no DSN configured (absence of monitoring must never break the feature it's
            // watching) or a malformed one (a deployment-config problem, not a reason to fail the
            // caller's own operation) — either way, log locally at most and move on.
            if (!string.IsNullOrWhiteSpace(options.Value.Dsn))
                logger.LogWarning("GlitchTip signal skipped: Sentry:Dsn is not in the expected shape.");
            return false;
        }

        // Defence in depth (code review, cycle 16): callers are contractually limited to kind/reference/
        // due-date (see the type doc above), but this is the one place that constraint can be enforced in
        // code rather than by caller discipline alone — the same masking the Serilog→Sentry path applies
        // (Program.cs), applied here to the one field this envelope actually carries free text in.
        var maskedMessage = ServiceBooking.API.Services.LogMasking.MaskPhoneSequences(message);

        var body = new
        {
            event_id = Guid.NewGuid().ToString("N"),
            timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            level = "info",
            logger = "servicebooking-subject-requests",
            platform = "csharp",
            environment = options.Value.Environment,
            release = options.Value.Release,
            message = new { formatted = maskedMessage },
        };

        try
        {
            using var client = httpClientFactory.CreateClient("glitchtip-signal");
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(body),
            };
            request.Headers.Add("X-Sentry-Auth", authHeader);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10)); // same budget as backup.sh's curl -m 10

            var response = await client.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("GlitchTip signal rejected: {StatusCode}", response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Network/timeout failures must never surface to the caller — sending a signal is best
            // effort, and a GlitchTip outage must not block subject-request intake or the retention task.
            logger.LogWarning(ex, "GlitchTip signal failed to send.");
            return false;
        }
    }
}
