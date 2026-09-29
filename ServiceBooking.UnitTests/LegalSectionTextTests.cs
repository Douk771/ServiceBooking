using FluentAssertions;
using ServiceBooking.API.Services.Legal;

namespace ServiceBooking.UnitTests;

/// <summary>CY20-U-08 (ARCHITECTURE_CYCLE20.md §411, §417, Т20-08 п. 2) — pure, no EF/HTTP.</summary>
public class LegalSectionTextTests
{
    [Fact]
    public void PlainSection_ExtractsOnlyTheMatchingHeadingsBody()
    {
        var html = """
            <p><em>Служебная справка для команды: не должна попасть в вывод.</em></p>
            <h2>Текст</h2>
            <p><strong>Заголовок</strong></p>
            <p>Первый абзац.</p>
            <ol><li>Пункт один.</li><li>Пункт два.</li></ol>
            <h2>Другой раздел</h2>
            <p>Этого тоже быть не должно.</p>
            """;

        var result = LegalSectionText.PlainSection(html, "Текст");

        result.Should().NotBeNull();
        result.Should().NotContain("Служебная справка");
        result.Should().NotContain("Другой раздел");
        result.Should().NotContain("Этого тоже быть не должно");
        result.Should().Contain("Заголовок");
        result.Should().Contain("Первый абзац.");
        result.Should().Contain("Пункт один.");
        result.Should().NotContain("<p>").And.NotContain("<strong>").And.NotContain("<li>");
    }

    [Fact]
    public void PlainSection_MatchIsCaseAndWhitespaceInsensitive()
    {
        var html = "<h2>  текст  </h2><p>тело</p>";
        LegalSectionText.PlainSection(html, "Текст").Should().Be("тело");
    }

    [Fact]
    public void PlainSection_NoMatchingHeading_ReturnsNull()
    {
        var html = "<h2>Прочее</h2><p>тело</p>";
        LegalSectionText.PlainSection(html, "Текст").Should().BeNull();
    }

    [Fact]
    public void PlainSection_DecodesHtmlEntities()
    {
        var html = "<h2>Текст</h2><p>Салон &laquo;Лотос&raquo; &amp; партнёры</p>";
        LegalSectionText.PlainSection(html, "Текст").Should().Be("Салон «Лотос» & партнёры");
    }

    [Fact]
    public void PlainSection_JoinsParagraphsWithBlankLine()
    {
        var html = "<h2>Текст</h2><p>Раз.</p><p>Два.</p>";
        LegalSectionText.PlainSection(html, "Текст").Should().Be("Раз.\n\nДва.");
    }

    /// <summary>Same real-document smoke test convention as
    /// <c>RuntimeValueFormsCorpusTests.RealBookingNoticeDocument_HasNoUnknownRuntimeValueNames</c> — the
    /// committed 14-guest-data-gate-notice.html must actually have a "Текст" section with no leftover
    /// markup/service commentary once extracted, since this is exactly what ProfileController's export
    /// ships to a real subject.</summary>
    [Fact]
    public void RealGuestDataGateNoticeDocument_TextSectionHasNoServiceCommentaryOrMarkup()
    {
        var path = FindRepoFile(Path.Combine("ServiceBooking.API", "App_Data", "legal", "14-guest-data-gate-notice.html"));
        var html = File.ReadAllText(path);

        var plain = LegalSectionText.PlainSection(html, "Текст");

        plain.Should().NotBeNullOrEmpty();
        plain.Should().NotContain("Служебная справка");
        plain.Should().NotContain("<");
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "ServiceBooking.sln")))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate '{relativePath}' by walking up from the test output directory to the " +
            "repository root (marked by 'ServiceBooking.sln').");
    }
}
