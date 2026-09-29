using ServiceBooking.API.Services.Orders;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Shops;

/// <summary>The validated, normalized form of a product input — what the controller stores.</summary>
public sealed record NormalizedProductInput(
    string Name, string? Description, decimal Price, string? PortionText, int? WeightStepGrams, int? MinQuantityGrams,
    string? CompositionAndAllergens);

/// <summary>
/// ARCHITECTURE_CYCLE23.md §410.2 — validation and normalization of <c>ProductInput</c> as a pure function. The
/// texts are the contract's (400 bare strings). Checked in the contract's order; the first error wins.
/// </summary>
public static class ProductInputRules
{
    public const decimal MinPrice = 0.01m;
    public const decimal MaxPrice = 1_000_000m;

    public static bool TryNormalize(
        string? name, string? description, decimal price, ProductUnit unit, string? portionText,
        int? weightStepGrams, int? minQuantityGrams, string? compositionAndAllergens,
        out NormalizedProductInput? result, out string? error)
    {
        result = null;
        var trimmedName = (name ?? string.Empty).Trim();
        if (trimmedName.Length is < 1 or > 200) return Fail("Укажите название товара", out error);
        if (price < MinPrice || price > MaxPrice || decimal.Round(price, 2) != price)
            return Fail("Цена — от 0,01 до 1 000 000 ₽, не больше двух знаков после запятой", out error);

        string? portion = null;
        int? step = null, min = null;
        if (unit == ProductUnit.Piece)
        {
            if (weightStepGrams is not null || minQuantityGrams is not null)
                return Fail("Шаг и минимальный вес указываются только у весового товара", out error);
            portion = NullIfBlank(portionText);
            if (portion is { Length: > 50 }) return Fail("Порция — не длиннее 50 символов", out error);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(portionText)) return Fail("Порция указывается только у штучного товара", out error);
            if (!OrderQuantityRules.TryNormalizeWeight(weightStepGrams, minQuantityGrams, out var s, out var m, out var weightError))
                return Fail(weightError!, out error);
            step = s;
            min = m;
        }

        var desc = NullIfBlank(description);
        if (desc is { Length: > 2000 }) return Fail("Описание — не длиннее 2000 символов", out error);
        var composition = NullIfBlank(compositionAndAllergens);
        if (composition is { Length: > 2000 }) return Fail("Состав — не длиннее 2000 символов", out error);

        result = new NormalizedProductInput(trimmedName, desc, price, portion, step, min, composition);
        error = null;
        return true;
    }

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
