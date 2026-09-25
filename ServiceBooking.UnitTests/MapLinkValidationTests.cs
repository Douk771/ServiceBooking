using FluentAssertions;
using ServiceBooking.API.Services.Companies;

namespace ServiceBooking.UnitTests;

public class MapLinkValidationTests
{
    // ARCHITECTURE_CYCLE15.md §262/API_CONTRACT_CYCLE15.md §284 — the four real links from 0-bis П2,
    // obligatory test cases: accepted byte-for-byte, including percent-escapes untouched.
    [Theory]
    [InlineData("https://2gis.ru/barnaul/firm/70000001094251007")]
    [InlineData("https://2gis.ru/barnaul/firm/563478234628539/83.795014%2C53.330486?m=83.795954%2C53.330025%2F17.89")]
    public void TryNormalize_RealTwoGisLinks_AcceptedByteForByte(string url)
    {
        MapLinkValidation.TryNormalize(url, MapLinkService.TwoGis, out var value, out var error).Should().BeTrue();
        value.Should().Be(url);
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("https://yandex.ru/maps/org/syrovarnya/11766054863/?ll=83.795110%2C53.330510&z=17")]
    [InlineData("https://yandex.ru/maps/197/barnaul/?ll=83.795035%2C53.330278&mode=whatshere&whatshere%5Bpoint%5D=83.795035%2C53.330278&whatshere%5Bzoom%5D=17&z=16")]
    public void TryNormalize_RealYandexLinks_AcceptedByteForByte(string url)
    {
        MapLinkValidation.TryNormalize(url, MapLinkService.Yandex, out var value, out var error).Should().BeTrue();
        value.Should().Be(url);
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryNormalize_EmptyOrWhitespace_ClearsField(string raw)
    {
        MapLinkValidation.TryNormalize(raw, MapLinkService.Yandex, out var value, out var error).Should().BeTrue();
        value.Should().BeNull();
        error.Should().BeNull();
    }

    [Fact]
    public void TryNormalize_HttpScheme_Rejected()
    {
        MapLinkValidation.TryNormalize("http://2gis.ru/barnaul", MapLinkService.TwoGis, out _, out var error).Should().BeFalse();
        error.Should().Be("Ссылка должна начинаться с https://");
    }

    [Fact]
    public void TryNormalize_JavascriptScheme_Rejected() =>
        MapLinkValidation.TryNormalize("javascript:alert(1)", MapLinkService.Yandex, out _, out _).Should().BeFalse();

    [Fact]
    public void TryNormalize_DataScheme_Rejected() =>
        MapLinkValidation.TryNormalize("data:text/html,hi", MapLinkService.Yandex, out _, out _).Should().BeFalse();

    [Fact]
    public void TryNormalize_NoScheme_Rejected() =>
        MapLinkValidation.TryNormalize("2gis.ru/barnaul", MapLinkService.TwoGis, out _, out _).Should().BeFalse();

    // R10 — the wildcard is read narrowly ("host == yandex.<tld> or ends with .yandex.<tld>"), NEVER as
    // "contains yandex" or "*.yandex.*" in the naive sense, or this becomes an open redirect.
    [Fact]
    public void TryNormalize_YandexEvilSubdomain_Rejected() =>
        MapLinkValidation.TryNormalize("https://yandex.evil.com/maps", MapLinkService.Yandex, out _, out var error)
            .Should().BeFalse();

    [Fact]
    public void TryNormalize_TwoGisLookalikeDomain_Rejected() =>
        MapLinkValidation.TryNormalize("https://2gis.ru.evil.com/x", MapLinkService.TwoGis, out _, out _).Should().BeFalse();

    [Fact]
    public void TryNormalize_ShortenerNotAccepted() =>
        MapLinkValidation.TryNormalize("https://clck.ru/abc", MapLinkService.Yandex, out _, out _).Should().BeFalse();

    [Fact]
    public void TryNormalize_UserInfoInUrl_Rejected() =>
        MapLinkValidation.TryNormalize("https://user:pass@2gis.ru/x", MapLinkService.TwoGis, out _, out _).Should().BeFalse();

    [Fact]
    public void TryNormalize_NonDefaultPort_Rejected() =>
        MapLinkValidation.TryNormalize("https://2gis.ru:8443/x", MapLinkService.TwoGis, out _, out _).Should().BeFalse();

    [Fact]
    public void TryNormalize_TooLong_Rejected()
    {
        var url = "https://2gis.ru/" + new string('a', 501);
        MapLinkValidation.TryNormalize(url, MapLinkService.TwoGis, out _, out var error).Should().BeFalse();
        error.Should().Be("Ссылка слишком длинная — не больше 500 символов");
    }

    [Fact]
    public void TryNormalize_EmbeddedNewline_Rejected() =>
        MapLinkValidation.TryNormalize("https://2gis.ru/\nbarnaul", MapLinkService.TwoGis, out _, out _).Should().BeFalse();

    [Fact]
    public void TryNormalize_TextWithoutScheme_Rejected() =>
        MapLinkValidation.TryNormalize("not a url at all", MapLinkService.Yandex, out _, out _).Should().BeFalse();

    [Theory]
    [InlineData("https://yandex.com/maps/x")]
    [InlineData("https://yandex.kz/maps/x")]
    [InlineData("https://sub.yandex.ru/maps/x")]
    public void TryNormalize_OtherKnownYandexTlds_Accepted(string url) =>
        MapLinkValidation.TryNormalize(url, MapLinkService.Yandex, out _, out _).Should().BeTrue();

    [Fact]
    public void TryNormalize_TwoGisSubdomain_Accepted() =>
        MapLinkValidation.TryNormalize("https://widget.2gis.ru/x", MapLinkService.TwoGis, out _, out _).Should().BeTrue();
}
