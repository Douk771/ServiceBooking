using System.Globalization;
using FluentAssertions;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// LEGAL_REVIEW_CYCLE20.md §11 (task L3), ARCHITECTURE_CYCLE20.md §404.3 (US-20-03) — pure template
/// rendering, no EF/HTTP. Rule 3 ("в сохранённом снимке не должно остаться ни одного {/}") is checked
/// here for every kind, as the legal review's own text demands ("проверка в тесте").
/// </summary>
public class PlatformNoticeTextsTests
{
    private static readonly DateOnly EffectiveFrom = new(2026, 11, 1);

    private static readonly CultureInfo RuRu = CultureInfo.GetCultureInfo("ru-RU");

    [Fact]
    public void BuildPriceChange_RendersDateNameAndAmounts_WithNoLeftoverBraces()
    {
        var (title, body) = PlatformNoticeTexts.BuildPriceChange("Бизнес", 990m, 1190m, EffectiveFrom);

        title.Should().Be("Тариф «Бизнес»: новая цена с 01.11.2026");
        body.Should().Contain($"С 01.11.2026 меняется цена «Бизнес»: было {990m.ToString("N0", RuRu)} ₽, " +
                              $"станет {1190m.ToString("N0", RuRu)} ₽ за месяц.");
        body.Should().Contain("пункты 6.13.9, 6.13.10 и 16.4 Соглашения с компанией");
        AssertNoBraces(title, body);
    }

    [Fact]
    public void BuildPriceChange_FractionalAmount_KeepsTwoDecimals()
    {
        var (_, body) = PlatformNoticeTexts.BuildPriceChange("Старт", 490.50m, 590m, EffectiveFrom);
        body.Should().Contain($"было {490.50m.ToString("N2", RuRu)} ₽");
        body.Should().Contain($"станет {590m.ToString("N0", RuRu)} ₽");
    }

    [Fact]
    public void BuildPriceChange_LongPlanName_TruncatesOnlyTheName()
    {
        var longName = new string('А', 250);
        var (title, _) = PlatformNoticeTexts.BuildPriceChange(longName, 100m, 200m, EffectiveFrom);

        title.Length.Should().BeLessThanOrEqualTo(PlatformNoticeRules.MaxTitleLength);
        title.Should().StartWith("Тариф «").And.Contain("…»: новая цена с 01.11.2026");
    }

    [Theory]
    [InlineData(LegalDocumentType.TermsOwner, "Соглашение с компанией: новая редакция с 01.11.2026")]
    [InlineData(LegalDocumentType.TermsClient, "Пользовательское соглашение: новая редакция с 01.11.2026")]
    [InlineData(LegalDocumentType.Privacy, "Политика обработки персональных данных: новая редакция с 01.11.2026")]
    public void BuildTermsChange_UsesTheRightTitlePerDocumentType(LegalDocumentType documentType, string expectedTitle)
    {
        var (title, body) = PlatformNoticeTexts.BuildTermsChange(documentType, "уточнён порядок уведомлений в кабинете.", EffectiveFrom);
        title.Should().Be(expectedTitle);
        AssertNoBraces(title, body);
    }

    [Fact]
    public void BuildTermsChange_TermsOwner_ContainsExpectedBodyFragments()
    {
        var (_, body) = PlatformNoticeTexts.BuildTermsChange(LegalDocumentType.TermsOwner, "уточнён порядок уведомлений в кабинете", EffectiveFrom);
        body.Should().Contain("С 01.11.2026 действует новая редакция Соглашения с компанией.");
        body.Should().Contain("Что меняется: уточнён порядок уведомлений в кабинете.");
        body.Should().Contain("пункт 16.4");
    }

