namespace ServiceBooking.API.Services.Signals;

/// <summary>
/// TD-03-quater (SPEC_CYCLE16_TECH_DEBT.md): a channel for events the
/// operator MUST see promptly, but that are not application errors — a new subject request, a subject
/// request approaching its statutory deadline. The application's normal Serilog→Sentry sink (Program.cs)
/// intentionally only forwards <c>LogEventLevel.Error</c> to GlitchTip ("only real failures become
/// GlitchTip issues" — its own comment); routing operational signals through it would either force every
/// such signal to masquerade as an error (poisoning the error stream) or never arrive at all. This
/// service instead posts a minimal Sentry-protocol envelope DIRECTLY to the same DSN's store endpoint,
/// bypassing Serilog entirely — same pattern as <c>deploy/backup/backup.sh</c>'s <c>send_glitchtip_event</c>
/// (the prior art this cycle's spec names explicitly).
///
/// 🔴 Composition of every message sent through this service is constrained by the legal review §6 this
/// cycle answers: request KIND, REFERENCE and DUE DATE only. Never a phone number, never request text,
/// never a hash of a phone number (reversible by brute force — the phone-number space is small), and
/// never a count of matched records. Callers must not pass anything else.
/// </summary>
public interface IGlitchTipSignalService
{
    /// <summary>Sends one informational signal. No-op (and never throws) when no DSN is configured —
    /// same "absence of monitoring must not break the feature it's watching" contract as the rest of the
    /// GlitchTip integration (ARCHITECTURE.md §11.4). Returns whether the event was actually accepted by
    /// GlitchTip (true) or skipped/rejected/failed to send (false) — callers that report a summary of how
    /// many signals went out MUST use this rather than assuming every call succeeded (code review, cycle
    /// 16): every failure path here is deliberately swallowed rather than thrown, so the return value is
    /// the only way to tell success from best-effort failure.</summary>
    Task<bool> SendAsync(string message, CancellationToken ct);
}
