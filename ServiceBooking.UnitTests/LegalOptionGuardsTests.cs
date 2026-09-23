using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE11.md §102.10 (Q11): notifications.whatsapp is only publicly sellable while
/// TermsOwner (which the channel offer D9 is spliced into, §102.2) is published. No DB, no HTTP —
/// LegalSnapshot is built in-memory directly.
/// </summary>
public class LegalOptionGuardsTests
{
    private static LegalDocument Document(LegalDocumentType type, bool isDraft) => new(
        type, "Title", isDraft ? "2026-01-01-draft" : "2026-01-01", new DateOnly(2026, 1, 1), isDraft,
        LegalChangeKind.Material, LegalGate.OwnerScope, [], "<p>text</p>", "hash", "file.html");

    private static LegalSnapshot SnapshotWith(params LegalDocument[] documents) =>
        new(documents.ToDictionary(d => d.Type), new Dictionary<string, LegalUiText>());

    [Fact]
    public void UnguardedOption_IsAlwaysSellable_RegardlessOfSnapshot()
    {
        LegalOptionGuards.IsPubliclySellable("extra-companies", snapshot: null).Should().BeTrue();
    }

    [Fact]
    public void GuardedOption_IsSellable_WhenRequiredDocumentIsPublished()
    {
        var snapshot = SnapshotWith(Document(LegalDocumentType.TermsOwner, isDraft: false));

        LegalOptionGuards.IsPubliclySellable("notifications.whatsapp", snapshot).Should().BeTrue();
    }

    [Fact]
    public void GuardedOption_IsNotSellable_WhenRequiredDocumentIsDraft()
    {
        var snapshot = SnapshotWith(Document(LegalDocumentType.TermsOwner, isDraft: true));

        LegalOptionGuards.IsPubliclySellable("notifications.whatsapp", snapshot).Should().BeFalse();
    }

    [Fact]
    public void GuardedOption_IsNotSellable_WhenSnapshotIsMissing()
    {
        LegalOptionGuards.IsPubliclySellable("notifications.whatsapp", snapshot: null).Should().BeFalse();
    }

    [Fact]
    public void GuardedOption_IsCaseInsensitiveOnCode()
    {
        var snapshot = SnapshotWith(Document(LegalDocumentType.TermsOwner, isDraft: false));

        LegalOptionGuards.IsPubliclySellable("NOTIFICATIONS.WHATSAPP", snapshot).Should().BeTrue();
    }
}
