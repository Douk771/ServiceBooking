using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications.GreenApi;

/// <summary>
/// Narrows GREEN-API's own <c>stateInstance</c> vocabulary to <see cref="ProviderChannelState"/>
/// (ARCHITECTURE_CYCLE4.md §28) — shared by <c>getStateInstance</c> polling
/// (<see cref="GreenApiProvisioning.GetStateAsync"/>) and the <c>stateInstance</c> webhook event
/// (<see cref="GreenApiWebhookParser"/>), so the provider's raw strings are recognised in exactly one
/// place.
/// </summary>
public static class GreenApiStateInstanceParser
{
    public static ProviderChannelState Parse(string? raw) => raw switch
    {
        "authorized" => ProviderChannelState.Authorized,
        "notAuthorized" => ProviderChannelState.NotAuthorized,
        "blocked" => ProviderChannelState.Blocked,
        "starting" => ProviderChannelState.Starting,
        // Transitional GREEN-API states, neither authorized nor a hard failure — treated as "still
        // starting up" rather than invented new outcomes on our side.
        "yellowCard" => ProviderChannelState.Starting,
        "sleepMode" => ProviderChannelState.Starting,
        _ => ProviderChannelState.Unknown,
    };
}
