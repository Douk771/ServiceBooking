using FluentAssertions;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// Contract check, cycle 11, round 2 (schemathesis re-run): GET /api/admin/legal/readiness's response
/// didn't match contracts/cycle11/legal-status.schema.json — several top-level required fields
/// (root/links/anchors/placeholders) were missing entirely, `impact` was a bare array instead of the
/// schema's `{ reAcceptanceRequired, note }` object, and per-document/uiText `file` was absent. These
/// tests exercise the pure link/anchor/placeholder-summary helpers directly, in-memory, without a
/// server, HTTP client, or database — the readiness endpoint's own DB-backed `impact` branch is left to
/// qa-engineer's functional suite (it needs a real ConsentRecords table to be meaningful).
/// </summary>
public class AdminLegalControllerReadinessHelpersTests
{
    private static LegalDocument Document(LegalDocumentType type, string file, string html) => new(
        type, "Title", "v1", new DateOnly(2026, 1, 1), IsDraft: false,
        LegalChangeKind.Material, LegalGate.None, [], html, "hash", file);

    private static LegalSnapshot Snapshot(params LegalDocument[] documents)
    {
        var all = Enum.GetValues<LegalDocumentType>()
            .ToDictionary(t => t, t => documents.FirstOrDefault(d => d.Type == t) ?? Document(t, $"{t}.html", "<p>text</p>"));
        return new LegalSnapshot(all, new Dictionary<string, LegalUiText>());
    }

    [Fact]
    public void CheckLinks_KnownRoute_IsNotBroken()
    {
        var snapshot = Snapshot(Document(
            LegalDocumentType.TermsOwner, "terms-owner.html", "<a href=\"/privacy\">privacy</a>"));

        var result = AdminLegalController.CheckLinks(snapshot);

        result.Checked.Should().Be(1);
        result.Broken.Should().BeEmpty();
    }

    [Fact]
    public void CheckLinks_UnknownRoute_IsReportedBroken()
    {
        var snapshot = Snapshot(Document(
            LegalDocumentType.TermsOwner, "terms-owner.html", "<a href=\"/no-such-route\">nope</a>"));

        var result = AdminLegalController.CheckLinks(snapshot);

        result.Checked.Should().Be(1);
        result.Broken.Should().ContainSingle(b => b.File == "terms-owner.html" && b.Href == "/no-such-route");
    }

    [Fact]
    public void CheckLinks_AliasRoute_IsNotBroken()
    {
        var snapshot = Snapshot(Document(
            LegalDocumentType.TermsClient, "terms.html", "<a href=\"/offer-channel\">offer</a>"));

        var result = AdminLegalController.CheckLinks(snapshot);

        result.Broken.Should().BeEmpty();
    }

    [Fact]
    public void CheckAnchors_RequiredAnchorPresent_IsNotMissing()
    {
        var snapshot = Snapshot(Document(
            LegalDocumentType.TermsOwner, "terms-owner.html", "<h2 id=\"offer-channel\">Offer</h2>"));

        var result = AdminLegalController.CheckAnchors(snapshot);

        result.Missing.Should().BeEmpty();
    }

    [Fact]
    public void CheckAnchors_RequiredAnchorAbsent_IsReportedMissing()
    {
        var snapshot = Snapshot(Document(LegalDocumentType.TermsOwner, "terms-owner.html", "<p>no anchor here</p>"));

        var result = AdminLegalController.CheckAnchors(snapshot);

        result.Missing.Should().ContainSingle(m => m.Route == "/terms-owner" && m.Anchor == "offer-channel");
    }

    [Fact]
    public void BuildPlaceholderSummary_AggregatesAcrossDocumentsAndUiTexts()
    {
        var documents = new List<LegalReadinessDocumentDto>
        {
            new("Privacy", "T", "v1", new DateOnly(2026, 1, 1), false, "Material", "Global", "privacy.html",
                "/privacy", "hash", [new LegalReadinessPlaceholderDto("ИНН", 2)]),
        };
        var uiTexts = new List<LegalReadinessUiTextDto>
        {
            new("BookingNotice", "v1", false, "booking-notice.html", "hash", [new LegalReadinessPlaceholderDto("ИНН", 1)]),
        };

        var result = AdminLegalController.BuildPlaceholderSummary(documents, uiTexts);

        result.Should().ContainSingle();
        var summary = result[0];
        summary.Name.Should().Be("ИНН");
        summary.Count.Should().Be(3);
        summary.Files.Should().BeEquivalentTo(["privacy.html", "booking-notice.html"]);
        summary.ValuePresent.Should().BeFalse();
    }

    [Theory]
    [InlineData("НАИМЕНОВАНИЕ_ОПЕРАТОРА", "ЕГРЮЛ")]
    [InlineData("ИНН_ОПЕРАТОРА", "ЕГРЮЛ")]
    [InlineData("ОГРН_ОПЕРАТОРА", "ЕГРЮЛ")]
    [InlineData("ЮРИДИЧЕСКИЙ_АДРЕС", "ЕГРЮЛ")]
    [InlineData("НОМЕР_УВЕДОМЛЕНИЯ_РКН", "после уведомления РКН")]
    [InlineData("ДАТА_УВЕДОМЛЕНИЯ_РКН", "после уведомления РКН")]
    [InlineData("ПОЧТОВЫЙ_АДРЕС", "решение заказчика")]
    [InlineData("ПОЧТА_ДЛЯ_ОБРАЩЕНИЙ", "решение заказчика")]
    [InlineData("ТЕЛЕФОН_ОПЕРАТОРА", "решение заказчика")]
    [InlineData("ОТВЕТСТВЕННЫЙ_ЗА_ОБРАБОТКУ", "решение заказчика")]
    [InlineData("ПОЧТА_ОТВЕТСТВЕННОГО", "решение заказчика")]
    [InlineData("СРОК_ОТВЕТА_НА_ОБРАЩЕНИЕ", "решение заказчика")]
    [InlineData("НДС_ОГОВОРКА", "решение заказчика")]
    [InlineData("ВЕРСИЯ_ДОКУМЕНТА", "из манифеста")]
    [InlineData("ДАТА_ВСТУПЛЕНИЯ_В_СИЛУ", "из манифеста")]
    [InlineData("НЕИЗВЕСТНЫЙ_ПЛЕЙСХОЛДЕР", "из манифеста")]
    public void BuildPlaceholderSummary_ClassifiesSourcePerLegalReview13Bis(string placeholderName, string expectedSource)
    {
        var documents = new List<LegalReadinessDocumentDto>
        {
            new("Privacy", "T", "v1", new DateOnly(2026, 1, 1), false, "Material", "Global", "privacy.html",
                "/privacy", "hash", [new LegalReadinessPlaceholderDto(placeholderName, 1)]),
        };

        var result = AdminLegalController.BuildPlaceholderSummary(documents, []);

        result.Should().ContainSingle().Which.Source.Should().Be(expectedSource);
    }

    [Fact]
    public void BuildPlaceholderSummary_NoPlaceholders_ReturnsEmpty()
    {
        var documents = new List<LegalReadinessDocumentDto>
        {
            new("Privacy", "T", "v1", new DateOnly(2026, 1, 1), false, "Material", "Global", "privacy.html",
                "/privacy", "hash", []),
        };

        var result = AdminLegalController.BuildPlaceholderSummary(documents, []);

        result.Should().BeEmpty();
    }
}
