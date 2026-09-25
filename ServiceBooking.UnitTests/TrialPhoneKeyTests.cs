using System.Linq;
using FluentAssertions;
using ServiceBooking.API.Services.Billing;

namespace ServiceBooking.UnitTests;

public class TrialPhoneKeyTests
{
    private static readonly string KeyA = Convert.ToBase64String(new byte[32].Select((_, i) => (byte)i).ToArray());
    private static readonly string KeyB = Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i + 1)).ToArray());

    [Fact]
    public void Compute_SameInputs_IsDeterministic()
    {
        TrialPhoneKey.Compute(KeyA, "79991234567").Should().Be(TrialPhoneKey.Compute(KeyA, "79991234567"));
    }

    [Fact]
    public void Compute_DifferentKeys_ProduceDifferentValues()
    {
        TrialPhoneKey.Compute(KeyA, "79991234567").Should().NotBe(TrialPhoneKey.Compute(KeyB, "79991234567"));
    }

    [Fact]
    public void Compute_DifferentPhones_ProduceDifferentValues()
    {
        TrialPhoneKey.Compute(KeyA, "79991234567").Should().NotBe(TrialPhoneKey.Compute(KeyA, "79997654321"));
    }

    [Fact]
    public void Compute_ReturnsLowercaseHex()
    {
        var value = TrialPhoneKey.Compute(KeyA, "79991234567");
        value.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64!!")]
    public void IsKeyUsable_MissingOrMalformed_FailsClosed(string? key)
    {
        TrialPhoneKey.IsKeyUsable(key).Should().BeFalse();
    }

    [Fact]
    public void IsKeyUsable_TooShort_FailsClosed()
    {
        TrialPhoneKey.IsKeyUsable(Convert.ToBase64String(new byte[16])).Should().BeFalse();
    }

    [Fact]
    public void IsKeyUsable_ValidLongEnoughKey_IsUsable()
    {
        TrialPhoneKey.IsKeyUsable(KeyA).Should().BeTrue();
    }

    [Fact]
    public void KeysCollide_SameKey_ReturnsTrue()
    {
        // К2 mechanical check: the trial key must never equal PhoneVerification's or Notifications'.
        TrialPhoneKey.KeysCollide(KeyA, KeyA).Should().BeTrue();
    }

    [Fact]
    public void KeysCollide_DifferentKeys_ReturnsFalse()
    {
        TrialPhoneKey.KeysCollide(KeyA, KeyB).Should().BeFalse();
    }
}
