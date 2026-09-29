using ServiceBooking.API.Services.PhoneVerification;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Shops;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §405 R-5 — is the phone verification subsystem (MAX) switched on. The same answer
/// <c>GET /api/phone-verification/config</c> gives (<c>enabled</c>); shops read it to refuse the strict mode when it is off.
/// </summary>
public sealed class PhoneVerificationAvailability(IPhoneVerificationMethodRegistry registry)
{
    public bool IsAvailable => registry.Get(PhoneVerificationMethod.MaxBot).Enabled;
}
