using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace ServiceBooking.API.Services.PhoneVerification.Max;

/// <summary>
/// The real <see cref="IMaxBotClient"/> (<c>PhoneVerification:Provider = "max-bot"</c>).
/// ARCHITECTURE_CYCLE14.md §146.4 (О3, R10): a GLOBAL token-bucket limiter (30 req/s) and a PER-CHAT
/// partitioned limiter (2 msg/s) — both built with <c>System.Threading.RateLimiting</c>, no new
/// dependency. Registered as a singleton (Program.cs) so both limiters' state is actually shared across
/// requests instead of being reset per-instance.
///
/// Exact request/response shapes below (<c>subscriptions</c>, <c>messages</c>) follow the MAX Bot API's
/// publicly documented surface as of this cycle; §147.1's own note applies equally here — if a live-bot
/// recon shows a different shape, only this file and its tests change.
/// </summary>
public sealed class MaxBotClient : IMaxBotClient, IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<PhoneVerificationOptions> _options;
    private readonly ILogger<MaxBotClient> _logger;
    private readonly RateLimiter _globalLimiter;
    private readonly PartitionedRateLimiter<string> _perChatLimiter;

    public MaxBotClient(IHttpClientFactory httpClientFactory, IOptions<PhoneVerificationOptions> options, ILogger<MaxBotClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;

        var maxOptions = options.Value.Max;
        var globalRps = Math.Max(1, maxOptions.GlobalRequestsPerSecond);
        _globalLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = globalRps,
            TokensPerPeriod = globalRps,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            AutoReplenishment = true,
            QueueLimit = 200,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });

        var perChatRps = Math.Max(1, maxOptions.PerChatMessagesPerSecond);
        _perChatLimiter = PartitionedRateLimiter.Create<string, string>(chatId =>
            RateLimitPartition.GetTokenBucketLimiter(chatId, _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = perChatRps,
                TokensPerPeriod = perChatRps,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                AutoReplenishment = true,
                QueueLimit = 10,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            }));
    }

    public async Task<bool> SubscribeAsync(CancellationToken ct)
    {
        var maxOptions = _options.Value.Max;
        if (string.IsNullOrWhiteSpace(maxOptions.BotToken) || string.IsNullOrWhiteSpace(maxOptions.PublicBaseUrl) ||
            string.IsNullOrWhiteSpace(maxOptions.WebhookToken))
        {
            _logger.LogError("max-webhook-renew: cannot subscribe — Max:BotToken/PublicBaseUrl/WebhookToken is not fully configured");
            return false;
        }

        using var lease = await _globalLimiter.AcquireAsync(1, ct);
        if (!lease.IsAcquired) return false;

        // §146.1: the webhook secret is the LAST path segment. §146.2 О4: HTTPS on 443 with a trusted CA
        // — this deployment's own reverse proxy is what makes that true, PublicBaseUrl just names it.
        var webhookUrl = $"{maxOptions.PublicBaseUrl!.TrimEnd('/')}/api/phone-verification/max/webhook/{maxOptions.WebhookToken}";

        try
        {
            using var client = CreateClient(maxOptions);
            using var response = await client.PostAsJsonAsync("subscriptions", new
            {
                url = webhookUrl,
                update_types = new[] { "bot_started", "message_created" },
            }, ct);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "max-webhook-renew: subscribe request failed");
            return false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("max-webhook-renew: subscribe request timed out");
            return false;
        }
    }

    public async Task SendMessageAsync(string chatId, string text, CancellationToken ct)
    {
        var maxOptions = _options.Value.Max;
        if (string.IsNullOrWhiteSpace(maxOptions.BotToken)) return;

        using var globalLease = await _globalLimiter.AcquireAsync(1, ct);
        if (!globalLease.IsAcquired) return;
        using var chatLease = await _perChatLimiter.AcquireAsync(chatId, 1, ct);
        if (!chatLease.IsAcquired) return;

        try
        {
            using var client = CreateClient(maxOptions);
            using var response = await client.PostAsJsonAsync($"messages?chat_id={Uri.EscapeDataString(chatId)}", new { text }, ct);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("max-bot: sendMessage returned {StatusCode}", (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "max-bot: sendMessage failed");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("max-bot: sendMessage timed out");
        }
    }

    // §146.4: the bot token lives ONLY in this header (О3) — never a query string, which would otherwise
    // land in access logs/proxy logs verbatim. This client's own request/response logging is deliberately
    // muted by category in Program.cs, same convention as the "green-api"/"web-push" named clients.
    private HttpClient CreateClient(MaxBotOptions maxOptions)
    {
        var client = _httpClientFactory.CreateClient("max-bot");
        client.BaseAddress = new Uri(maxOptions.ApiUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", maxOptions.BotToken);
        return client;
    }

    public void Dispose()
    {
        _globalLimiter.Dispose();
        _perChatLimiter.Dispose();
    }
}
