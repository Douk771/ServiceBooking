namespace ServiceBooking.API.Services.Billing;

/// <summary>Cycle 7 (ARCHITECTURE_CYCLE7.md §47.1), reworked by cycle 40 (ARCHITECTURE_CYCLE40.md §40.3.2): the funding rank of
/// one number. <see cref="Funded"/> is not "connected"/"working" in the transport sense; it only says "this is the first live
/// number of a transport that is paid". Computed by <c>TransportFunding.Rank</c> (pure) via <c>AccountMessagingReader</c>.</summary>
public enum ChannelFundingState
{
    Funded,
    Unfunded,
    NotPaid,
}
