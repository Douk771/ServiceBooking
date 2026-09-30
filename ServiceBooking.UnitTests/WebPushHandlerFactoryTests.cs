using FluentAssertions;
using ServiceBooking.API.Services.Notifications.WebPush;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE25.md §507.2 (T-25-02) — web-push never goes through the system proxy and never follows redirects.</summary>
public class WebPushHandlerFactoryTests
{
    [Fact]
    public void Create_DisablesProxyAndRedirects_AndGuardsTheConnection()
    {
        using var handler = WebPushHandlerFactory.Create();
        handler.UseProxy.Should().BeFalse();
        handler.AllowAutoRedirect.Should().BeFalse();
        handler.ConnectCallback.Should().NotBeNull();
    }
}
