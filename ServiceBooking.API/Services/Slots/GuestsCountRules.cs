namespace ServiceBooking.API.Services.Slots;

public readonly record struct GuestsCountResult(bool Ok, int? Stored, string? Error);

/// <summary>ARCHITECTURE_CYCLE42.md §42.7 — number of guests of a booking. capacity = null (a "Дома" service) → the value is ignored and stored as null.</summary>
public static class GuestsCountRules
{
    public const string RequiredText = "Укажите, сколько человек придёт";

    public static string OutOfRangeText(int capacity) => $"Число гостей — от 1 до {capacity}";

    public static GuestsCountResult Validate(int? guestsCount, int? capacity)
    {
        if (capacity is not { } cap) return new(true, null, null);
        if (guestsCount is not { } guests) return new(false, null, RequiredText);
        if (guests < 1 || guests > cap) return new(false, null, OutOfRangeText(cap));
        return new(true, guests, null);
    }
}
