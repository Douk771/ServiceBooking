using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.2.3, §40.13 — the ONE writer of <see cref="ChannelOptionChangeLog"/>, the append-only journal of
/// changes to the two channel options (<c>notifications.whatsapp</c>, <c>notifications.max</c>) of a billing account. Every code path
/// that creates or changes an <see cref="AccountSubscriptionOption"/> row of those options calls it: the two admin assignment paths
/// and «Подтвердить оплату», the trial grant, the opening of the trial mailing window and the trial expiry. Like the other journal
/// writers it only adds the row to the caller's change tracker — the caller's <c>SaveChangesAsync</c> commits it with the change itself.
/// </summary>
public static class ChannelOptionLog
{
    public static bool IsChannelOption(string? optionCode) =>
        optionCode is ChannelOptionCodes.WhatsApp or ChannelOptionCodes.Max;

    /// <summary>Adds one journal row, or nothing when <paramref name="optionCode"/> is not a channel option. Returns whether a row was added.</summary>
    public static bool Write(
        AppDbContext db, Guid billingAccountId, string optionCode, ChannelOptionChangeSource source,
        DateTime? oldPaidUntilUtc, DateTime? newPaidUntilUtc, DateTime? oldEndsAtUtc, DateTime? newEndsAtUtc,
        string? changedByUserId, DateTime? changedAtUtc = null, Guid? channelId = null, string? comment = null)
    {
        if (!IsChannelOption(optionCode)) return false;
        db.ChannelOptionChangeLogs.Add(new ChannelOptionChangeLog
        {
            Id = Guid.NewGuid(),
            BillingAccountId = billingAccountId,
            OptionCode = optionCode,
            Source = (int)source,
            OldPaidUntilUtc = oldPaidUntilUtc,
            NewPaidUntilUtc = newPaidUntilUtc,
            OldEndsAtUtc = oldEndsAtUtc,
            NewEndsAtUtc = newEndsAtUtc,
            ChangedByUserId = changedByUserId,
            ChangedAtUtc = changedAtUtc ?? DateTime.UtcNow,
            ChannelId = channelId,
            Comment = comment is { Length: > 500 } ? comment[..500] : comment,
        });
        return true;
    }
}
