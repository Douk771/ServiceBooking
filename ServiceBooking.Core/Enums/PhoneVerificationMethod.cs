namespace ServiceBooking.Core.Enums;

/// <summary>
/// How a phone number was proven to belong to the person who typed it (ARCHITECTURE_CYCLE12.md §142.1,
/// §144.2). Cycle 12 ships exactly one member — <see cref="MaxBot"/> — behind
/// <c>IPhoneVerificationMethodAdapter</c>/<c>IPhoneVerificationMethodRegistry</c>; a call/SMS method is a
/// future adapter registration, not a redesign.
///
/// APPEND-ONLY: participates in persisted data (<see cref="Core.Entities.VerifiedPhone.Method"/>,
/// <see cref="Core.Entities.PhoneVerificationSession.Method"/>) — a reordering would relabel every
/// already-persisted row. A new member is added only at the end.
/// </summary>
public enum PhoneVerificationMethod
{
    MaxBot = 0,
}
