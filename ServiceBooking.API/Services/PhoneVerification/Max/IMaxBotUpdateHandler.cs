namespace ServiceBooking.API.Services.PhoneVerification.Max;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §498.3 — an extension point of the shared MAX webhook. The phone confirmation (cycle 14) knows nothing of orders: other
/// subsystems register a handler that claims a <c>bot_started</c> payload by its PREFIX and reacts to <c>bot_stopped</c>. With no handler registered,
/// the behaviour of cycle 14 is unchanged bit for bit.
/// </summary>
public interface IMaxBotUpdateHandler
{
    /// <summary>True when the payload of a <c>bot_started</c> belongs to this handler (by prefix).</summary>
    bool CanHandleStart(string payload);

    Task HandleStartAsync(MaxUpdateData update, CancellationToken ct);

    Task HandleStoppedAsync(MaxUpdateData update, CancellationToken ct);
}
