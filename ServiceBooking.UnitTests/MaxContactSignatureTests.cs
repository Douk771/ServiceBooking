using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using ServiceBooking.API.Services.PhoneVerification.Max;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE12.md §147.1, §156.1 — matching/mismatched signature, empty hash, empty
/// vcf_info, and both accepted encodings (hex/base64). Constant-time comparison itself isn't
/// black-box-testable from a unit test, but the behavior it implements (match/no-match) is.</summary>
public class MaxContactSignatureTests
{
    private const string BotToken = "test-bot-token-value";
    private const string VcfInfo = "BEGIN:VCARD\nVERSION:3.0\nTEL:+79990000000\nEND:VCARD";

    private static string ComputeHex(string vcfInfo, string botToken) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(botToken), Encoding.UTF8.GetBytes(vcfInfo)));

    [Fact]
    public void Verify_MatchingHexSignature_ReturnsTrue() =>
        MaxContactSignature.Verify(VcfInfo, ComputeHex(VcfInfo, BotToken), BotToken).Should().BeTrue();

    [Fact]
    public void Verify_MatchingBase64Signature_ReturnsTrue()
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(BotToken), Encoding.UTF8.GetBytes(VcfInfo));
        MaxContactSignature.Verify(VcfInfo, Convert.ToBase64String(hash), BotToken).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongToken_ReturnsFalse() =>
        MaxContactSignature.Verify(VcfInfo, ComputeHex(VcfInfo, BotToken), "a-different-bot-token").Should().BeFalse();

    [Fact]
    public void Verify_TamperedVcfInfo_ReturnsFalse() =>
        MaxContactSignature.Verify(VcfInfo + "TAMPERED", ComputeHex(VcfInfo, BotToken), BotToken).Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Verify_EmptyOrMissingHash_ReturnsFalse_NotTreatedAsNothingToCheck(string? hash) =>
        MaxContactSignature.Verify(VcfInfo, hash, BotToken).Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Verify_EmptyOrMissingVcfInfo_ReturnsFalse(string? vcfInfo) =>
        MaxContactSignature.Verify(vcfInfo, ComputeHex(VcfInfo, BotToken), BotToken).Should().BeFalse();

    [Fact]
    public void Verify_GarbageHash_ReturnsFalseWithoutThrowing() =>
        MaxContactSignature.Verify(VcfInfo, "not-a-valid-hash-!!", BotToken).Should().BeFalse();
}
