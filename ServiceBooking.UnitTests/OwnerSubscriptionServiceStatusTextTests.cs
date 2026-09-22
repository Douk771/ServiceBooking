using FluentAssertions;
using ServiceBooking.API.Services.Billing;

namespace ServiceBooking.UnitTests;

/// <summary>OwnerSubscriptionService.StatusTextFor — since cycle 7, also reused by
/// AdminBillingController's AdminBillingAccountListItemDto/AdminBillingAccountDto (merge-review
/// finding: the admin billing-accounts screen only had a bare `status` enum, forcing the frontend to
/// keep its own `STATUS_LABEL_RU` translation dictionary — every other text on that screen, e.g.
/// AdminSubscribedOptionDto.statusText, is already server-assembled per §41 п. 8).</summary>
public class OwnerSubscriptionServiceStatusTextTests
{
    [Fact]
    public void Free_HasNoDate_ReadsAsFreePlan() =>
        OwnerSubscriptionService.StatusTextFor("Free", null).Should().Be("Бесплатный тариф");

    [Fact]
    public void Active_WithPaidUntil_NamesTheDate() =>
        OwnerSubscriptionService.StatusTextFor("Active", new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc))
            .Should().Be("Оплачено до 31.10.2026");

    [Fact]
    public void Active_WithoutPaidUntil_ReadsAsSimplyActive() =>
        OwnerSubscriptionService.StatusTextFor("Active", null).Should().Be("Активна");

    [Fact]
    public void Expired_WithPaidUntil_NamesTheExpiryDate() =>
        OwnerSubscriptionService.StatusTextFor("Expired", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc))
            .Should().Be("Подписка истекла 01.09.2026");

    [Fact]
    public void Expired_WithoutPaidUntil_ReadsAsInactive() =>
        OwnerSubscriptionService.StatusTextFor("Expired", null).Should().Be("Подписка неактивна");
}
