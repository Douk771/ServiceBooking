using System.Text;
using System.Text.RegularExpressions;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Stays;

public enum ReminderMode { Messenger, Page, Push }

public enum ReminderDropReason { ForbiddenPlaceholder, EmptyValue, Digits, Link, Email, Phone, CodeWord }

public enum ReminderWarning { CancellationTermsInText, Passport, CardNumber, PassportNumber }

public enum ReminderErrorCode { TooLong, UnknownPlaceholder, ForbiddenWords }

public sealed record ReminderSessionFact(string ServiceName, DateOnly BusinessDate, int StartMinute, int Hours);

/// <summary>Everything a reminder is made of, taken from the booking and the company at the moment of sending — never from the request.</summary>
public sealed record ReminderFacts(
    string CompanyName, string HouseName, DateOnly CheckInDate, DateOnly CheckOutDate, int Nights, string CheckInTime, string CheckOutTime,
    string? GuestName, string? Address, string? CompanyPhone, int DueAtCheckInRub, string BookingUrl, string? UnsubscribeUrl,
    IReadOnlyList<ReminderSessionFact> Sessions);

public sealed record ReminderDroppedLine(int Line, string Text, ReminderDropReason Reason);

public sealed record ReminderRender(string Text, IReadOnlyList<ReminderDroppedLine> Dropped, bool UsesFixedText);

public sealed record ReminderError(ReminderErrorCode Code, string Message);

public sealed record ReminderValidation(
    IReadOnlyList<ReminderError> Errors, IReadOnlyList<ReminderWarning> Warnings, bool ConfirmationRequired, IReadOnlyList<string> Markers);

public sealed record ReminderPlaceholder(string Token, string Description, bool InMessenger, bool OnPage, bool InPush);

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.11, ЮР39-3/4/5 (Т39-11…13) — the editable reminder sent the day before the check-in. Pure: the validation of the owner's text, the render in
/// three modes (messenger / booking page / web-push) and the hard filter of the push text. A <c>null</c> template keeps the code of cycle 37 byte for byte: the messenger
/// text is the old <see cref="StayNotificationTexts.Messenger"/>, the push text is the old fixed one. Vectors: service-vectors.json → reminderTemplate.
/// </summary>
public static class ArrivalReminderTemplate
{
    public const int TemplateMaxLength = 700;
    public const int MessengerMaxLength = 1000;
    public const int PageMaxLength = 1000;
    public const int PushMaxLength = 180;
    public const string FixedPush = "Завтра заезд — откройте бронь";

    /// <summary>The text shown in the editor when the template is not changed; equal to the old text of cycle 37 (guarded by a test).</summary>
    public const string Default =
        "{Компания}: завтра заезд в «{Дом}» — {ДатаЗаезда} с {ВремяЗаезда}.\nАдрес: {Адрес}.\nК оплате при заселении: {КОплатеПриЗаселении}.\nБронь: {СсылкаНаБронь}";

    private sealed record Spec(string Token, string Description, bool Messenger, bool Page, bool Push, Func<ReminderFacts, string?> Value);

    private static readonly Spec[] Specs =
    [
        new("{Компания}", "Название компании", true, true, true, f => f.CompanyName),
        new("{Дом}", "Название дома", true, true, true, f => f.HouseName),
        new("{ДатаЗаезда}", "Дата заезда, например «15 янв»", true, true, true, f => StayNotificationTexts.Short(f.CheckInDate)),
        new("{ДатаВыезда}", "Дата выезда, например «18 янв»", true, true, true, f => StayNotificationTexts.Short(f.CheckOutDate)),
        new("{Ночей}", "Число ночей, например «3 ночи»", true, true, true, f => StaysTexts.NightsText(f.Nights)),
        new("{ВремяЗаезда}", "Время заезда из брони, например «14:00»", true, true, true, f => f.CheckInTime),
        new("{ВремяВыезда}", "Время выезда из брони, например «12:00»", true, true, true, f => f.CheckOutTime),
        new("{Услуги}", "Услуги к брони со временем; пусто, если услуг нет", true, true, true, f => ServicesText(f)),
        new("{ИмяГостя}", "Имя гостя из брони (не попадает в push)", true, true, false, f => f.GuestName),
        new("{Адрес}", "Адрес дома или компании (не попадает в push)", true, true, false, f => f.Address),
        new("{ТелефонКомпании}", "Телефон компании (не попадает в push)", true, true, false, f => f.CompanyPhone),
        new("{КОплатеПриЗаселении}", "Сумма к оплате при заселении; пусто, если остаток 0 (не попадает в push)", true, true, false,
            f => f.DueAtCheckInRub > 0 ? StaysTexts.Rub(f.DueAtCheckInRub) : null),
        new("{СсылкаНаБронь}", "Ссылка на страницу брони (только в мессенджере)", true, false, false, f => f.BookingUrl),
    ];

