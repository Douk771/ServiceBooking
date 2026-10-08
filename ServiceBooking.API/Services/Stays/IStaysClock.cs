namespace ServiceBooking.API.Services.Stays;

/// <summary>ARCHITECTURE_CYCLE37.md §37.5.3 — the one source of "now" for holds, deadlines and the hold-expiry race. Replaced by a fake clock in tests.</summary>
public interface IStaysClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemStaysClock : IStaysClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
