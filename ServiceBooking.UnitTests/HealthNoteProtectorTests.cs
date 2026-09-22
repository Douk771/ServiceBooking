using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Notifications;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE5.md §48.1 — no DB, no host; HealthNoteProtector wraps SecretProtector's
/// string-AAD overload directly.</summary>
public class HealthNoteProtectorTests
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static HealthNoteProtector CreateProtector(string? key = null) =>
        new(Options.Create(new NotificationOptions { EncryptionKey = key ?? NewKey() }));

    [Fact]
    public void ProtectThenUnprotect_SameCompanyAndSubject_RoundTrips()
    {
        var protector = CreateProtector();
        var companyId = Guid.NewGuid();

        var ciphertext = protector.Protect("аллергия на аммиак", companyId, "phone:79991234567");
        var plaintext = protector.Unprotect(ciphertext, companyId, "phone:79991234567");

        plaintext.Should().Be("аллергия на аммиак");
    }

    [Fact]
    public void Unprotect_DifferentCompany_ReturnsNull_NotThrows()
    {
        // AAD = "health:{companyId}:{subjectKey}" (§48.1) — a row moved/copied between companies must
        // not decrypt under the new company's id, even with the right key.
        var protector = CreateProtector();
        var ciphertext = protector.Protect("secret", Guid.NewGuid(), "user-1");

        var result = protector.Unprotect(ciphertext, Guid.NewGuid(), "user-1");

        result.Should().BeNull();
    }

    [Fact]
    public void Unprotect_DifferentSubjectKey_ReturnsNull_NotThrows()
    {
        var protector = CreateProtector();
        var companyId = Guid.NewGuid();
        var ciphertext = protector.Protect("secret", companyId, "user-1");

        var result = protector.Unprotect(ciphertext, companyId, "user-2");

        result.Should().BeNull();
    }

    [Fact]
    public void Unprotect_WrongKey_ReturnsNull_NotThrows()
    {
        // §48.1: a decrypt failure must surface as null ("данные недоступны"), never an exception that
        // would turn an arbitrary read into a 500.
        var companyId = Guid.NewGuid();
        var ciphertext = CreateProtector(NewKey()).Protect("secret", companyId, "user-1");

        var result = CreateProtector(NewKey()).Unprotect(ciphertext, companyId, "user-1");

        result.Should().BeNull();
    }

    [Fact]
    public void CurrentKeyId_MatchesTheKeyIdEmbeddedInProtectsOwnCiphertext()
    {
        var key = NewKey();
        var protector = CreateProtector(key);
        var ciphertext = protector.Protect("secret", Guid.NewGuid(), "user-1");

        // Ciphertext format: v1.<keyId>.<payload> (SecretProtector's own doc comment).
        var embeddedKeyId = ciphertext.Split('.', 3)[1];

        protector.CurrentKeyId.Should().Be(embeddedKeyId);
    }
}