    [Fact]
    public void BuildTermsChange_TermsClient_ContainsNoRetroactivityFragment()
    {
        var (_, body) = PlatformNoticeTexts.BuildTermsChange(LegalDocumentType.TermsClient, "новый раздел про отмену записи", EffectiveFrom);
        body.Should().Contain("не имеет обратной силы");
        body.Should().Contain("пункт 21.3 Пользовательского соглашения");
        body.Should().Contain("пункт 21.4 Пользовательского соглашения");
    }

    [Fact]
    public void BuildTermsChange_Privacy_ContainsDataSubjectRightsFragment()
    {
        var (_, body) = PlatformNoticeTexts.BuildTermsChange(LegalDocumentType.Privacy, "срок хранения журнала изменений записи — 3 года", EffectiveFrom);
        body.Should().Contain("не отменяет и не ограничивает ваших прав");
        body.Should().Contain("раздела 15 Политики");
    }

    [Theory]
    [InlineData("уточнены отдельные положения.")]
    [InlineData("уточнены отдельные положения. ")]
    [InlineData("уточнены отдельные положения!")]
    [InlineData("уточнены отдельные положения;")]
    [InlineData("уточнены отдельные положения ; . !")]
    public void BuildTermsChange_TrimsTrailingPunctuationFromChangesSummary(string changesSummary)
    {
        var (_, body) = PlatformNoticeTexts.BuildTermsChange(LegalDocumentType.Privacy, changesSummary, EffectiveFrom);
        body.Should().Contain("Что меняется: уточнены отдельные положения.");
        body.Should().NotContain("положения. .");
        body.Should().NotContain("положения ;");
    }

    [Fact]
    public void BuildTermsChange_UnknownDocumentType_Throws()
    {
        var act = () => PlatformNoticeTexts.BuildTermsChange(LegalDocumentType.PdnConsent, "x", EffectiveFrom);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BuildPhotoRemoved_RendersCompanyNameAndDate_WithNoLeftoverBraces()
    {
        var (title, body) = PlatformNoticeTexts.BuildPhotoRemoved("Салон красоты «Ромашка»", new DateOnly(2026, 10, 5));

        title.Should().Be("Фотография компании «Салон красоты «Ромашка»» удалена по просьбе изображённого на ней человека");
        body.Should().StartWith("05.10.2026 мы удалили из сервиса одну из фотографий компании «Салон красоты «Ромашка»».");
        body.Should().Contain("статьи 152.1 Гражданского кодекса РФ");
        body.Should().Contain("пункт 8.8 Соглашения с компанией");
        body.Should().Contain("пункте 4.1 Соглашения с компанией");
        AssertNoBraces(title, body);
    }

    [Fact]
    public void BuildPhotoRemoved_NeverMentionsARequesterIdentity()
    {
        // §11.3's own requirement: no name/contact of the person who asked for removal.
        var (_, body) = PlatformNoticeTexts.BuildPhotoRemoved("Компания", new DateOnly(2026, 10, 5));
        body.Should().Contain("Сведения о том, кто обратился, мы не сообщаем.");
    }

    [Fact]
    public void BuildPhotoRemoved_LongCompanyName_TruncatesOnlyTheName()
    {
        var longName = new string('Б', 250);
        var (title, _) = PlatformNoticeTexts.BuildPhotoRemoved(longName, new DateOnly(2026, 10, 5));
        title.Length.Should().BeLessThanOrEqualTo(PlatformNoticeRules.MaxTitleLength);
        title.Should().EndWith("…» удалена по просьбе изображённого на ней человека");
    }

    [Fact]
    public void AcknowledgeButtonAndCaption_MatchTheLegalReviewVerbatim()
    {
        PlatformNoticeTexts.AcknowledgeButtonText.Should().Be("Я ознакомился");
        PlatformNoticeTexts.AcknowledgeCaption.Should().Be("Это подтверждает только то, что вы прочитали сообщение, а не согласие с ним.");
    }

    private static void AssertNoBraces(string title, string body)
    {
        title.Should().NotContain("{").And.NotContain("}");
        body.Should().NotContain("{").And.NotContain("}");
    }
}
