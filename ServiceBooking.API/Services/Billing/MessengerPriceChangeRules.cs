using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Billing;

public readonly record struct MessengerPriceState(decimal? PricePerMonth, bool IsActive);

public sealed record MessengerPriceConsequences(
    bool SellableBefore, bool SellableAfter, bool StartsSelling, bool StopsSelling, bool IsZeroPrice, IReadOnlyList<string> Texts);

/// <summary>
/// ARCHITECTURE_CYCLE43.md §43.3.3, API_CONTRACT_CYCLE43.md §43.23 — validation of a price change and the consequences shown before it is saved.
/// Pure. Vectors: <c>priceValidation</c> and <c>consequences</c> in contracts/cycle43/messenger-vectors.json.
/// </summary>
public static class MessengerPriceChangeRules
{
    public const decimal MaxPrice = 99_999_999.99m;
    public const int MaxCommentLength = 500;

    /// <summary>Checks of the price itself: null (price removed) or 0…99 999 999,99 with at most two decimals. Returns the 400 text or null.</summary>
    public static string? ValidatePrice(decimal? price)
    {
        if (price is not { } p) return null;
        if (p < 0) return MessengerTexts.PriceNegative;
        if (p > MaxPrice) return MessengerTexts.PriceTooLarge;
        if (decimal.Round(p, 2) != p) return MessengerTexts.PriceTooPrecise;
        return null;
    }

    /// <summary>The whole body check in the order of §43.23.1. <paramref name="current"/> is null when the catalog row is missing
    /// (then "nothing changed" cannot be judged — the caller answers 409). <paramref name="comment"/> is checked only when
    /// <paramref name="commentRequired"/> (the PUT).</summary>
    public static string? Validate(decimal? newPrice, bool? newIsActive, MessengerPriceState? current, bool commentRequired, string? comment)
    {
        if (ValidatePrice(newPrice) is { } priceError) return priceError;
        if (current is { } c && c.PricePerMonth == newPrice && c.IsActive == (newIsActive ?? c.IsActive)) return MessengerTexts.PriceUnchanged;
        if (!commentRequired) return null;
        var trimmed = comment?.Trim() ?? "";
        if (trimmed.Length == 0) return MessengerTexts.PriceCommentRequired;
        if (trimmed.Length > MaxCommentLength) return MessengerTexts.PriceCommentTooLong;
        return null;
    }

    public static MessengerPriceConsequences Consequences(
        NotificationTransport transport, MessengerPriceState before, MessengerPriceState after, bool open, bool termsOwnerPublished,
        int activePaidAccounts)
    {
        var saleBefore = ChannelOptionAvailability.Explain(open, before.IsActive, before.PricePerMonth, termsOwnerPublished);
        var saleAfter = ChannelOptionAvailability.Explain(open, after.IsActive, after.PricePerMonth, termsOwnerPublished);
        var starts = !saleBefore.Sellable && saleAfter.Sellable;
        var stops = saleBefore.Sellable && !saleAfter.Sellable;
        var zero = after.PricePerMonth == 0m;

        var texts = new List<string>();
        if (starts) texts.Add(MessengerTexts.ConsequenceStartsSelling(transport));
        if (stops) texts.Add(MessengerTexts.ConsequenceStopsSelling(transport));
        if (before.PricePerMonth is not null && after.PricePerMonth is not null && before.PricePerMonth != after.PricePerMonth)
            texts.Add(MessengerTexts.ConsequencePaidAccounts(transport, activePaidAccounts));
        if (zero) texts.Add(MessengerTexts.ConsequenceZeroPrice(transport));
        if (after.PricePerMonth is not null && saleAfter.Reason is { } reason)
            texts.Add(MessengerTexts.ConsequenceNotSellable(transport, reason));

        return new MessengerPriceConsequences(saleBefore.Sellable, saleAfter.Sellable, starts, stops, zero, texts);
    }
}
