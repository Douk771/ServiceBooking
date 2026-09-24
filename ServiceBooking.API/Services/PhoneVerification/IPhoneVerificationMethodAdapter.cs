using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.PhoneVerification;

/// <summary>
/// What starting a verification challenge produces (ARCHITECTURE_CYCLE12.md §144.2). Method-specific
/// content (for MAX: a deep link and, optionally, a QR PNG) alongside <see cref="PayloadHash"/> — the ONE
/// piece every method must produce, since it's what <c>PhoneVerificationSession.PayloadHash</c> is looked
/// up by when that method's own inbound event (MAX's webhook, a future call/SMS callback) arrives. The
/// raw secret itself is never returned to the caller a second time — <see cref="IPhoneVerificationMethodAdapter.StartAsync"/>'s
/// caller (<c>PhoneVerificationSessionService</c>) only persists the hash.
/// </summary>
public sealed record VerificationChallenge(string PayloadHash, string DeepLink, byte[]? QrPng);

/// <summary>
/// Port under future verification methods (ARCHITECTURE_CYCLE12.md §144.2, Q2). Cycle 12 ships exactly
/// one implementation (<c>Max.MaxBotVerificationAdapter</c>) — this interface exists so a second method
/// (call/SMS, a later cycle) is a new adapter registration, never a rewrite of
/// <c>PhoneVerificationSessionService</c>/the controller.
/// </summary>
public interface IPhoneVerificationMethodAdapter
{
    PhoneVerificationMethod Method { get; }

    /// <summary>Whether THIS method is currently usable — for MAX, whether
    /// <c>PhoneVerification:Provider</c> is <c>"max-bot"</c> (not merely whether an adapter class exists:
    /// the adapter is always registered, §150.3's "структурно заменён", so the network client behind it
    /// can be swapped for a stub without touching the registry's completeness).</summary>
    bool Enabled { get; }

    /// <summary>Produces this method's own one-time secret and whatever the frontend shows the person
    /// (deep link/QR for MAX; a future call/SMS method's own challenge shape). Makes NO outbound network
    /// call — MAX only learns about a session when the person themselves opens the link (§145.1 step 1,
    /// О2 "бот не пишет первым").</summary>
    Task<VerificationChallenge> StartAsync(PhoneVerificationSession session, CancellationToken ct);

    /// <summary>Best-effort cleanup when a session is explicitly cancelled (§166) — for MAX, currently a
    /// no-op (there is nothing at the provider to undo before the person ever opens the bot).</summary>
    Task CancelAsync(PhoneVerificationSession session, CancellationToken ct);
}

/// <summary>
/// Resolves the right <see cref="IPhoneVerificationMethodAdapter"/> for a
/// <see cref="PhoneVerificationMethod"/> (§144.2). <see cref="Get"/> throws
/// <see cref="MissingVerificationMethodImplementationException"/> for an enum member with no registered
/// adapter — checked complete at startup (<c>DeploymentSafetyChecks.ValidateVerificationMethodRegistry</c>),
/// mirroring cycle 9's <c>INotificationTransportRegistry</c>.
/// </summary>
public interface IPhoneVerificationMethodRegistry
{
    IPhoneVerificationMethodAdapter Get(PhoneVerificationMethod method);

    IReadOnlyCollection<PhoneVerificationMethod> Registered { get; }
}

/// <summary>Thrown by <see cref="IPhoneVerificationMethodRegistry.Get"/> when
/// <see cref="PhoneVerificationMethod"/> gained a member with no matching adapter wired up in
/// <c>Program.cs</c> — a deployment defect, never a runtime condition a caller should route around.</summary>
public sealed class MissingVerificationMethodImplementationException(PhoneVerificationMethod method)
    : InvalidOperationException($"No IPhoneVerificationMethodAdapter is registered for method '{method}'.")
{
    public PhoneVerificationMethod Method { get; } = method;
}

/// <summary>Dictionary-backed default implementation, built once in <c>Program.cs</c> from whatever
/// adapters DI produced.</summary>
public sealed class PhoneVerificationMethodRegistry : IPhoneVerificationMethodRegistry
{
    private readonly IReadOnlyDictionary<PhoneVerificationMethod, IPhoneVerificationMethodAdapter> _adapters;

    public PhoneVerificationMethodRegistry(IEnumerable<IPhoneVerificationMethodAdapter> adapters) =>
        _adapters = adapters.ToDictionary(a => a.Method);

    public IPhoneVerificationMethodAdapter Get(PhoneVerificationMethod method) =>
        _adapters.TryGetValue(method, out var adapter)
            ? adapter
            : throw new MissingVerificationMethodImplementationException(method);

    public IReadOnlyCollection<PhoneVerificationMethod> Registered => [.. _adapters.Keys];
}
