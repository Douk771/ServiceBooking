using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications.Funding;

/// <summary>One company of the account, reduced to the "does it want customer messages" facts (§40.4.4).
/// <paramref name="EnabledTypeMask"/> is the salon mask; <see langword="null"/> = no settings row (the default applies).</summary>
public readonly record struct CompanyDemandFacts(
    CompanyKind Kind, bool IsActive, bool IsShowcase, int? EnabledTypeMask, bool ShopCustomerMessengerEnabled, bool StaysGuestMessengerEnabled);

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.4.4 — "the account has an active, non-showcase company with customer messaging
/// switched on": salon — <c>EnabledTypeMask &amp; BookingTypesMask ≠ 0</c>; shop — <c>CustomerMessengerEnabled</c>;
/// "Дома" — <c>GuestMessengerEnabled</c>. Input of <c>ChannelIdleCalculator.Recompute(hasDemand)</c>. The platform
/// switch does not start idling (decided by the caller). Pure.
/// </summary>
public static class MessagingDemand
{
    /// <summary>Bit mask of the salon booking types addressed to the CLIENT (confirmation, reminder, cancellation,
    /// reschedule). The staff types (<c>Staff*</c>) are push/MAX to employees and are not "messages to customers", so
    /// they do not create demand for the account's number. Interpretation of §40.4.4's <c>BookingTypesMask</c>.</summary>
    public static readonly int BookingTypesMask = new[]
    {
        NotificationType.BookingConfirmed, NotificationType.Reminder, NotificationType.BookingCancelled, NotificationType.BookingRescheduled,
    }.Aggregate(0, (mask, type) => mask | (1 << (int)type));

    public static bool HasDemand(IEnumerable<CompanyDemandFacts> companies) => companies.Any(WantsCustomerMessages);

    public static bool WantsCustomerMessages(CompanyDemandFacts c)
    {
        if (!c.IsActive || c.IsShowcase) return false;
        return c.Kind switch
        {
            CompanyKind.Services => ((c.EnabledTypeMask ?? Core.Entities.CompanyNotificationSettings.DefaultEnabledTypeMask) & BookingTypesMask) != 0,
            CompanyKind.Orders => c.ShopCustomerMessengerEnabled,
            CompanyKind.Stays or CompanyKind.Baths => c.StaysGuestMessengerEnabled, // «Бани» keep the guest flag in the same StaysSettings
            _ => false,
        };
    }
}
