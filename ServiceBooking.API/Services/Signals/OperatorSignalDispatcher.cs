namespace ServiceBooking.API.Services.Signals;

/// <summary>
/// Sends an operator signal off the request path (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.11): fire-and-forget with <see cref="CancellationToken.None"/>.
/// Every exception is logged as a warning and swallowed — a failing signal channel must never change the answer of the endpoint it watches
/// (same pattern as <c>SubjectRequestsController</c>).
/// </summary>
public static class OperatorSignalDispatcher
{
    public static void FireAndForget(IGlitchTipSignalService signals, ILogger logger, string message) =>
        _ = SendAsync(signals, logger, message);

    private static async Task SendAsync(IGlitchTipSignalService signals, ILogger logger, string message)
    {
        try
        {
            await signals.SendAsync(message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Operator signal about a new billing request failed to send.");
        }
    }
}
