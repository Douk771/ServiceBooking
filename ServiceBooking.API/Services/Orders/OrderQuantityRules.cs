using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Orders;

public enum QuantityCheck
{
    Ok,
    BelowMinimum,
    InvalidQuantity
}

/// <summary>
/// ARCHITECTURE_CYCLE23.md §395.2 step 8, §396.4 — quantity rules per unit, in whole pieces / whole grams.
/// Pieces: 1..99. Weight: a multiple of the product's step, not below its minimum, at most 10 000 g.
/// The actual weight at issue is a different rule (any 1..100 000 g — a scale shows any value).
/// </summary>
public static class OrderQuantityRules
{
    public const int MaxPieces = 99;
    public const int MaxGrams = 10_000;
    public const int MinStepGrams = 10;
    public const int MaxStepGrams = 5_000;
    public const int DefaultStepGrams = 100;
    public const int MaxActualGrams = 100_000;

    public static int EffectiveStep(int? stepGrams) => stepGrams is > 0 ? stepGrams.Value : DefaultStepGrams;

    public static int MinQuantity(ProductUnit unit, int? stepGrams, int? minQuantityGrams) =>
        unit == ProductUnit.Piece ? 1 : minQuantityGrams is > 0 ? minQuantityGrams.Value : EffectiveStep(stepGrams);

    public static int MaxQuantity(ProductUnit unit) => unit == ProductUnit.Piece ? MaxPieces : MaxGrams;

    /// <summary>Validates a customer's quantity (create/quote). Above the maximum or off the step → InvalidQuantity.</summary>
    public static QuantityCheck Check(ProductUnit unit, int quantity, int? stepGrams, int? minQuantityGrams)
    {
        if (quantity < 1 || quantity > MaxQuantity(unit)) return QuantityCheck.InvalidQuantity;
        if (unit == ProductUnit.Weight && quantity % EffectiveStep(stepGrams) != 0) return QuantityCheck.InvalidQuantity;
        return quantity < MinQuantity(unit, stepGrams, minQuantityGrams) ? QuantityCheck.BelowMinimum : QuantityCheck.Ok;
    }

    /// <summary>
    /// How much a customer can order given the free stock: the free amount capped at the maximum, floored to the weight step,
    /// and 0 when that is below the product's minimum (the product is then simply "sold out" for the customer).
    /// </summary>
    public static int AvailableQuantity(ProductUnit unit, int freeStock, int? stepGrams, int? minQuantityGrams)
    {
        if (freeStock <= 0) return 0;
        var max = Math.Min(freeStock, MaxQuantity(unit));
        if (unit == ProductUnit.Weight) max = max / EffectiveStep(stepGrams) * EffectiveStep(stepGrams);
        return max < MinQuantity(unit, stepGrams, minQuantityGrams) ? 0 : max;
    }

    /// <summary>Validates a quantity in a staff edit: only the step (of the line's snapshot) and the bounds — no product minimum.</summary>
    public static bool IsValidForEdit(ProductUnit unit, int quantity, int? stepGrams)
    {
        if (quantity < 1 || quantity > MaxQuantity(unit)) return false;
        return unit == ProductUnit.Piece || quantity % EffectiveStep(stepGrams) == 0;
    }

    /// <summary>Actual weight at issue: 1..100 000 g, no step required.</summary>
    public static bool IsValidActualWeight(int grams) => grams is >= 1 and <= MaxActualGrams;

    /// <summary>
    /// Product input: applies the defaults (step → 100, minimum → step) and validates a weight product's step
    /// and minimum. The texts are the contract's (API_CONTRACT_CYCLE23.md §410.2).
    /// </summary>
    public static bool TryNormalizeWeight(int? stepGrams, int? minQuantityGrams, out int step, out int min, out string? error)
    {
        step = stepGrams ?? DefaultStepGrams;
        min = minQuantityGrams ?? step;
        error = null;
        if (step < MinStepGrams || step > MaxStepGrams)
        {
            error = "Шаг — от 10 до 5000 г";
            return false;
        }
        if (min < step || min % step != 0 || min > MaxGrams)
        {
            error = "Минимальный вес — не меньше шага и кратен ему";
            return false;
        }
        return true;
    }
}
