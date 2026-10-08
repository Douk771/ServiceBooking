namespace ServiceBooking.Core.Enums;

/// <summary>ARCHITECTURE_CYCLE40.md §40.2.2, §40.8 — outcome of the automatic check message. Stored as int; values are fixed.</summary>
public enum ChannelTestResult
{
    Pending = 0,
    Sending = 1,
    Sent = 2,
    Failed = 3,
    SkippedSameNumber = 4,
    SkippedNoOwnerPhone = 5,
    SkippedPlatformDisabled = 6
}
