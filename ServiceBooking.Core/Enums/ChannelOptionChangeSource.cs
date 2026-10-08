namespace ServiceBooking.Core.Enums;

/// <summary>ARCHITECTURE_CYCLE40.md §40.2.2, §40.13 — who changed a channel option (journal ChannelOptionChangeLogs). Stored as int; values are fixed.</summary>
public enum ChannelOptionChangeSource
{
    AdminBillingAccount = 0,
    AdminChannelCard = 1,
    TrialGrant = 2,
    TrialWindowStart = 3,
    TrialExpiry = 4,
    AdminOptionEnded = 5
}
