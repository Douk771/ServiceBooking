using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Stays;

public enum StayRefundKind { NothingPaid, Full, Partial }

public enum CancellationBoundary { None, CheckInDayStart, CheckInTime }

/// <summary>One cancellation template (configuration <c>Stays:CancellationPolicies</c>). R37-11: <see cref="Validate"/> refuses an unlawful value at startup.</summary>
public sealed record StayCancellationRule(CancellationBoundary Boundary, int MaxDeductionNights)
{
    public static IReadOnlyDictionary<StayCancellationPolicy, StayCancellationRule> Defaults { get; } =
        new Dictionary<StayCancellationPolicy, StayCancellationRule>
        {
            [StayCancellationPolicy.Standard] = new(CancellationBoundary.CheckInDayStart, 1),
            [StayCancellationPolicy.Flexible] = new(CancellationBoundary.CheckInTime, 1),
            [StayCancellationPolicy.NoDeductions] = new(CancellationBoundary.CheckInTime, 0),
        };

    /// <summary>Errors of a rule set: a deduction of more than one night or a boundary earlier than the check-in day is never allowed (ЮР-1).</summary>
    public static IReadOnlyList<string> Validate(IReadOnlyDictionary<StayCancellationPolicy, StayCancellationRule> rules)
    {
        var errors = new List<string>();
        foreach (var policy in Enum.GetValues<StayCancellationPolicy>())
        {
            if (!rules.TryGetValue(policy, out var r)) { errors.Add($"Stays:CancellationPolicies:{policy} is missing."); continue; }
            if (r.MaxDeductionNights is < 0 or > 1) errors.Add($"Stays:CancellationPolicies:{policy}: maxDeductionNights must be 0 or 1.");
            if (r.Boundary == CancellationBoundary.None) errors.Add($"Stays:CancellationPolicies:{policy}: boundary must not be earlier than the check-in day.");
        }
        return errors;
    }
}

public sealed record StayRefundView(StayRefundKind Kind, int RefundAtLeastRub, int MaxDeductionRub, string Text);

/// <summary>ARCHITECTURE_CYCLE37.md §37.6.4 (ЮР-1) — "refund at least X". Pure; vectors: stay-vectors.json → refund.</summary>
public static class StayRefund
{
    public static StayRefundView Compute(
        StayBookingStatus status, StayCancellationPolicy policy, int prepayRub, int firstNightRub,
        DateOnly checkInDate, TimeOnly checkInTime, string timeZoneId, DateTime atUtc, bool cancelledByOwner,
        IReadOnlyDictionary<StayCancellationPolicy, StayCancellationRule>? rules = null)
    {
        // A booking in a final status cannot be cancelled any more: there is no "refund at least X" to promise (QA CY37: not «К возврату не меньше 0 ₽ …»).
        if (StayStateMachine.IsTerminal(status))
            return new(StayRefundKind.NothingPaid, 0, 0, StaysTexts.RefundNotApplicable);
        if (status == StayBookingStatus.Held || prepayRub <= 0)
            return new(StayRefundKind.NothingPaid, 0, 0, StaysTexts.RefundNothingPaid);
        if (cancelledByOwner) return Full(prepayRub);

        var rule = (rules ?? StayCancellationRule.Defaults)[policy];
        if (rule.MaxDeductionNights == 0) return Full(prepayRub);

        var boundaryUtc = rule.Boundary == CancellationBoundary.CheckInDayStart
            ? StayTime.ToUtc(timeZoneId, checkInDate, TimeOnly.MinValue)
            : StayTime.ToUtc(timeZoneId, checkInDate, checkInTime);
        if (atUtc < boundaryUtc) return Full(prepayRub);

        var deduction = Math.Min(prepayRub, firstNightRub);
        var refund = prepayRub - deduction;
        return new(StayRefundKind.Partial, refund, deduction, StaysTexts.RefundPartial(refund, deduction));
    }

    private static StayRefundView Full(int prepayRub) => new(StayRefundKind.Full, prepayRub, 0, StaysTexts.RefundFull(prepayRub));
}
