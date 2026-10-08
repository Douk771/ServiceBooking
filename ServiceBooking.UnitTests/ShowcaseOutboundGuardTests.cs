using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE28.md §576 — the pure suppression rule and the delivery-log wording of the new reason.</summary>
public class ShowcaseOutboundGuardTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void IsSuppressed_HoldsForShowcaseCompaniesAndForEveryCompanyInDemoMode(bool isShowcase, bool demoMode, bool expected) =>
        ShowcaseOutboundGuard.IsSuppressed(new Company { IsShowcase = isShowcase }, demoMode).Should().Be(expected);

    [Fact]
    public void IsSuppressed_DoesNotDependOnAnyOtherCompanySetting()
    {
        // Held on the mark alone: a fully "enabled" showcase company is still suppressed.
        var company = new Company { IsShowcase = true, AllowSelfBooking = true, ShowInPublicListing = true, IsActive = true };

        ShowcaseOutboundGuard.IsSuppressed(company, demoMode: false).Should().BeTrue();
    }

    [Fact]
    public void ShowcaseSuppressed_IsAppendedAtTheEndOfTheEnum_SoStoredRowsKeepTheirMeaning()
    {
        // The enum is stored as an integer; append-only (§572.2). Every earlier member keeps its number.
        var values = Enum.GetValues<NotificationReason>().Select(v => (int)v).ToList();

        // Cycle 37 appended three "Дома" reasons AFTER it (append-only): it keeps its number and the members before it stay put.
        ((int)NotificationReason.ShowcaseSuppressed).Should().Be(30);
        // Cycle 40 appended three more (34..36), so the cycle-37 trio is pinned by number, not relative to the maximum.
        ((int)NotificationReason.StayMessageOutdated).Should().Be(31);
        values.Max().Should().BeGreaterThanOrEqualTo(33);
        ((int)NotificationReason.StaffMaxShopInactive).Should().Be((int)NotificationReason.ShowcaseSuppressed - 1);
    }

    [Fact]
    public void DeliveryLogText_NamesTheReason()
    {
        var text = NotificationTexts.StatusText(NotificationStatus.Skipped, NotificationReason.ShowcaseSuppressed, channelId: null, readAtUtc: null, attemptCount: 0);

        text.Should().Contain("демонстрационная");
    }
}
