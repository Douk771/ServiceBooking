using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.11.3 (Т40-L-07) — a signed-in customer's <c>true</c> messenger mark IS the provider-delivery consent
/// (<c>PdnConsent</c>/<c>ProviderDelivery</c>): it is entered in the consent journal once, when there is no current grant. A guest's mark is proved by
/// the record itself and never comes here. <see cref="ConsentLedger.GrantAsync"/> owns its transaction and cannot nest in a caller's one, so this runs
/// right after the booking/order/stay commits; the caller catches and logs a failure — the record is never undone for it.
/// </summary>
public static class MessengerOptInLedger
{
    public static async Task GrantForCustomerAsync(
        ConsentLedger ledger, LegalDocumentProvider legal, string userId, ConsentSource source, string? remoteIp, CancellationToken ct)
    {
        var subject = ConsentSubject.ForUser(userId);
        if (await ledger.CurrentAsync(subject, LegalDocumentType.PdnConsent.ToString(), ConsentPurpose.ProviderDelivery, ct) is not null) return;
        var pdn = legal.Current?.Get(LegalDocumentType.PdnConsent);
        if (pdn is null) return;
        await ledger.GrantAsync(new ConsentGrant(
            subject, LegalDocumentType.PdnConsent.ToString(), pdn.Version, pdn.ContentHash,
            ConsentPurpose.ProviderDelivery, ConsentAct.Accepted, source, IpAddress: remoteIp), ct);
    }
}