    private static readonly Dictionary<string, Spec> ByToken = Specs.ToDictionary(s => s.Token, StringComparer.Ordinal);

    private static readonly Regex PlaceholderRx = new(@"\{[^{}\n]*\}", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex DigitsRx = new(@"\d(?:[\s\-]?\d){3,}", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex LinkRx = new(
        @"https?://|www\.|(?<![@\p{L}\d.\-])[\p{L}\d\-]+(?:\.[\p{L}\d\-]+)*\.(?:ru|рф|com|net|org|su|io)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EmailRx = new(@"[\p{L}\d._%+\-]+@[\p{L}\d.\-]+", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PhoneRx = new(@"(?:\+7|(?<![\d])8)[\s\-(]*\d", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex CodeWordRx = new(@"\bкод|парол|wi-?fi|вай-?фай|ключниц|сейф|домофон", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ForbiddenWordsRx = new(@"задат|невозвратн|депозит", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CancellationWordsRx = new(@"штраф|неустойк|не\s+возвращ", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PassportWordRx = new(@"паспорт", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CardRx = new(@"(?<!\d)(?:\d[ \-]?){12,18}\d(?!\d)", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PassportNumberRx = new(@"(?<!\d)(?:\d{4}\s\d{6}|\d{2}\s\d{2}\s\d{6})(?!\d)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The values shortened first when a text is too long, in this order (§39.11.4 п. 6).</summary>
    private static readonly string[] ShrinkOrder = ["{Адрес}", "{Компания}", "{Дом}", "{Услуги}", "{ИмяГостя}"];

    public static IReadOnlyList<ReminderPlaceholder> Placeholders { get; } =
        Specs.Select(s => new ReminderPlaceholder(s.Token, s.Description, s.Messenger, s.Page, s.Push)).ToList();

    public static string Normalize(string? text) => (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>Is the text the same as the default one (after the normalisation of line breaks)? Then it is stored as NULL.</summary>
    public static bool IsDefault(string? template) => string.Equals(Normalize(template).TrimEnd(), Default, StringComparison.Ordinal);

    // ── validation ──

    public static ReminderValidation Validate(string? template)
    {
        var text = Normalize(template);
        var errors = new List<ReminderError>();
        if (text.Length > TemplateMaxLength)
            errors.Add(new ReminderError(ReminderErrorCode.TooLong, "Текст напоминания — не длиннее 700 символов"));

        foreach (Match m in PlaceholderRx.Matches(text))
            if (!ByToken.ContainsKey(m.Value))
            {
                errors.Add(new ReminderError(ReminderErrorCode.UnknownPlaceholder,
                    $"Неизвестная подстановка {m.Value}. Можно: {string.Join(", ", Specs.Select(s => s.Token))}"));
                break;
            }
        if (ForbiddenWordsRx.IsMatch(text))
            errors.Add(new ReminderError(ReminderErrorCode.ForbiddenWords, "Не используйте слова «задаток», «невозвратный», «депозит»"));

        var warnings = new List<ReminderWarning>();
        if (CancellationWordsRx.IsMatch(text)) warnings.Add(ReminderWarning.CancellationTermsInText);
        if (PassportWordRx.IsMatch(text)) warnings.Add(ReminderWarning.Passport);
        if (CardRx.IsMatch(text)) warnings.Add(ReminderWarning.CardNumber);
        if (PassportNumberRx.IsMatch(text)) warnings.Add(ReminderWarning.PassportNumber);

        var markers = CodeMarkers(text);
        return new ReminderValidation(errors, warnings, markers.Count > 0, markers);
    }

    /// <summary>Words and numbers of the owner's own text (placeholders cut out) that look like an access code: the owner must confirm a dialog (ЮР39-4).</summary>
    public static List<string> CodeMarkers(string template)
    {
        var markers = new List<string>();
        foreach (var line in Normalize(template).Split('\n'))
        {
            var owner = PlaceholderRx.Replace(line, " ");
            foreach (Match m in CodeWordRx.Matches(owner)) AddMarker(markers, m.Value.ToLowerInvariant());
            foreach (Match m in DigitsRx.Matches(owner)) AddMarker(markers, m.Value.Trim());
        }
        return markers;
    }

    private static void AddMarker(List<string> markers, string marker)
    {
        if (!markers.Contains(marker)) markers.Add(marker);
    }

    // ── render ──

    /// <summary>
    /// The text of one channel. <paramref name="pushTextEnabled"/> matters for <see cref="ReminderMode.Push"/> only: the template reaches a push when the owner switched it on
    /// AND changed the template; otherwise the fixed push of cycle 37.
    /// </summary>
    public static ReminderRender Render(string? template, ReminderFacts facts, ReminderMode mode, bool pushTextEnabled = false)
    {
        if (mode == ReminderMode.Push && (template is null || !pushTextEnabled)) return new ReminderRender(FixedPush, [], true);
        if (mode == ReminderMode.Messenger && template is null) return new ReminderRender(LegacyMessenger(facts), [], false);

        var source = Normalize(template ?? Default);
        for (var shrink = 0; shrink <= ShrinkOrder.Length; shrink++)
        {
            var shrunk = ShrinkOrder.Take(shrink).ToHashSet(StringComparer.Ordinal);
            var (body, dropped) = Compose(source, facts, mode, shrunk, dropLinkLines: false);
            var result = Finish(body, dropped, facts, mode, final: shrink == ShrinkOrder.Length, source, shrunk);
            if (result is not null) return result;
        }
        throw new InvalidOperationException("unreachable");
    }

    /// <summary>The messenger text of cycle 37, byte for byte (template NULL).</summary>
    public static string LegacyMessenger(ReminderFacts f) =>
        StayNotificationTexts.Messenger(NotificationType.StayGuestArrivalReminder,
            new StayTextFacts(f.CompanyName, f.HouseName, f.CheckInDate, f.CheckOutDate, f.Nights, 0, f.DueAtCheckInRub, f.Address, f.CompanyPhone,
                null, null, null, null, f.CheckInTime, null),
            f.BookingUrl, f.UnsubscribeUrl);

    private static ReminderRender? Finish(
        string body, List<ReminderDroppedLine> dropped, ReminderFacts f, ReminderMode mode, bool final, string source, HashSet<string> shrunk)
    {
        switch (mode)
        {
            case ReminderMode.Push:
            {
                if (body.Length == 0) return new ReminderRender(FixedPush, dropped, true);
                return new ReminderRender(CutTo(body, PushMaxLength), dropped, false);
            }
            case ReminderMode.Page:
            {
                if (body.Length <= PageMaxLength) return new ReminderRender(body, dropped, false);
                return final ? new ReminderRender(CutTo(body, PageMaxLength), dropped, false) : null;
            }
            default:
            {
                var tail = string.IsNullOrEmpty(f.UnsubscribeUrl) ? string.Empty : $"\n\nОтписаться от сообщений: {f.UnsubscribeUrl}";
                var linkLine = body.Contains(f.BookingUrl, StringComparison.Ordinal) ? string.Empty : (body.Length == 0 ? $"Бронь: {f.BookingUrl}" : $"\nБронь: {f.BookingUrl}");
                var full = body + linkLine + tail;
                if (full.Length <= MessengerMaxLength) return new ReminderRender(full, dropped, false);
                if (!final) return null;

                // The link and the unsubscribe line are never cut: the body is cut to what is left, the link goes on its own last line (§39.11.4 п. 6).
                var (plain, droppedPlain) = Compose(source, f, ReminderMode.Messenger, shrunk, dropLinkLines: true);
                var link = $"Бронь: {f.BookingUrl}";
                var budget = MessengerMaxLength - link.Length - 1 - tail.Length;
                var cut = budget > 0 ? CutTo(plain, budget) : string.Empty;
                var text = (cut.Length == 0 ? link : $"{cut}\n{link}") + tail;
                return new ReminderRender(text, droppedPlain, false);
            }
        }
    }

    /// <summary>Splits the template into lines, drops the ones that cannot appear in the mode, substitutes the values and collapses blank lines.</summary>
    private static (string Body, List<ReminderDroppedLine> Dropped) Compose(
        string source, ReminderFacts facts, ReminderMode mode, HashSet<string> shrunk, bool dropLinkLines)
    {
        var dropped = new List<ReminderDroppedLine>();
        var kept = new List<string>();
        var lines = source.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var tokens = PlaceholderRx.Matches(line).Select(m => m.Value).Where(ByToken.ContainsKey).ToList();

            if (tokens.Any(t => !Allowed(ByToken[t], mode))) { dropped.Add(new ReminderDroppedLine(i + 1, line, ReminderDropReason.ForbiddenPlaceholder)); continue; }
            if (dropLinkLines && tokens.Contains("{СсылкаНаБронь}")) continue;
            if (tokens.Any(t => string.IsNullOrWhiteSpace(ByToken[t].Value(facts)))) { dropped.Add(new ReminderDroppedLine(i + 1, line, ReminderDropReason.EmptyValue)); continue; }
            if (mode == ReminderMode.Push && PushFilter(line) is { } reason) { dropped.Add(new ReminderDroppedLine(i + 1, line, reason)); continue; }

            kept.Add(PlaceholderRx.Replace(line, m =>
            {
                if (!ByToken.TryGetValue(m.Value, out var spec)) return m.Value;
                var value = spec.Value(facts)!;
                return shrunk.Contains(m.Value) && value.Length > 20 ? value[..20] + "…" : value;
            }));
        }
        return (Collapse(kept), dropped);
    }

    private static bool Allowed(Spec s, ReminderMode mode) => mode switch
    {
        ReminderMode.Messenger => s.Messenger,
        ReminderMode.Page => s.Page,
        _ => s.Push
    };

    /// <summary>
    /// ЮР39-3 — the HARD filter of a push line: it looks only at the owner's own words (the placeholders are cut out, so a time «14:00» or a date never drops a line).
    /// The order of the reasons is the contract's: Digits → Link → Email → Phone → CodeWord.
    /// </summary>
    public static ReminderDropReason? PushFilter(string line)
    {
        var owner = PlaceholderRx.Replace(line, " ");
        if (DigitsRx.IsMatch(owner)) return ReminderDropReason.Digits;
        if (LinkRx.IsMatch(owner)) return ReminderDropReason.Link;
        if (EmailRx.IsMatch(owner)) return ReminderDropReason.Email;
        if (PhoneRx.IsMatch(owner)) return ReminderDropReason.Phone;
        if (CodeWordRx.IsMatch(owner)) return ReminderDropReason.CodeWord;
        return null;
    }

    private static string Collapse(List<string> lines)
    {
        var sb = new StringBuilder();
        var previousBlank = true; // also trims the leading blank lines
        foreach (var line in lines)
        {
            var blank = line.Trim().Length == 0;
            if (blank && previousBlank) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(blank ? string.Empty : line);
            previousBlank = blank;
        }
        return sb.ToString().TrimEnd('\n', ' ');
    }

    private static string ServicesText(ReminderFacts f) => f.Sessions.Count == 0
        ? string.Empty
        : string.Join("; ", f.Sessions.Select(s => $"{s.ServiceName}, {ServiceTimeFormat.Guest(s.BusinessDate, s.StartMinute, s.Hours)}"));

    /// <summary>Cuts a text to <paramref name="max"/> characters, ending with «…», never in the middle of a surrogate pair.</summary>
    public static string CutTo(string text, int max)
    {
        if (text.Length <= max) return text;
        var take = max - 1;
        if (take > 0 && char.IsHighSurrogate(text[take - 1])) take--;
        return text[..Math.Max(0, take)] + "…";
    }

    /// <summary>The sample the owner sees in the preview when no booking is chosen (a constant of the server).</summary>
    public static ReminderFacts Sample(DateOnly today, string companyName) => new(
        companyName, "Дом у леса", today.AddDays(1), today.AddDays(4), 3, "14:00", "12:00", "Анна", "ул. Лесная, 5", "+7 900 000-00-00", 12500,
        "https://dom.ezbook.ru/b/пример", null, [new ReminderSessionFact("Баня", today.AddDays(1), 1320, 3)]);
}
