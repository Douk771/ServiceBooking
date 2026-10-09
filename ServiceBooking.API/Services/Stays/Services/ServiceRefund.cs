using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Stays;

public sealed record ServiceRefundResult(ServiceRefundKind Kind, int? RefundAtLeastRub, int MaxDeductionRub, string Text);

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.8, ЮР39-1 (Т39-01…03) — «refund at least X» of a stand-alone session. Two templates only: «Без удержаний» and «Расходы на подготовку»
/// (later than the boundary the company may keep ONLY actual costs, not more than the first hour). A «Standard» template does not exist in any form. The text
/// «к возврату не меньше 0 ₽» is never produced: with a zero rest the result is <see cref="ServiceRefundKind.CostsOnlyUpTo"/> and <c>RefundAtLeastRub = null</c>.
/// Pure; vectors: service-vectors.json → refund.
/// </summary>
public static class ServiceRefund
{
    public static ServiceRefundResult Compute(
        StayBookingStatus status, StayServiceCancellationPolicy policy, int boundaryHours, int prepayRub, int firstHourRub,
        DateTime startUtc, DateTime atUtc, bool cancelledByOwner, int maxDeductionHours = 1, string? terminalText = null)
    {
        if (StayStateMachine.IsTerminal(status))
            return new(ServiceRefundKind.NothingPaid, 0, 0, terminalText ?? ServiceTexts.RefundTerminal);
        if (status == StayBookingStatus.Held || prepayRub <= 0)
            return new(ServiceRefundKind.NothingPaid, 0, 0, ServiceTexts.RefundNothingPaid);
        if (cancelledByOwner) return Full(prepayRub, byOwner: true);
        if (policy == StayServiceCancellationPolicy.NoDeductions || maxDeductionHours <= 0) return Full(prepayRub, byOwner: false);

        if (atUtc < startUtc.AddHours(-boundaryHours)) return Full(prepayRub, byOwner: false);
        var deduction = Math.Min(prepayRub, firstHourRub * maxDeductionHours);
        var rest = prepayRub - deduction;
        return rest > 0
            ? new(ServiceRefundKind.PartialAtLeast, rest, deduction, ServiceTexts.RefundPartialAtLeast(rest, deduction))
            : new(ServiceRefundKind.CostsOnlyUpTo, null, deduction, ServiceTexts.RefundCostsOnly(deduction));
    }

    private static ServiceRefundResult Full(int prepayRub, bool byOwner) =>
        new(ServiceRefundKind.Full, prepayRub, 0, byOwner ? ServiceTexts.RefundFullByOwner(prepayRub) : ServiceTexts.RefundFullByRule(prepayRub));

    /// <summary>Errors of the configuration (vectors: refund.config): MaxDeductionHours ∈ {0, 1}; 1 ≤ Min ≤ Default ≤ Max ≤ 24; only known template names.</summary>
    public static IReadOnlyList<string> ValidateConfiguration(int maxDeductionHours, int boundaryMin, int boundaryMax, int boundaryDefault, IEnumerable<string>? policyNames = null)
    {
        var errors = new List<string>();
        if (maxDeductionHours is not (0 or 1)) errors.Add("Stays:Services:CancellationPolicies:PreparationCosts:MaxDeductionHours must be 0 or 1.");
        if (boundaryMin < 1 || boundaryMax > 24 || boundaryMin > boundaryDefault || boundaryDefault > boundaryMax)
            errors.Add("Stays:Services:CancellationBoundaryHours must satisfy 1 <= Min <= Default <= Max <= 24.");
        var known = Enum.GetNames<StayServiceCancellationPolicy>();
        foreach (var name in policyNames ?? [])
            if (!known.Contains(name, StringComparer.OrdinalIgnoreCase))
                errors.Add($"Stays:Services:CancellationPolicies:{name} is not a known template.");
        return errors;
    }
}
