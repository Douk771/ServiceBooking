using FluentAssertions;
using ServiceBooking.API.Services.Retention;

namespace ServiceBooking.UnitTests;

/// <summary>T5-B8/B9 (ARCHITECTURE_CYCLE5.md §49.4: "вычисление cutoff'ов — чистая функция... покрывается
/// юнит-тестами без БД"). <see cref="RetentionPlan.CutoffsFor"/> is the one place every rule's "how old is
/// old enough" question is answered — these tests are the only place that question is checked without a
/// database or a clock.</summary>
public class RetentionPlanTests
{
    private static readonly DateTime Now = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

    private static RetentionPeriods DefaultPeriods() => new();

    [Fact]
    public void CutoffsFor_NotificationBody_IsNowMinusConfiguredDays()
    {
        var periods = DefaultPeriods();

        var cutoffs = RetentionPlan.CutoffsFor(Now, periods);

        cutoffs.NotificationBody.Should().Be(Now.AddDays(-periods.NotificationBodyDays));
    }

    [Fact]
    public void CutoffsFor_EveryCategory_MatchesItsOwnConfiguredDays()
    {
        var periods = new RetentionPeriods
        {
            NotificationBodyDays = 30, NotificationMetadataDays = 365, TemplateHistoryDays = 1095,
            InactiveAccountDays = 1095, BookingPersonalizationDays = 1095, ClientNoteDays = 1095,
            ClientNotePhotoDays = 365, ClientHealthNoteDays = 1095, ConsentRecordDays = 1095,
            ChannelStateEventDays = 365, PaymentLogDays = 1825, MailLogDays = 365, AppLogDays = 90,
        };

        var cutoffs = RetentionPlan.CutoffsFor(Now, periods);

        cutoffs.NotificationBody.Should().Be(Now.AddDays(-30));
        cutoffs.NotificationMetadata.Should().Be(Now.AddDays(-365));
        cutoffs.TemplateHistory.Should().Be(Now.AddDays(-1095));
        cutoffs.InactiveAccount.Should().Be(Now.AddDays(-1095));
        cutoffs.BookingPersonalization.Should().Be(Now.AddDays(-1095));
        cutoffs.ClientNote.Should().Be(Now.AddDays(-1095));
        cutoffs.ClientNotePhoto.Should().Be(Now.AddDays(-365));
        cutoffs.ClientHealthNote.Should().Be(Now.AddDays(-1095));
        cutoffs.ConsentRecord.Should().Be(Now.AddDays(-1095));
        cutoffs.ChannelStateEvent.Should().Be(Now.AddDays(-365));
        cutoffs.PaymentLog.Should().Be(Now.AddDays(-1825));
        cutoffs.MailLog.Should().Be(Now.AddDays(-365));
        cutoffs.AppLog.Should().Be(Now.AddDays(-90));
    }

    [Fact]
    public void CutoffsFor_DifferentPeriods_ProduceDifferentCutoffs()
    {
        // A change to one category's configured days must not silently affect another — each cutoff is
        // computed from its OWN field, not shared arithmetic.
        var periods = new RetentionPeriods { NotificationBodyDays = 30, MailLogDays = 400 };

        var cutoffs = RetentionPlan.CutoffsFor(Now, periods);

        cutoffs.NotificationBody.Should().NotBe(cutoffs.MailLog);
    }

    [Fact]
    public void CutoffsFor_IsPure_SameInputsAlwaysProduceSameOutput()
    {
        var periods = DefaultPeriods();

        var first = RetentionPlan.CutoffsFor(Now, periods);
        var second = RetentionPlan.CutoffsFor(Now, periods);

        first.Should().Be(second);
    }

    [Fact]
    public void CutoffsFor_FutureNow_MovesEveryCutoffForwardByTheSameAmount()
    {
        // §49.4's last bullet: functional tests pass NowUtc in the future to see an effect immediately.
        // The cutoff for every category must move forward by exactly that same delta — nothing is anchored
        // to real wall-clock time.
        var periods = DefaultPeriods();
        var future = Now.AddDays(400);

        var atNow = RetentionPlan.CutoffsFor(Now, periods);
        var atFuture = RetentionPlan.CutoffsFor(future, periods);

        (atFuture.NotificationBody - atNow.NotificationBody).Should().Be(TimeSpan.FromDays(400));
        (atFuture.ConsentRecord - atNow.ConsentRecord).Should().Be(TimeSpan.FromDays(400));
    }
}
