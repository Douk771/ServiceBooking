namespace ServiceBooking.Core.Enums;

/// <summary>
/// A channel's payment state — deliberately COMPUTED, never stored (ARCHITECTURE_CYCLE4.md §23.2,
/// SPEC §15.1). See <c>ChannelPaymentState.Of</c> for the one place this is calculated.
/// </summary>
public enum ChannelPaymentStatus
{
    NotPaid,
    Paid,
    Suspended,
}
