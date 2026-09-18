using FluentAssertions;
using ServiceBooking.API.Services.Notifications.GreenApi;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE4.md §24.3 rung 2 / §28: the SafeLabel returned alongside every request URL must
/// never contain the token, and chatId construction is its own pure, tested function (US-27 p.1).
/// </summary>
public class GreenApiUrlsTests
{
    private const string ApiUrl = "https://api.green-api.com";
    private const string InstanceId = "1234567890";
    private const string SecretToken = "super-secret-token-value";
    private const string PartnerToken = "super-secret-partner-token";

    [Fact]
    public void BuildChatId_AppendsWhatsAppSuffix()
    {
        GreenApiUrls.BuildChatId("79991234567").Should().Be("79991234567@c.us");
    }

    [Theory]
    [InlineData("SendMessage")]
    [InlineData("GetStateInstance")]
    [InlineData("GetQr")]
    [InlineData("Logout")]
    [InlineData("SetSettings")]
    public void InstanceUrls_SafeLabel_NeverContainsToken(string methodName)
    {
        var (uri, safeLabel) = methodName switch
        {
            "SendMessage" => GreenApiUrls.SendMessage(ApiUrl, InstanceId, SecretToken),
            "GetStateInstance" => GreenApiUrls.GetStateInstance(ApiUrl, InstanceId, SecretToken),
            "GetQr" => GreenApiUrls.GetQr(ApiUrl, InstanceId, SecretToken),
            "Logout" => GreenApiUrls.Logout(ApiUrl, InstanceId, SecretToken),
            "SetSettings" => GreenApiUrls.SetSettings(ApiUrl, InstanceId, SecretToken),
            _ => throw new InvalidOperationException(),
        };

        safeLabel.Should().NotContain(SecretToken);
        uri.ToString().Should().Contain(SecretToken); // the real URL DOES need the token to actually work
        safeLabel.Should().Contain(InstanceId);
    }

    [Fact]
    public void CreateInstance_SafeLabel_NeverContainsPartnerToken()
    {
        var (uri, safeLabel) = GreenApiUrls.CreateInstance(ApiUrl, PartnerToken);
        safeLabel.Should().NotContain(PartnerToken);
        uri.ToString().Should().Contain(PartnerToken);
    }

    [Fact]
    public void DeleteInstance_SafeLabel_NeverContainsPartnerToken_ButContainsInstanceId()
    {
        var (uri, safeLabel) = GreenApiUrls.DeleteInstance(ApiUrl, PartnerToken, InstanceId);
        safeLabel.Should().NotContain(PartnerToken);
        safeLabel.Should().Contain(InstanceId);
        uri.ToString().Should().Contain(PartnerToken);
        uri.ToString().Should().Contain(InstanceId);
    }

    [Fact]
    public void SendMessage_Url_UsesWaInstancePathShape()
    {
        var (uri, _) = GreenApiUrls.SendMessage(ApiUrl, InstanceId, SecretToken);
        uri.ToString().Should().Be($"{ApiUrl}/waInstance{InstanceId}/sendMessage/{SecretToken}");
    }

    [Fact]
    public void ApiUrlWithTrailingSlash_DoesNotProduceDoubleSlash()
    {
        var (uri, _) = GreenApiUrls.SendMessage($"{ApiUrl}/", InstanceId, SecretToken);
        uri.ToString().Should().Be($"{ApiUrl}/waInstance{InstanceId}/sendMessage/{SecretToken}");
    }
}
