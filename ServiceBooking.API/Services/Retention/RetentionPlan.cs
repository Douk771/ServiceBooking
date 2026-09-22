namespace ServiceBooking.API.Services.Retention;

/// <summary>One UTC cutoff instant per retention category — a row/file older than the corresponding
/// cutoff is eligible for this pass. Pure data, produced only by <see cref="RetentionPlan.CutoffsFor"/>.</summary>
public readonly record struct RetentionCutoffs(
    DateTime NotificationBody,
    DateTime NotificationMetadata,
    DateTime TemplateHistory,
    DateTime InactiveAccount,
    DateTime BookingPersonalization,
    DateTime ClientNote,
    DateTime ClientNotePhoto,
    DateTime ClientHealthNote,
    DateTime ConsentRecord,
    DateTime ChannelStateEvent,
    DateTime PaymentLog,
    DateTime MailLog,
    DateTime AppLog);

/// <summary>
/// Pure cutoff arithmetic (ARCHITECTURE_CYCLE5.md §49.4) — every rule's "how old is old enough" question
/// reduces to one subtraction, computed once per pass and handed to every rule via <see cref="RetentionContext"/>.
/// No DB, no clock read inside — <c>nowUtc</c> is the caller's <see cref="DateTime.UtcNow"/>
/// (real mode) or a test-supplied future instant (functional tests, per §49.4's last bullet), matching
/// the established convention of <see cref="PhotoQuota.CutoffUtc"/>.
/// </summary>
public static class RetentionPlan
{
    public static RetentionCutoffs CutoffsFor(DateTime nowUtc, RetentionPeriods periods) => new(
        NotificationBody: nowUtc.AddDays(-periods.NotificationBodyDays),
        NotificationMetadata: nowUtc.AddDays(-periods.NotificationMetadataDays),
        TemplateHistory: nowUtc.AddDays(-periods.TemplateHistoryDays),
        InactiveAccount: nowUtc.AddDays(-periods.InactiveAccountDays),
        BookingPersonalization: nowUtc.AddDays(-periods.BookingPersonalizationDays),
        ClientNote: nowUtc.AddDays(-periods.ClientNoteDays),
        ClientNotePhoto: nowUtc.AddDays(-periods.ClientNotePhotoDays),
        ClientHealthNote: nowUtc.AddDays(-periods.ClientHealthNoteDays),
        ConsentRecord: nowUtc.AddDays(-periods.ConsentRecordDays),
        ChannelStateEvent: nowUtc.AddDays(-periods.ChannelStateEventDays),
        PaymentLog: nowUtc.AddDays(-periods.PaymentLogDays),
        MailLog: nowUtc.AddDays(-periods.MailLogDays),
        AppLog: nowUtc.AddDays(-periods.AppLogDays));
}
