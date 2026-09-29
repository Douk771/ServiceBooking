using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// US-20-02 (ARCHITECTURE_CYCLE20.md §403.2, API_CONTRACT_CYCLE20.md §433.1, D3 п. 6.13.15.4) — pure,
/// no EF/HTTP types. Decides whether a manual plan assignment needs a reason, and whether a supplied
/// reason is acceptable. Runs AFTER the existing checks and the cycle-18 trial refusal (that one stays
/// first no matter what reason is supplied — Р6), and BEFORE the row is written.
/// </summary>
public static class ManualPlanAssignmentPolicy
{
    public const int MaxReasonDetailsLength = 1000;

    /// <summary>П3 — a reason is required only when the target plan is both set AND not on the public
    /// storefront AND different from what the account already has. Extending the target plan, switching
    /// between two public plans, or assigning the public plan the account already sits on never requires
    /// one — those aren't "hand a hidden tariff to someone" (О-1, §418: intentionally narrower than D3 п.
    /// 6.13.15.4's literal "any change without a request").</summary>
    public static bool RequiresReason(Guid? currentPlanId, Guid? targetPlanId, bool targetIsPublic) =>
        targetPlanId is not null && !targetIsPublic && targetPlanId != currentPlanId;

    /// <summary>Returns the Russian 400 message to send, or null when the supplied
    /// reason/reasonDetails pair is acceptable. The last three checks run REGARDLESS of
    /// <paramref name="required"/>: a reason volunteered where it wasn't required is still validated,
    /// not accepted blindly (§433.1's own ordering).</summary>
    public static string? Validate(SubscriptionChangeReason? reasonCode, string? reasonDetails, bool required)
    {
        if (required && reasonCode is null)
            return "Для назначения скрытого тарифа укажите основание из списка.";

        // Code-review finding (cycle 20): Program.cs's JsonStringEnumConverter accepts a raw integer for
        // any enum by default (`allowIntegerValues` defaults to true), so an out-of-range value like
        // `"reasonCode": 99` used to bind straight through model binding and reach this method as a
        // technically-non-null-but-undefined enum value — §433.1 requires 400 for an unknown reasonCode,
        // not a row silently written with a meaningless numeric code.
        if (reasonCode is not null && !Enum.IsDefined(reasonCode.Value))
            return $"Неизвестное значение reasonCode '{reasonCode}'.";

        if (reasonCode == SubscriptionChangeReason.TrialReissue)
            return "Перевыдача пробного периода делается только через «Выдать повторно» в блоке «Пробный период».";

        if (reasonCode == SubscriptionChangeReason.OperatorErrorCorrection && string.IsNullOrWhiteSpace(reasonDetails))
            return "Опишите исправляемую ошибку.";

        if (reasonDetails is { Length: > MaxReasonDetailsLength })
            return $"Описание основания не должно превышать {MaxReasonDetailsLength} символов.";

        return null;
    }
}
