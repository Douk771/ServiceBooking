using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications.GreenApiMax;

/// <summary>
/// Narrows MAX's own <c>stateInstance</c> vocabulary to <see cref="ProviderChannelState"/> — the MAX
/// counterpart of <see cref="GreenApi.GreenApiStateInstanceParser"/>, kept as its OWN class (not a shared
/// switch) because B1's research found the vocabulary genuinely DIFFERS, not just cosmetically renamed:
///
/// MAX's <c>GetStateInstance</c> doc lists <c>notAuthorized</c>, <c>authorized</c>, <c>blocked</c>,
/// <c>starting</c> — the same four WhatsApp has — PLUS TWO MAX-ONLY values this adapter has to decide
/// what to do with:
///
///   <c>suspended</c> — a TEMPORARY, partial restriction ("отправка сообщений возможна только на номера,
///   сохранённые в контакты"), not a full ban. The instance is still genuinely authorized; sends to
///   contacts not already saved on the MAX account fail per-message with 403 "Your account is suspended"
///   — which <see cref="GreenApiMaxResultClassifier"/> already classifies as <c>ChannelInvalid</c> via the
///   existing 401/403 branch, the same outcome WhatsApp's classifier gives an unauthorized instance.
///   Mapped to <see cref="ProviderChannelState.Unknown"/> here so polling NEVER regresses a channel that
///   was Connected a moment ago on the strength of a state that self-resolves — the per-message 403s are
///   what actually surface the problem, the same "ambiguous answer, don't act on it alone" reasoning
///   <c>ChannelStateMapper</c> already applies to a genuinely unrecognised provider answer.
///
///   <c>pendingPassword</c> — the instance scanned a QR but the MAX account also has a login password
///   (2FA) set, and finishing authorization needs a SEPARATE <c>SendAuthorizationPassword</c> call this
///   cycle does not implement (ARCHITECTURE_CYCLE9.md §104.1/§104.9: the connect screen instead tells the
///   owner, BEFORE they start, to disable MAX's login password first — US-119's acceptance criterion).
///   Mapped to <see cref="ProviderChannelState.NotAuthorized"/> — the same bucket as "still scanning, not
///   done yet" — so a channel stuck here for longer than
///   <c>Notifications:UnauthorizedInstanceTimeoutMinutes</c> is abandoned by <c>ChannelHealthTask</c>'s
///   existing timeout exactly the way an ordinary stuck QR flow is, rather than needing a new state and a
///   new abandonment rule this cycle doesn't build.
/// </summary>
public static class GreenApiMaxStateInstanceParser
{
    public static ProviderChannelState Parse(string? raw) => raw switch
    {
        "authorized" => ProviderChannelState.Authorized,
        "notAuthorized" => ProviderChannelState.NotAuthorized,
        "blocked" => ProviderChannelState.Blocked,
        "starting" => ProviderChannelState.Starting,
        "pendingPassword" => ProviderChannelState.NotAuthorized,
        "suspended" => ProviderChannelState.Unknown,
        // Same transitional-state treatment the WhatsApp parser already gives yellowCard/sleepMode —
        // MAX's own docs don't list these, but "unrecognised, still starting" is the safe default either
        // way (never a hard failure on a value this adapter hasn't specifically classified).
        _ => ProviderChannelState.Unknown,
    };
}
