using FluentAssertions;
using ServiceBooking.LegalKit;

namespace ServiceBooking.UnitTests.LegalKit;

/// <summary>Risk A4 (API_CONTRACT_CYCLE11.md §112, SPEC R10): a placeholder written in Latin or lowercase
/// Cyrillic must NOT be silently accepted as "filled in" just because it doesn't match the product's
/// narrow, Cyrillic-uppercase-only regex.</summary>
public class PlaceholderScannerTests
{
    [Fact]
    public void FindUnknownForms_LatinPlaceholder_IsFlagged()
    {
        var result = PlaceholderScanner.FindUnknownForms("<p>{{OPERATOR_NAME}}</p>");
        result.Should().ContainSingle().Which.Should().Be("{{OPERATOR_NAME}}");
    }

    [Fact]
    public void FindUnknownForms_LowercaseCyrillicPlaceholder_IsFlagged()
    {
        var result = PlaceholderScanner.FindUnknownForms("<p>{{наименование_оператора}}</p>");
        result.Should().ContainSingle();
    }

    [Fact]
    public void FindUnknownForms_KnownUppercaseCyrillicName_IsNotFlagged()
    {
        var result = PlaceholderScanner.FindUnknownForms("<p>{{НАИМЕНОВАНИЕ_ОПЕРАТОРА}}</p>");
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindUnknownForms_ManifestDerivedNames_AreNotFlagged()
    {
        var result = PlaceholderScanner.FindUnknownForms("<p>{{ВЕРСИЯ_ДОКУМЕНТА}} {{ДАТА_ВСТУПЛЕНИЯ_В_СИЛУ}}</p>");
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindUnknownForms_UnrecognizedCyrillicUppercaseName_IsFlagged()
    {
        // Correctly-formed (matches the shared regex) but not one of the 15 known names — still a defect.
        var result = PlaceholderScanner.FindUnknownForms("<p>{{СОВЕРШЕННО_НЕИЗВЕСТНЫЙ}}</p>");
        result.Should().ContainSingle();
    }

    [Fact]
    public void KnownPattern_IsExactlyLegalDocumentProviderPlaceholderPattern()
    {
        // §106.3: one regex for the whole repository — this assertion is what keeps that true.
        PlaceholderScanner.KnownPattern.Should()
            .Be(ServiceBooking.API.Services.Legal.LegalDocumentProvider.PlaceholderPattern);
    }

    [Fact]
    public void FindAllTokens_MixOfKnownAndUnknown_ReturnsBoth()
    {
        var result = PlaceholderScanner.FindAllTokens("<p>{{НАИМЕНОВАНИЕ_ОПЕРАТОРА}} {{OPERATOR_NAME}}</p>");
        result.Should().BeEquivalentTo(["{{НАИМЕНОВАНИЕ_ОПЕРАТОРА}}", "{{OPERATOR_NAME}}"]);
    }
}
