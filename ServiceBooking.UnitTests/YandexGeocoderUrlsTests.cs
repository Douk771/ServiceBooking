using FluentAssertions;
using ServiceBooking.API.Services.Geo.Yandex;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE13.md §206/§214/R15 — the SafeLabel returned alongside every request URL
/// must never contain the API key, the same "rung 2" test shape as GreenApiUrlsTests.</summary>
public class YandexGeocoderUrlsTests
{
    private const string ApiUrl = "https://geocode-maps.yandex.ru/1.x/";
    private const string SecretApiKey = "super-secret-yandex-api-key";

    [Fact]
    public void Lookup_SafeLabel_NeverContainsApiKey()
    {
        var (_, safeLabel) = YandexGeocoderUrls.Lookup(ApiUrl, SecretApiKey, "Ленина 5", 5);
        safeLabel.Should().NotContain(SecretApiKey);
    }

    [Fact]
    public void Lookup_RequestUri_DoesContainApiKey_ItHasTo()
    {
        // The real request URI MUST carry the key (that's how the API call authenticates) — only
        // SafeLabel is redacted. This test pins that expectation so a future "redact both" mistake fails
        // loudly instead of silently breaking every real call.
        var (request, _) = YandexGeocoderUrls.Lookup(ApiUrl, SecretApiKey, "Ленина 5", 5);
        request.Query.Should().Contain(SecretApiKey);
    }

    [Fact]
    public void Lookup_SafeLabel_MarksRedactionWithAPlaceholder()
    {
        var (_, safeLabel) = YandexGeocoderUrls.Lookup(ApiUrl, SecretApiKey, "Ленина 5", 5);
        safeLabel.Should().Contain("apikey=***");
    }

    [Fact]
    public void Lookup_RequestUri_ContainsGeocodeAndResultsParams()
    {
        var (request, _) = YandexGeocoderUrls.Lookup(ApiUrl, SecretApiKey, "Ленина 5", 5);
        request.Query.Should().Contain("results=5");
        request.Query.Should().Contain("format=json");
    }

    [Fact]
    public void SafeLabel_PreservesNonSecretQueryParameters()
    {
        var (_, safeLabel) = YandexGeocoderUrls.Lookup(ApiUrl, SecretApiKey, "Ленина 5", 5);
        safeLabel.Should().Contain("results=5");
        safeLabel.Should().Contain("format=json");
    }
}
