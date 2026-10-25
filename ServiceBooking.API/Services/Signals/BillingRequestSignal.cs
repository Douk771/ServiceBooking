using System.Globalization;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Signals;

/// <summary>
/// Text of the "operator signal" about a new subscription / messenger-payment request (US-48-08, ТЛ48-08;
/// ARCHITECTURE_CYCLE48_STOREFRONT.md §48.11, API_CONTRACT_CYCLE48_STOREFRONT.md §48.25). The operator promised an invoice by the next working day, so a request must
/// reach them the same day. Pure: no EF, no HTTP, no clock of its own.
///
/// Personal data never goes into the message (the rule of <see cref="IGlitchTipSignalService"/>, legal §6 of cycle 16): only the line, the catalog tariff name,
/// the number of option lines, the first 8 hex digits of the billing-account id and a date.
/// </summary>
public static class BillingRequestSignal
{
    private const string OperatorTimeZoneId = "Asia/Barnaul";

    private static readonly Lazy<TimeZoneInfo> OperatorTimeZone = new(() => TimeZoneInfo.FindSystemTimeZoneById(OperatorTimeZoneId));

    /// <summary>"Новая заявка на подписку: линейка=Services, тариф=«Салон», опций=2, аккаунт=1a2b3c4d, счёт выставить до 27.10.2026".</summary>
    public static string ForSubscriptionRequest(CompanyKind line, string? planName, int optionLines, Guid billingAccountId, DateOnly invoiceDueBy)
    {
        var plan = string.IsNullOrWhiteSpace(planName) ? "тариф=без смены" : $"тариф=«{planName}»";
        return $"Новая заявка на подписку: линейка={line}, {plan}, опций={optionLines.ToString(CultureInfo.InvariantCulture)}, "
            + $"аккаунт={ShortId(billingAccountId)}, счёт выставить до {FormatDate(invoiceDueBy)}";
    }

    /// <summary>"Новая заявка на опцию: мессенджер=WhatsApp, аккаунт=1a2b3c4d, счёт выставить до 27.10.2026".</summary>
    public static string ForMessengerPaymentRequest(NotificationTransport transport, Guid billingAccountId, DateOnly invoiceDueBy)
    {
        var messenger = transport == NotificationTransport.Max ? "MAX" : "WhatsApp";
        return $"Новая заявка на опцию: мессенджер={messenger}, аккаунт={ShortId(billingAccountId)}, счёт выставить до {FormatDate(invoiceDueBy)}";
    }

    /// <summary>"Not later than the next working day after the day of submission": the date in Barnaul plus one working day. Public holidays are not
    /// counted, so the date is never later than the real one — the stricter reading.</summary>
    public static DateOnly InvoiceDueBy(DateTime utcNow)
    {
        var utc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, OperatorTimeZone.Value);
        return DateOnly.FromDateTime(WorkingDays.Add(local.Date, 1));
    }

    private static string ShortId(Guid id) => id.ToString("N")[..8];

    private static string FormatDate(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
}
