namespace ServiceBooking.API.Services.Retention;

/// <summary>
/// Retention periods as configuration (ARCHITECTURE_CYCLE5.md §49.5) — bound from the <c>Retention</c>
/// section. Starting values come from the lawyer's table (LEGAL_REVIEW.md §13.5), accepted by the
/// customer as a starting point and deliberately kept OUT of code so a future change is a config edit,
/// not a redeploy. Two of these (<see cref="ConsentRecordDays"/>, <see cref="TemplateHistoryDays"/>) are
/// additionally fail-fast-checked at startup (<see cref="DeploymentSafetyChecks.ValidateRetentionPeriods"/>)
/// because they are legally load-bearing minimums, not just defaults an operator is free to shorten.
/// </summary>
public sealed class RetentionPeriods
{
    public const string SectionName = "Retention";

    /// <summary>US-73, §49.3: age (from the terminal status timestamp) after which an
    /// <see cref="Core.Entities.OutboundNotification"/>'s <c>Body</c>/<c>RecipientName</c>/<c>RecipientPhone</c>
    /// are wiped in place. Default 30 (LEGAL_REVIEW.md §13.5, row 1 — "окно разбора жалоб").</summary>
    public int NotificationBodyDays { get; set; } = 30;

    /// <summary>Age (from the same reference point as <see cref="NotificationBodyDays"/>) after which an
    /// already-redacted notification row is removed outright — it has carried no personal data since
    /// redaction, and nothing in the product reads a delivery journal entry older than this. Default 365.</summary>
    public int NotificationMetadataDays { get; set; } = 365;

    /// <summary>🔴 Fail-fast minimum 365 days (LEGAL_REVIEW.md §13.5: "не сокращать" — advertising
    /// limitation period is 1 year under ст. 4.5 КоАП). Age of a <see cref="Core.Entities.NotificationTemplateHistory"/>
    /// row since <c>ChangedAtUtc</c>. Default 1095 (3 years, matching the general limitation period, ст. 196 ГК).</summary>
    public int TemplateHistoryDays { get; set; } = 1095;

    /// <summary>Age since a client account's last observable activity (registration, or last booking as
    /// a client) after which the account is anonymized in place — same fields DeleteAccount already
    /// scrubs, just triggered by inactivity instead of the subject's own request. Default 1095 (3 years,
    /// ст. 196 ГК).</summary>
    public int InactiveAccountDays { get; set; } = 1095;

    /// <summary>Age since a booking's visit date after which it is anonymized (same mechanism as
    /// <see cref="Core.Entities.Booking.ClientDeleted"/>, already used by DeleteAccount). Default 1095.</summary>
    public int BookingPersonalizationDays { get; set; } = 1095;

    /// <summary>Age since a <see cref="Core.Entities.ClientNote"/> was written after which it is deleted.
    /// Default 1095 (3 years — LEGAL_REVIEW.md §13.5: "через 3 года «аллергия» неактуальна").</summary>
    public int ClientNoteDays { get; set; } = 1095;

    /// <summary>Legal backstop cap for <see cref="Core.Entities.ClientNotePhoto"/>, independent of the
    /// per-company tariff window <c>PhotoRetentionCleanupTask</c> already enforces (which is capped
    /// at 12 months since <c>PhotoRetention.Forever</c> was removed, §44.7) — this rule exists so a future
    /// tariff misconfiguration can never exceed the legal maximum. Default 365.</summary>
    public int ClientNotePhotoDays { get; set; } = 365;

    /// <summary>Age since a <see cref="Core.Entities.ClientHealthNote"/> was last updated after which it
    /// is deleted. Default 1095, matching <see cref="ClientNoteDays"/> (health notes are a specialised
    /// client note, §44.3).</summary>
    public int ClientHealthNoteDays { get; set; } = 1095;

    /// <summary>🔴 Fail-fast minimum 1095 days (LEGAL_REVIEW.md §13.5: operator must prove consent,
    /// ч. 1 ст. 9 — limitation period). Age of a <see cref="Core.Entities.ConsentRecord"/> since it was
    /// revoked (never touches un-revoked/active rows — an active consent has no age at which it expires
    /// on its own). Default 1095.</summary>
    public int ConsentRecordDays { get; set; } = 1095;

    /// <summary>ARCHITECTURE_CYCLE10.md §107 (Q2). Age of a <see cref="Core.Entities.BookingEvent"/>
    /// since <c>OccurredAtUtc</c>. Default 0, read by <see cref="Rules.BookingEventRule"/> as "срок не
    /// задан" — legal-counsel hasn't given a number yet, so at 0 the rule deletes nothing and says so in
    /// its summary line rather than guessing a cutoff. NOT fail-fast-checked at startup, unlike
    /// <see cref="ConsentRecordDays"/>/<see cref="TemplateHistoryDays"/> above: those guard a KNOWN legal
    /// minimum, this one's minimum is the open question — refusing to start the app over it would block
    /// the whole cycle's release on legal's timeline, which the spec doesn't ask for.</summary>
    public int BookingEventDays { get; set; } = 0;

    /// <summary>Age of a <see cref="Core.Entities.ChannelStateEvent"/> since it occurred. Default 365.</summary>
    public int ChannelStateEventDays { get; set; } = 365;

    /// <summary>Age of a <see cref="Core.Entities.ChannelPaymentLog"/> since it was recorded — primary
    /// accounting documents (ст. 29 ФЗ «О бухгалтерском учёте»). Default 1825 (5 years).</summary>
    public int PaymentLogDays { get; set; } = 1825;

    /// <summary>Age of a <see cref="Core.Entities.MailLog"/> row since it was sent. Default 365.</summary>
    public int MailLogDays { get; set; } = 365;

    /// <summary>Age (file last-write time) of a file under the application's log directory. Not a DB
    /// rule — <see cref="Rules.AppLogAgeRule"/> only reports a warning count, it never deletes a file
    /// (log rotation/retention is the deployment's job, DEPLOY.md — this is a compliance tripwire, not a
    /// cleanup mechanism). Default 90.</summary>
    public int AppLogDays { get; set; } = 90;

    /// <summary>Directory scanned by <see cref="Rules.AppLogAgeRule"/>. Empty disables the check (no
    /// log directory configured, e.g. under Testing).</summary>
    public string AppLogDirectory { get; set; } = string.Empty;

    /// <summary>ARCHITECTURE_CYCLE9.md §105.11 (US-124). Age of a <see cref="Core.Entities.PushSubscription"/>
    /// since its last successful delivery — or, if it has NEVER delivered successfully, since it was
    /// created (<see cref="Rules.PushSubscriptionRule"/>'s doc comment). Default 180.</summary>
    public int PushSubscriptionDays { get; set; } = 180;

    /// <summary>§105.11. Age of a <see cref="Core.Entities.StaffPushNotification"/> queue/journal row
    /// since it was created. Default 365 — the row is deleted outright, not redacted in place (its
    /// <c>Payload</c> is a one-shot system message, not correspondence worth keeping a scrubbed trace
    /// of).</summary>
    public int StaffPushNotificationDays { get; set; } = 365;
}
