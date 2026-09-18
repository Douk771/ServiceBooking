using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>History of a channel's state transitions (US-55 p.4). <see cref="Detail"/> is a technical
/// note only — never provider secret material (§23.1).</summary>
public class ChannelStateEvent
{
    public Guid Id { get; set; }

    public Guid ChannelId { get; set; }
    public NotificationChannel Channel { get; set; } = null!;

    public ChannelState FromState { get; set; }
    public ChannelState ToState { get; set; }
    public ChannelStateReason Reason { get; set; }
    public string? Detail { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
