using FluentAssertions;
using ServiceBooking.API.Services.Geo;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE13.md §203/§214 — pure, no DB. Every branch of the "is this address
/// verified" rule, including the two edge cases the architecture calls out by name: an address edited
/// after verification (US-134 p.2), and a company created before this cycle with empty columns (US-137,
/// no backfill).</summary>
public class AddressVerificationStateTests
{
    private static Company Company(string? address, string? verifiedInputKey, DateTime? verifiedAt,
        AddressPrecision? precision = AddressPrecision.House) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test",
        Slug = "test",
        OwnerUserId = "owner",
        Address = address,
        AddressVerifiedInputKey = verifiedInputKey,
        AddressVerifiedAt = verifiedAt,
        AddressPrecision = precision,
    };

    [Fact]
    public void Status_MatchingKeyAndTimestamp_IsVerified()
    {
        var company = Company("Ленина 5", AddressNormalization.Key("Ленина 5"), DateTime.UtcNow);
        AddressVerificationState.Status(company).Should().Be(AddressVerificationStatus.Verified);
    }

    [Fact]
    public void Status_AddressEditedAfterVerification_FallsBackToUnverified()
    {
        // US-134 p.2: the owner verified "Ленина 5", then changed the text to "Ленина 6" through any
        // write path — the key no longer matches, no explicit "reset" step exists or is needed.
        var company = Company("Ленина 6", AddressNormalization.Key("Ленина 5"), DateTime.UtcNow);
        AddressVerificationState.Status(company).Should().Be(AddressVerificationStatus.Unverified);
    }

    [Fact]
    public void Status_AddressEmpty_IsUnverified_EvenWithStaleVerificationColumns()
    {
        var company = Company(null, AddressNormalization.Key("Ленина 5"), DateTime.UtcNow);
        AddressVerificationState.Status(company).Should().Be(AddressVerificationStatus.Unverified);
    }

    [Fact]
    public void Status_NeverVerified_IsUnverified()
    {
        var company = Company("Ленина 5", null, null);
        AddressVerificationState.Status(company).Should().Be(AddressVerificationStatus.Unverified);
    }

    [Fact]
    public void Status_PreCycleCompany_EmptyVerificationColumns_IsUnverified()
    {
        // US-137: a company that existed before this cycle simply has null verification columns — no
        // backfill migration, this must not crash or accidentally read as Verified.
        var company = Company("Ленина 5", null, null, precision: null);
        AddressVerificationState.Status(company).Should().Be(AddressVerificationStatus.Unverified);
    }

    [Fact]
    public void Status_VerifiedAtSetButKeyMissing_IsUnverified()
    {
        var company = Company("Ленина 5", null, DateTime.UtcNow);
        AddressVerificationState.Status(company).Should().Be(AddressVerificationStatus.Unverified);
    }

    [Fact]
    public void Status_KeySetButVerifiedAtMissing_IsUnverified()
    {
        var company = Company("Ленина 5", AddressNormalization.Key("Ленина 5"), null);
        AddressVerificationState.Status(company).Should().Be(AddressVerificationStatus.Unverified);
    }

    [Fact]
    public void Status_CaseAndSpacingDifferences_StillReadAsVerified()
    {
        var company = Company("  ленина   5 ", AddressNormalization.Key("Ленина 5."), DateTime.UtcNow);
        AddressVerificationState.Status(company).Should().Be(AddressVerificationStatus.Verified);
    }
}
