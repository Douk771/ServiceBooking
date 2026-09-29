using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>The one place a channel's payment state is computed (ARCHITECTURE_CYCLE4.md §23.2).
/// Cycle 22 (ARCHITECTURE_CYCLE22.md §379, Р2): the source is the channel's FUNDING
/// (<see cref="ChannelFundingReader"/> — the account's notifications.whatsapp option ranked over its live
/// channels), not the dropped NotificationChannel.PaidUntilUtc column. Same rule the company settings
/// screen already used: suspended by the admin → Suspended; funded → Paid; otherwise NotPaid.</summary>
public static class ChannelPaymentState
{
    public static ChannelPaymentStatus Of(NotificationChannel channel, ChannelFundingInfo? funding) =>
        Of(channel.IsSuspendedByAdmin, funding?.State == ChannelFundingState.Funded);

    public static ChannelPaymentStatus Of(bool isSuspendedByAdmin, bool isFunded)
    {
        if (isSuspendedByAdmin) return ChannelPaymentStatus.Suspended;
        return isFunded ? ChannelPaymentStatus.Paid : ChannelPaymentStatus.NotPaid;
    }
}
