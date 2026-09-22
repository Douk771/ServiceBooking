namespace ServiceBooking.Core.Enums;

/// <summary>
/// The granular purposes a PdnConsent (or, for ChannelOffer, TermsOwner) grant can cover
/// (ARCHITECTURE_CYCLE5.md §43.3 `purposes`, §44.2). Null on a ConsentRecord means "this document as a
/// whole" (Privacy, TermsClient, TermsOwner's CompanyCreation row) — Purpose only ever has a value
/// on rows that are actually purpose-scoped.
/// </summary>
public enum ConsentPurpose
{
    ProviderDelivery,
    WorkPhotos,
    HealthData,
    ChannelOffer
}
