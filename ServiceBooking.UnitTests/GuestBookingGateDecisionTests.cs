using FluentAssertions;
using ServiceBooking.API.Services.PhoneVerification;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE12.md §148.5, §156.1 (US-12-17, Р1/Р3) — the change-phone gate as a pure
/// function of three facts.</summary>
public class GuestBookingGateDecisionTests
{
    [Fact]
    public void PhoneNotChanging_AlwaysAllows_GateNeverParticipates() =>
        GuestBookingGateDecision.Evaluate(phoneIsChanging: false, newNumberHasGuestBookings: true,
            validSessionPresented: false, subsystemEnabled: false).Should().Be(ChangePhoneGateOutcome.Allow);

    [Fact]
    public void NoGuestBookingsOnNewNumber_Allows_NoVerificationRequired_Р1() =>
        GuestBookingGateDecision.Evaluate(phoneIsChanging: true, newNumberHasGuestBookings: false,
            validSessionPresented: false, subsystemEnabled: true).Should().Be(ChangePhoneGateOutcome.Allow);

    [Fact]
    public void GuestBookingsExist_ValidSessionPresented_Allows() =>
        GuestBookingGateDecision.Evaluate(phoneIsChanging: true, newNumberHasGuestBookings: true,
            validSessionPresented: true, subsystemEnabled: true).Should().Be(ChangePhoneGateOutcome.Allow);

    [Fact]
    public void GuestBookingsExist_NoSession_SubsystemEnabled_RequiresVerification() =>
        GuestBookingGateDecision.Evaluate(phoneIsChanging: true, newNumberHasGuestBookings: true,
            validSessionPresented: false, subsystemEnabled: true).Should().Be(ChangePhoneGateOutcome.RequireVerification);

    [Fact]
    public void GuestBookingsExist_NoSession_SubsystemDisabled_HonestRefusal() =>
        GuestBookingGateDecision.Evaluate(phoneIsChanging: true, newNumberHasGuestBookings: true,
            validSessionPresented: false, subsystemEnabled: false).Should().Be(ChangePhoneGateOutcome.SubsystemDisabled);

    [Fact]
    public void ValidSessionPresented_EvenWhileSubsystemDisabled_StillAllows()
    {
        // A session that WAS verified while the subsystem was on, then presented after an operator
        // switched it off, is still a real, already-proven fact — the gate honors it (§148.5's own
        // ordering: session presence is checked before subsystem state).
        GuestBookingGateDecision.Evaluate(phoneIsChanging: true, newNumberHasGuestBookings: true,
            validSessionPresented: true, subsystemEnabled: false).Should().Be(ChangePhoneGateOutcome.Allow);
    }
}
