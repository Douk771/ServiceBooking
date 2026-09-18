namespace ServiceBooking.Core.Enums;

/// <summary>
/// A WhatsApp channel's connection state (ARCHITECTURE_CYCLE4.md §23.2). The first seven are exactly
/// the list SPEC US-55 p.1 shows the owner; <see cref="Replaced"/> is an eighth, terminal state for a
/// channel superseded by a number swap after a ban (US-63 p.2) — without it a replaced channel would
/// have to be shown as <see cref="Blocked"/>, and the owner would see two identically red channels with
/// no way to tell which one is still in use. This is a documented, deliberate divergence from "exactly
/// seven states" (§38.1), not an oversight.
/// </summary>
public enum ChannelState
{
    NotConnected,
    Connecting,
    Connected,
    Disconnected,
    Blocked,
    DisabledByOwner,
    NeedsReconnect,
    Replaced,
}
