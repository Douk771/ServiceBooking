using FluentAssertions;
using ServiceBooking.API.Services.Billing;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE5.md §51.1/§51.2 — the pure, DB-free rules
/// <see cref="CompanyTransferService"/> is built on: new-owner linkage and the seat-overflow formula
/// with its +1 correction for a new owner who isn't already a member of the transferred company
/// (risk A8).</summary>
public class CompanyTransferCalculatorTests
{
    // ── IsNewOwnerLinkedToTargetAccount (§51.1 p.3) ────────────────────────────

    [Fact]
    public void IsNewOwnerLinkedToTargetAccount_Holder_IsLinked()
    {
        CompanyTransferCalculator.IsNewOwnerLinkedToTargetAccount(isTargetAccountHolder: true, isMemberOfTargetAccountCompany: false)
            .Should().BeTrue();
    }

    [Fact]
    public void IsNewOwnerLinkedToTargetAccount_MemberOfAnotherCompanyOnAccount_IsLinked()
    {
        CompanyTransferCalculator.IsNewOwnerLinkedToTargetAccount(isTargetAccountHolder: false, isMemberOfTargetAccountCompany: true)
            .Should().BeTrue();
    }

    [Fact]
    public void IsNewOwnerLinkedToTargetAccount_NeitherHolderNorMember_IsNotLinked()
    {
        CompanyTransferCalculator.IsNewOwnerLinkedToTargetAccount(isTargetAccountHolder: false, isMemberOfTargetAccountCompany: false)
            .Should().BeFalse();
    }

    [Fact]
    public void IsNewOwnerLinkedToTargetAccount_BothTrue_IsLinked()
    {
        CompanyTransferCalculator.IsNewOwnerLinkedToTargetAccount(isTargetAccountHolder: true, isMemberOfTargetAccountCompany: true)
            .Should().BeTrue();
    }

    // ── ComputeSeatsAfter (§51.2, risk A8) ──────────────────────────────────────

    [Fact]
    public void ComputeSeatsAfter_NoNewOwner_JustAddsCompanySeats()
    {
        CompanyTransferCalculator.ComputeSeatsAfter(seatsUsedOnTargetAccount: 5, seatsOfTransferredCompany: 3, newOwnerAddsSeat: false)
            .Should().Be(8);
    }

    [Fact]
    public void ComputeSeatsAfter_NewOwnerNotYetMember_AddsOneExtraSeat()
    {
        CompanyTransferCalculator.ComputeSeatsAfter(seatsUsedOnTargetAccount: 5, seatsOfTransferredCompany: 3, newOwnerAddsSeat: true)
            .Should().Be(9);
    }

    [Fact]
    public void ComputeSeatsAfter_NewOwnerAlreadyMember_DoesNotDoubleCount()
    {
        // Caller is responsible for passing newOwnerAddsSeat=false when the new owner is already a
        // CompanyMember of the transferred company — this is exactly that case, verified via the same
        // seat count as the "no new owner" case above.
        CompanyTransferCalculator.ComputeSeatsAfter(seatsUsedOnTargetAccount: 5, seatsOfTransferredCompany: 3, newOwnerAddsSeat: false)
            .Should().Be(8);
    }

    [Fact]
    public void ComputeSeatsAfter_ZeroUsageAndZeroCompanySeats_WithNewOwner_IsOne()
    {
        CompanyTransferCalculator.ComputeSeatsAfter(seatsUsedOnTargetAccount: 0, seatsOfTransferredCompany: 0, newOwnerAddsSeat: true)
            .Should().Be(1);
    }

    // ── IsSeatOverflow ───────────────────────────────────────────────────────────

    [Fact]
    public void IsSeatOverflow_NullLimit_NeverOverflows()
    {
        CompanyTransferCalculator.IsSeatOverflow(seatsAfter: 1_000_000, accountMaxEmployees: null).Should().BeFalse();
    }

    [Fact]
    public void IsSeatOverflow_ExactlyAtLimit_DoesNotOverflow()
    {
        CompanyTransferCalculator.IsSeatOverflow(seatsAfter: 10, accountMaxEmployees: 10).Should().BeFalse();
    }

    [Fact]
    public void IsSeatOverflow_OneOverLimit_Overflows()
    {
        CompanyTransferCalculator.IsSeatOverflow(seatsAfter: 11, accountMaxEmployees: 10).Should().BeTrue();
    }

    [Fact]
    public void IsSeatOverflow_TheACorrectionTipsItOver()
    {
        // Without the +1 correction (A8) this would read 10/10 = fine; with it, 11/10 = overflow.
        var seatsAfter = CompanyTransferCalculator.ComputeSeatsAfter(seatsUsedOnTargetAccount: 9, seatsOfTransferredCompany: 1, newOwnerAddsSeat: true);
        CompanyTransferCalculator.IsSeatOverflow(seatsAfter, accountMaxEmployees: 10).Should().BeTrue();
    }

    // ── IsCompanyLimitExceeded ───────────────────────────────────────────────────

    [Fact]
    public void IsCompanyLimitExceeded_NullLimit_NeverExceeded()
    {
        CompanyTransferCalculator.IsCompanyLimitExceeded(companiesUsedOnTargetAccount: 1_000_000, accountMaxCompanies: null).Should().BeFalse();
    }

    [Fact]
    public void IsCompanyLimitExceeded_UsedEqualsLimit_IsExceeded()
    {
        // §51.2: "usage >= limit" rejects — there's no room left for one more company.
        CompanyTransferCalculator.IsCompanyLimitExceeded(companiesUsedOnTargetAccount: 2, accountMaxCompanies: 2).Should().BeTrue();
    }

    [Fact]
    public void IsCompanyLimitExceeded_UsedBelowLimit_IsNotExceeded()
    {
        CompanyTransferCalculator.IsCompanyLimitExceeded(companiesUsedOnTargetAccount: 1, accountMaxCompanies: 2).Should().BeFalse();
    }
}
