namespace ServiceBooking.API.Services.PhoneVerification;

/// <summary>What <c>POST /api/profile/change-phone</c> should do next (ARCHITECTURE_CYCLE12.md §148.5,
/// US-12-17). Pure — a function of three already-evaluated facts, with no knowledge of HTTP status codes
/// or wording; the controller maps this to 200/409 and picks the exact text.</summary>
public enum ChangePhoneGateOutcome
{
    /// <summary>Proceed with the change exactly as before this cycle — either the number isn't actually
    /// changing, the new number has no guest bookings on it, or a valid confirming session was
    /// presented.</summary>
    Allow,

    /// <summary>The new number has guest bookings and no valid session was presented, while the subsystem
    /// is enabled — 409, "confirm it through MAX first".</summary>
    RequireVerification,

    /// <summary>Same situation, but the subsystem itself is switched off — 409 with the honest "cannot
    /// verify right now" text (§148.5's mandatory honest-refusal branch), never the misleading
    /// RequireVerification wording for a button that doesn't exist anywhere on the page.</summary>
    SubsystemDisabled,
}

/// <summary>
/// Р3/Р6's gate, isolated to one pure decision (ARCHITECTURE_CYCLE12.md §144.3). Every fact it needs —
/// whether the number is actually changing, whether <c>GuestBookingLookup</c> found a guest booking on
/// the new number, whether a session satisfying §148.5's five conditions was presented, whether the
/// subsystem is enabled — is evaluated by the caller; this function only encodes the DECISION TABLE.
/// </summary>
public static class GuestBookingGateDecision
{
    public static ChangePhoneGateOutcome Evaluate(
        bool phoneIsChanging, bool newNumberHasGuestBookings, bool validSessionPresented, bool subsystemEnabled)
    {
        if (!phoneIsChanging) return ChangePhoneGateOutcome.Allow;
        if (!newNumberHasGuestBookings) return ChangePhoneGateOutcome.Allow;
        if (validSessionPresented) return ChangePhoneGateOutcome.Allow;

        return subsystemEnabled ? ChangePhoneGateOutcome.RequireVerification : ChangePhoneGateOutcome.SubsystemDisabled;
    }
}
