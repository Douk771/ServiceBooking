using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// ARCHITECTURE_CYCLE43.md §43.2.1 — the ONE writer of <see cref="SubscriptionOptionPriceChangeLog"/>, the append-only journal of changes
/// to the price or activity of the two messenger options. Like <see cref="ChannelOptionLog"/> it only adds the row to the caller's change
/// tracker; the caller's <c>SaveChangesAsync</c> commits it together with the change itself.
/// </summary>
public static class OptionPriceLog
{
    /// <summary>A row is written only for a messenger option and only when the price or the activity actually changed.</summary>
    public static bool ShouldWrite(string? optionCode, decimal? oldPrice, decimal? newPrice, bool oldIsActive, bool newIsActive) =>
        ChannelOptionLog.IsChannelOption(optionCode) && (oldPrice != newPrice || oldIsActive != newIsActive);

    public static bool Write(
        AppDbContext db, SubscriptionOption option, decimal? oldPrice, decimal? newPrice, bool oldIsActive, bool newIsActive,
        OptionPriceChangeSource source, int? activePaidAccounts, string? changedByUserId, string? comment, DateTime? changedAtUtc = null)
    {
        if (!ShouldWrite(option?.Code, oldPrice, newPrice, oldIsActive, newIsActive)) return false;
        db.SubscriptionOptionPriceChangeLogs.Add(new SubscriptionOptionPriceChangeLog
        {
            Id = Guid.NewGuid(),
            OptionId = option!.Id,
            OptionCode = option.Code,
            OldPricePerMonth = oldPrice,
            NewPricePerMonth = newPrice,
            OldIsActive = oldIsActive,
            NewIsActive = newIsActive,
            Source = (int)source,
            ActivePaidAccounts = activePaidAccounts,
            ChangedByUserId = changedByUserId,
            ChangedAtUtc = changedAtUtc ?? DateTime.UtcNow,
            Comment = comment is { Length: > 500 } ? comment[..500] : comment,
        });
        return true;
    }
}
