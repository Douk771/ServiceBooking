using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE11.md §102.7/§114.1: the one rule behind both the informational
/// `pricingPublicBlockedReason` field and the 409 on PUT /api/admin/platform-settings. Pure function,
/// no DB, no HTTP.
/// </summary>
public class PricingCatalogCacheBlockReasonTests
{
    private static LegalDocument TermsOwner(bool isDraft) => new(
        LegalDocumentType.TermsOwner, "Title", isDraft ? "v-draft" : "v", new DateOnly(2026, 1, 1), isDraft,
        LegalChangeKind.Material, LegalGate.OwnerScope, [], "<p>text</p>", "hash");

    [Fact]
    public void NoReason_WhenOfferIsPublished()
    {
        var snapshot = new LegalSnapshot(
            new Dictionary<LegalDocumentType, LegalDocument> { [LegalDocumentType.TermsOwner] = TermsOwner(false) },
            new Dictionary<string, LegalUiText>());

        PricingCatalogCache.GetPublicationBlockReason(snapshot).Should().BeNull();
    }

    [Fact]
    public void OfferIsDraft_WhenOfferIsAdraft()
    {
        var snapshot = new LegalSnapshot(
            new Dictionary<LegalDocumentType, LegalDocument> { [LegalDocumentType.TermsOwner] = TermsOwner(true) },
            new Dictionary<string, LegalUiText>());

        PricingCatalogCache.GetPublicationBlockReason(snapshot).Should().Be("OfferIsDraft");
    }

    [Fact]
    public void LegalUnavailable_WhenSnapshotIsNull()
    {
        PricingCatalogCache.GetPublicationBlockReason(null).Should().Be("LegalUnavailable");
    }

    [Fact]
    public void LegalUnavailable_WhenSnapshotHasNoTermsOwnerEntry()
    {
        var snapshot = new LegalSnapshot(new Dictionary<LegalDocumentType, LegalDocument>(), new Dictionary<string, LegalUiText>());

        PricingCatalogCache.GetPublicationBlockReason(snapshot).Should().Be("LegalUnavailable");
    }
}
