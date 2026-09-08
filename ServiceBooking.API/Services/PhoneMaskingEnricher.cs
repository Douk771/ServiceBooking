using Serilog.Core;
using Serilog.Events;

namespace ServiceBooking.API.Services;

/// <summary>
/// Third rung of the defence against phone numbers in logs (ARCHITECTURE.md §11.3 p.3): a safety net
/// for the interpolated string someone writes through <c>logger.LogError($"... {phone} ...")</c> six
/// months from now, not a replacement for reviewing log call sites. Scans every string-valued property
/// of a log event for 10–15 digit runs and masks them (<see cref="LogMasking.MaskPhoneSequences"/>).
///
/// Restricted to Warning-and-above events and events carrying an exception: a successful request's log
/// line (Information — method, path, status, duration, ids, traceId) contains no personal data by
/// construction, so running a regex over its properties on every single request would be pure cost with
/// nothing to catch.
/// </summary>
public class PhoneMaskingEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent.Level < LogEventLevel.Warning && logEvent.Exception is null)
            return;

        List<LogEventProperty>? replacements = null;
        foreach (var (name, value) in logEvent.Properties)
        {
            if (value is not ScalarValue { Value: string text }) continue;

            var masked = LogMasking.MaskPhoneSequences(text);
            if (masked == text) continue;

            (replacements ??= []).Add(propertyFactory.CreateProperty(name, masked));
        }

        if (replacements is null) return;
        foreach (var property in replacements)
            logEvent.AddOrUpdateProperty(property);
    }
}
