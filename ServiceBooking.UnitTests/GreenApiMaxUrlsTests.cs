using FluentAssertions;
using ServiceBooking.API.Services.Notifications.GreenApiMax;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE9.md §104.9 (B1) — mirrors GreenApiUrlsTests. B1's researched confirmation: MAX
/// reuses the exact same waInstance/{method}/{token} URL shape as WhatsApp, and phoneNumber@c.us is still
/// accepted as a chat id for backward compatibility.
/// </summary>
public class GreenApiMaxUrlsTests
{
    private const string ApiUrl = "https://api.green-api.com";
    private const string InstanceId = "3100000000";
    private const string SecretToken = "super-secret-max-token-value";
    private const string PartnerToken = "super-secret-max-partner-token";

    [Fact]
    public void BuildChatId_AppendsPhoneSuffix()
    {
        GreenApiMaxUrls.BuildChatId("79991234567").Should().Be("79991234567@c.us");
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
            "SendMessage" => GreenApiMaxUrls.SendMessage(ApiUrl, InstanceId, SecretToken),
            "GetStateInstance" => GreenApiMaxUrls.GetStateInstance(ApiUrl, InstanceId, SecretToken),
            "GetQr" => GreenApiMaxUrls.GetQr(ApiUrl, InstanceId, SecretToken),
            "Logout" => GreenApiMaxUrls.Logout(ApiUrl, InstanceId, SecretToken),
            "SetSettings" => GreenApiMaxUrls.SetSettings(ApiUrl, InstanceId, SecretToken),
            _ => throw new InvalidOperationException(),
        };

        safeLabel.Should().NotContain(SecretToken);
        uri.ToString().Should().Contain(SecretToken);
        safeLabel.Should().Contain(InstanceId);
    }

    [Fact]
    public void CreateInstance_SafeLabel_NeverContainsPartnerToken()
    {
        var (uri, safeLabel) = GreenApiMaxUrls.CreateInstance(ApiUrl, PartnerToken);
        safeLabel.Should().NotContain(PartnerToken);
        uri.ToString().Should().Contain(PartnerToken);
    }

    [Fact]
    public void DeleteInstance_SafeLabel_NeverContainsPartnerToken_ButContainsInstanceId()
    {
        var (uri, safeLabel) = GreenApiMaxUrls.DeleteInstance(ApiUrl, PartnerToken, InstanceId);
        safeLabel.Should().NotContain(PartnerToken);
        safeLabel.Should().Contain(InstanceId);
        uri.ToString().Should().Contain(PartnerToken);
    }

    [Fact]
    public void DeleteInstance_Url_UsesDeleteInstanceAccountMethod_AndDoesNotPutInstanceIdInThePath()
    {
        var (uri, _) = GreenApiMaxUrls.DeleteInstance(ApiUrl, PartnerToken, InstanceId);
        uri.ToString().Should().Be($"{ApiUrl}/partner/deleteInstanceAccount/{PartnerToken}");
    }

    [Fact]
    public void SendMessage_Url_UsesWaInstancePathShape()
    {
        var (uri, _) = GreenApiMaxUrls.SendMessage(ApiUrl, InstanceId, SecretToken);
        uri.ToString().Should().Be($"{ApiUrl}/waInstance{InstanceId}/sendMessage/{SecretToken}");
    }

    [Fact]
    public void ApiUrlWithTrailingSlash_DoesNotProduceDoubleSlash()
    {
        var (uri, _) = GreenApiMaxUrls.SendMessage($"{ApiUrl}/", InstanceId, SecretToken);
        uri.ToString().Should().Be($"{ApiUrl}/waInstance{InstanceId}/sendMessage/{SecretToken}");
    }
}
