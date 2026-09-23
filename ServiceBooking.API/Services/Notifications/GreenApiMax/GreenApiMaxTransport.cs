using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services;

namespace ServiceBooking.API.Services.Notifications.GreenApiMax;

/// <summary>
/// The real MAX transport (<c>Notifications:Provider=green-api</c>, <c>NotificationChannel.Transport ==
/// Max</c>, ARCHITECTURE_CYCLE9.md §104.2/§104.9) — structurally a copy of
/// <see cref="GreenApi.GreenApiTransport"/> with MAX's own URL builder/classifier/options swapped in,
/// kept as a SEPARATE class per the architecture's own file list rather than a shared generic transport:
/// R2 says explicitly that if MAX's shape ever stops matching WhatsApp's, the fix must be local to ONE
/// adapter. Reuses the SAME <c>"green-api"</c> named <see cref="IHttpClientFactory"/> client as WhatsApp
/// (§104.2: "тот же провайдер, тот же IHttpClientFactory") — MAX needs its own <em>URL and partner
/// token</em>, not its own network/handler configuration.
/// </summary>
public sealed class GreenApiMaxTransport(
    IHttpClientFactory httpClientFactory,
    IOptions<NotificationOptions> options,
    ILogger<GreenApiMaxTransport> logger) : INotificationTransport
{
    public async Task<SendOutcome> SendAsync(ChannelCredentials credentials, string canonicalPhone, string text, CancellationToken ct)
    {
        var opts = options.Value;

        // Same sandbox convention as WhatsApp (US-35 p.5) — one allow-list governs both transports,
        // because it answers the same question ("is this recipient safe to actually message") regardless
        // of which messenger carries the message.
        if (opts.AllowedRecipients.Length > 0 && !opts.AllowedRecipients.Contains(canonicalPhone))
        {
            logger.LogInformation(
                "GREEN-API MAX sandbox: {MaskedPhone} is not in Notifications:AllowedRecipients — logging " +
                "instead of sending (length={Length})",
                LogMasking.Phone(canonicalPhone), text.Length);
            return new SendOutcome.Sent($"sandbox-max-{Guid.NewGuid():N}");
        }

        var client = httpClientFactory.CreateClient("green-api");
        var chatId = GreenApiMaxUrls.BuildChatId(canonicalPhone);
        var (uri, safeLabel) = GreenApiMaxUrls.SendMessage(opts.GreenApiMax.ApiUrl, credentials.InstanceId, credentials.Token);

        var stopwatch = Stopwatch.StartNew();
        var networkFailure = false;
        HttpStatusCode? statusCode = null;
        string? body = null;

        try
        {
            using var response = await client.PostAsJsonAsync(uri, new { chatId, message = text }, ct);
            statusCode = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException)
        {
            networkFailure = true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Same distinction GreenApiTransport makes: HttpClient's own Timeout, not the dispatcher's
            // per-pass budget — see that class's doc comment for why the filter matters.
            networkFailure = true;
        }
        finally
        {
            stopwatch.Stop();
            logger.LogInformation(
                "GREEN-API MAX request {SafeLabel} took {DurationMs}ms, status={StatusCode}",
                safeLabel, stopwatch.ElapsedMilliseconds, statusCode is null ? "(none)" : ((int)statusCode).ToString());
        }

        return GreenApiMaxResultClassifier.Classify(networkFailure, statusCode, body);
    }
}
