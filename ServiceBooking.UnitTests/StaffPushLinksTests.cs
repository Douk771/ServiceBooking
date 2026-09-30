using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>API_CONTRACT_CYCLE33.md §33.27 — url by subscription site, body length, salon name.</summary>
public class StaffPushLinksTests
{
    private static StaffPushLinks Links() => new(new PublicSiteLinks(Options.Create(new PublicSitesOptions
    {
        ServicesBaseUrl = "https://ezbook.test/", OrdersBaseUrl = "https://goods.ezbook.test"
    })));

    [Fact]
    public void ResolveUrl_SameSite_StaysRelative() =>
        Links().ResolveUrl(CompanyKind.Services, CompanyKind.Services, "/my-bookings?booking=1").Should().Be("/my-bookings?booking=1");

    [Fact]
    public void ResolveUrl_ServicesEventOnOrdersSubscription_IsAbsoluteOnServicesSite() =>
        Links().ResolveUrl(CompanyKind.Orders, CompanyKind.Services, "/my-bookings?booking=1").Should().Be("https://ezbook.test/my-bookings?booking=1");

    [Fact]
    public void ResolveUrl_OrdersEventOnServicesSubscription_IsAbsoluteOnOrdersSite() =>
        Links().ResolveUrl(CompanyKind.Services, CompanyKind.Orders, "/cabinet/subscription").Should().Be("https://goods.ezbook.test/cabinet/subscription");

    [Fact]
    public void Build_WritesCyrillicAsIs()
    {
        var json = StaffPushPayloadJson.Build("Новая запись", "тело", "t", "/u");
        json.Should().Contain("Новая запись").And.NotContain("\\u");
    }

    [Fact]
    public void Build_LongBody_IsShortenedWithEllipsisWithinLimit()
    {
        var json = StaffPushPayloadJson.Build("T", new string('я', 3000), "t", "/u");
        json.Length.Should().BeLessThanOrEqualTo(StaffPushPayloadJson.MaxLength);
        JsonDocument.Parse(json).RootElement.GetProperty("body").GetString().Should().EndWith("…");
    }

    [Fact]
    public void BuildPayload_LongSalonName_IsCutTo60WithEllipsis()
    {
        var payload = StaffPushScheduler.BuildPayload(["A"], new DateOnly(2026, 1, 1), new TimeOnly(9, 0), "К", new string('с', 100), Guid.NewGuid(), "/u");
        var body = JsonDocument.Parse(payload).RootElement.GetProperty("body").GetString()!;
        body.Should().EndWith(new string('с', 59) + "…");
    }
}
