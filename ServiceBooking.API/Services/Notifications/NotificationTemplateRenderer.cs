namespace ServiceBooking.API.Services.Notifications;

/// <summary>Everything a template's placeholders might need — some fields only apply to certain
/// notification types (e.g. <see cref="CancellationReason"/> for <c>BookingCancelled</c>) and are left
/// null/empty otherwise, which <see cref="NotificationTemplateRenderer.Render"/> substitutes as "".</summary>
public sealed record TemplateContext(
    string ClientName,
    string ServiceName,
    string MasterName,
    string Date,
    string Time,
    string CompanyName,
    string? Address = null,
    string? CompanyPhone = null,
    string? CancellationReason = null,
    string? NewDate = null,
    string? NewTime = null);

/// <summary>
/// Substitutes a template's placeholders (ARCHITECTURE_CYCLE4.md §25). Pure string replacement — the
/// text this produces is what gets snapshotted into <c>OutboundNotification.Body</c> at queue time
/// (US-59 p.7), so a later template edit never changes wording already queued.
/// </summary>
public static class NotificationTemplateRenderer
{
    public static string Render(string body, TemplateContext ctx) =>
        body
            .Replace("{КлиентИмя}", ctx.ClientName)
            .Replace("{Услуга}", ctx.ServiceName)
            .Replace("{Мастер}", ctx.MasterName)
            .Replace("{Дата}", ctx.Date)
            .Replace("{Время}", ctx.Time)
            .Replace("{Салон}", ctx.CompanyName)
            .Replace("{Адрес}", ctx.Address ?? string.Empty)
            .Replace("{ТелефонСалона}", ctx.CompanyPhone ?? string.Empty)
            .Replace("{ПричинаОтмены}", ctx.CancellationReason ?? string.Empty)
            .Replace("{НоваяДата}", ctx.NewDate ?? string.Empty)
            .Replace("{НовоеВремя}", ctx.NewTime ?? string.Empty);

    /// <summary>Appends the non-removable unsubscribe line AFTER the rendered body (US-33 p.4, §31.4).
    /// Building the line itself (token, URL) is <c>UnsubscribeTokens</c>' job, not this one's — this is
    /// only the "the line goes last, separated by a blank line" rule, kept in one place.</summary>
    public static string AppendUnsubscribeLine(string renderedBody, string unsubscribeLine) =>
        $"{renderedBody}\n\n{unsubscribeLine}";
}
