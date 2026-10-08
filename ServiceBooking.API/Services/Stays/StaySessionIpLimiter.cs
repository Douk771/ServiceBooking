using System.Threading.RateLimiting;

namespace ServiceBooking.API.Services.Stays;

/// <summary>API_CONTRACT_CYCLE39.md §39.32 — the second link of the `stay-session-add` chain: 30 additions per hour per IP (the first link, 10 per hour per booking token, is the policy of that name).</summary>
public sealed class StaySessionIpLimiter : IDisposable
{
    public const string Text = "Слишком много попыток. Попробуйте позже";

    private readonly PartitionedRateLimiter<string> _limiter;

    public StaySessionIpLimiter(IConfiguration configuration)
    {
        var permit = configuration.GetValue("RateLimits:stay-session-ip:PermitLimit", 30);
        var window = TimeSpan.FromMinutes(configuration.GetValue("RateLimits:stay-session-ip:WindowMinutes", 60));
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
