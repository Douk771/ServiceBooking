using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>The one place a channel's payment state is computed (ARCHITECTURE_CYCLE4.md §23.2).</summary>
public static class ChannelPaymentState
{
    public static ChannelPaymentStatus Of(NotificationChannel channel, DateTime nowUtc)
    {
        if (channel.IsSuspendedByAdmin) return ChannelPaymentStatus.Suspended;
        if (channel.PaidUntilUtc is null) return ChannelPaymentStatus.NotPaid;
        return channel.PaidUntilUtc.Value >= nowUtc ? ChannelPaymentStatus.Paid : ChannelPaymentStatus.Suspended;
    }
}
