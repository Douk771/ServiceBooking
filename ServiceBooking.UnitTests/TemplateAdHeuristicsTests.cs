using FluentAssertions;
using ServiceBooking.API.Services.Notifications;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE5.md §51.2 — pure substring scan, no DB, no NLP.</summary>
public class TemplateAdHeuristicsTests
{
    private static readonly string[] Markers = ["скидк", "акци", "промо", "%", "бесплатн"];

    [Fact]
    public void Scan_NoMarkersPresent_ReturnsEmpty()
    {
        TemplateAdHeuristics.Scan("Здравствуйте, {ИмяКлиента}! Ждём вас {Дата} в {Время}.", Markers)
            .Should().BeEmpty();
    }

    [Fact]
    public void Scan_OneMarkerPresent_ReturnsIt()
    {
        TemplateAdHeuristics.Scan("Специально для вас скидка 20%!", Markers)
            .Should().Contain("скидк").And.Contain("%");
    }

    [Fact]
    public void Scan_IsCaseInsensitive()
    {
        TemplateAdHeuristics.Scan("СКИДКА для всех", Markers).Should().Contain("скидк");
    }

    [Fact]
    public void Scan_MultipleMarkersPresent_ReturnsAllOfThem()
    {
        var hits = TemplateAdHeuristics.Scan("Акция! Скидка 10%, бесплатная укладка в подарок", Markers);
        hits.Should().BeEquivalentTo(["акци", "скидк", "%", "бесплатн"]);
    }

    [Fact]
    public void Scan_EmptyBody_ReturnsEmpty()
    {
        TemplateAdHeuristics.Scan("", Markers).Should().BeEmpty();
    }

    [Fact]
    public void Scan_EmptyMarkerList_ReturnsEmpty()
    {
        TemplateAdHeuristics.Scan("скидка!", []).Should().BeEmpty();
    }

    [Fact]
    public void Scan_BlankMarkerInList_IsSkippedRatherThanMatchingEverything()
    {
        // A stray empty entry in the superadmin-edited comma-separated list (PlatformSetting) must not
        // turn into "every template is flagged" via body.Contains("").
        TemplateAdHeuristics.Scan("Здравствуйте, {ИмяКлиента}!", ["", "  ", "скидк"]).Should().BeEmpty();
    }
}
