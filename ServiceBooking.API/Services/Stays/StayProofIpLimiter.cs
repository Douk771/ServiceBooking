using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// API_CONTRACT_CYCLE37.md §37.35 — the second link of the `stay-proof` chain: 20 uploads per hour per IP (the first link, 6 per 10 minutes per booking token,
/// is the rate-limit policy of that name). A policy cannot hold two partitions, so the IP window lives here and the controller asks it.
/// </summary>
public sealed class StayProofIpLimiter : IDisposable
{
    public const string Text = "Слишком много загрузок. Попробуйте позже";

    private readonly PartitionedRateLimiter<string> _limiter;

    public StayProofIpLimiter(IConfiguration configuration)
    {
        var permit = configuration.GetValue("RateLimits:stay-proof-ip:PermitLimit", 20);
        var window = TimeSpan.FromMinutes(configuration.GetValue("RateLimits:stay-proof-ip:WindowMinutes", 60));
        _limiter = PartitionedRateLimiter.Create<string, string>(ip => RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permit, Window = window, QueueLimit = 0
        }));
    }

    public bool TryAcquire(string? ip)
    {
        using var lease = _limiter.AttemptAcquire(ip ?? "anonymous");
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
