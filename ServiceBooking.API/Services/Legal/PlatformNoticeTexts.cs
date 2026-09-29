using System.Globalization;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Legal;

/// <summary>
/// ARCHITECTURE_CYCLE20.md §404.3, LEGAL_REVIEW_CYCLE20.md §11 (task L3, verbatim legal-counsel text) —
/// the templates for the four "server assembles the text" notice kinds, plus the acknowledge button's
/// own copy. DO NOT REWRITE ANY STRING BELOW WITHOUT legal-counsel (cycle-18 convention, §5): every
/// template is a direct quote from the legal review, byte-exact including punctuation.
///
/// <see cref="TemplateVersion"/> is stamped onto every <see cref="Core.Entities.PlatformNotice.TemplateVersion"/>
/// row this class produces (LEGAL_REVIEW_CYCLE20.md §11 rule 1) — this is the FIRST implementation of
/// this class, so there is no earlier version to preserve.
/// </summary>
public static class PlatformNoticeTexts
{
    public const string TemplateVersion = "2026-09-29";

    public const string AcknowledgeButtonText = "Я ознакомился";
    public const string AcknowledgeCaption = "Это подтверждает только то, что вы прочитали сообщение, а не согласие с ним.";

    /// <summary>LEGAL_REVIEW_CYCLE20.md line 844 (Т20-02 п. 6).</summary>
    public static (string Title, string Body) BuildPriceChange(string planName, decimal oldPricePerMonth, decimal newPricePerMonth, DateOnly effectiveFrom)
    {
        var dateStr = FormatDate(effectiveFrom);
        var title = RenderWithTruncatedName($"Тариф «{{название}}»: новая цена с {dateStr}", "{название}", planName);
        var body =
            $"С {dateStr} меняется цена «{planName}»: было {FormatMoney(oldPricePerMonth)} ₽, станет {FormatMoney(newPricePerMonth)} ₽ за месяц. " +
            "Уже оплаченный период пересчитан не будет. Если новая цена вам не подходит, до " + dateStr +
            " вы вправе отказаться от тарифа или опции либо от договора с возвратом платы за неиспользованные дни " +
            "(пункты 6.13.9, 6.13.10 и 16.4 Соглашения с компанией).";
        return EnsureNoBraces(title, body);
    }

    /// <summary>LEGAL_REVIEW_CYCLE20.md §11.1/§11.2 — one template per <paramref name="documentType"/>;
    /// only <c>Privacy</c>/<c>TermsClient</c>/<c>TermsOwner</c> are valid (enforced by the caller via
    /// <see cref="PlatformNoticeRules.ValidateAudienceForKind"/> before this is ever called).</summary>
    public static (string Title, string Body) BuildTermsChange(LegalDocumentType documentType, string changesSummary, DateOnly effectiveFrom)
    {
        var dateStr = FormatDate(effectiveFrom);
        var list = TrimChangesSummary(changesSummary);

        var (title, body) = documentType switch
        {
            LegalDocumentType.TermsOwner => (
                $"Соглашение с компанией: новая редакция с {dateStr}",
                $"С {dateStr} действует новая редакция Соглашения с компанией. Что меняется: {list}. " +
                "Текст новой редакции — по ссылке ниже. Если вы не согласны, до " + dateStr +
                " вы вправе отказаться от договора с возвратом платы за неиспользованные дни (пункт 16.4)."),

            LegalDocumentType.TermsClient => (
                $"Пользовательское соглашение: новая редакция с {dateStr}",
                $"С {dateStr} действует новая редакция Пользовательского соглашения. Что меняется: {list}. " +
                "Текст новой редакции — по ссылке ниже. Новая редакция не имеет обратной силы: к записям и иным " +
                $"отношениям, возникшим до {dateStr}, применяется прежняя редакция (пункт 21.3 Пользовательского " +
                "соглашения). Если вы не согласны с новой редакцией, вы вправе в любой момент перестать " +
                "пользоваться сервисом и (или) удалить личный кабинет — без каких-либо платежей и иных " +
                "неблагоприятных последствий (пункт 21.4 Пользовательского соглашения)."),

            LegalDocumentType.Privacy => (
                $"Политика обработки персональных данных: новая редакция с {dateStr}",
                $"С {dateStr} действует новая редакция Политики обработки персональных данных. Что меняется: {list}. " +
                "Текст новой редакции — по ссылке ниже. Изменение Политики не отменяет и не ограничивает ваших прав " +
                "как субъекта персональных данных и не заменяет согласия на обработку: если для новой цели " +
                "понадобится ваше согласие, мы запросим его отдельно (пункты 18.3 и 18.4 Политики). Узнать, какие " +
                "ваши данные мы обрабатываем, потребовать их уточнения или уничтожения, отозвать согласие можно в " +
                "порядке раздела 15 Политики."),

            _ => throw new ArgumentOutOfRangeException(nameof(documentType), documentType, "Not a valid TermsChange documentType."),
        };

        return EnsureNoBraces(title, body);
    }

    /// <summary>LEGAL_REVIEW_CYCLE20.md §11.3 (Т20-07, D3 п. 8.8). <paramref name="removalDateMsk"/> is
    /// the deletion date by Europe/Moscow (§404.3's own "today" rule), not UTC.</summary>
    public static (string Title, string Body) BuildPhotoRemoved(string companyName, DateOnly removalDateMsk)
    {
        var dateStr = FormatDate(removalDateMsk);
        var title = RenderWithTruncatedName("Фотография компании «{компания}» удалена по просьбе изображённого на ней человека", "{компания}", companyName);
        var body =
            $"{dateStr} мы удалили из сервиса одну из фотографий компании «{companyName}». К нам обратился человек, " +
            "изображённый на снимке (или его законный представитель), и попросил его убрать. Обнародовать и " +
            "использовать изображение человека можно только с его согласия (пункт 1 статьи 152.1 Гражданского " +
            "кодекса РФ), поэтому по такому обращению мы удаляем фотографию, не дожидаясь вашего ответа (пункт 8.8 " +
            "Соглашения с компанией). Сведения о том, кто обратился, мы не сообщаем. Остальные фотографии компании " +
            "не затронуты. Пожалуйста, не загружайте этот снимок повторно без согласия изображённого на нём " +
            "человека. Если вы считаете, что фотография удалена по ошибке — например, на ней нет людей, — " +
            "напишите нам по адресу для обращений, указанному в пункте 4.1 Соглашения с компанией: мы рассмотрим " +
            "обращение и ответим. До ответа снимок не восстанавливается.";
        return EnsureNoBraces(title, body);
    }

    private static string FormatDate(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    /// <summary>§404.3 "суммы — ru-RU без копеек, если они нулевые": whole amounts print with no
    /// fractional part, a fractional amount prints with exactly two digits.</summary>
    private static string FormatMoney(decimal amount)
    {
        var ruRu = CultureInfo.GetCultureInfo("ru-RU");
        return amount == decimal.Truncate(amount) ? amount.ToString("N0", ruRu) : amount.ToString("N2", ruRu);
    }

    /// <summary>LEGAL_REVIEW_CYCLE20.md §11 rule 2, "{перечень}" row: trailing '.', ';', '!' and
    /// whitespace are stripped (repeatedly, in any combination) because the template already places a
    /// period right after the placeholder.</summary>
    private static string TrimChangesSummary(string changesSummary)
    {
        var s = changesSummary.TrimEnd();
        while (s.Length > 0 && (s[^1] is '.' or ';' or '!' or ' '))
            s = s[..^1].TrimEnd();
        return s;
    }

    /// <summary>§11 rule 4: only the substituted name/company is shortened (with a trailing "…") when the
    /// rendered title would exceed <see cref="PlatformNoticeRules.MaxTitleLength"/> — the rest of the
    /// title template is never cut.</summary>
    private static string RenderWithTruncatedName(string templateWithPlaceholder, string placeholder, string name)
    {
        var full = templateWithPlaceholder.Replace(placeholder, name);
        if (full.Length <= PlatformNoticeRules.MaxTitleLength) return full;

        var baseLength = templateWithPlaceholder.Length - placeholder.Length;
        var allowedNameLength = Math.Max(0, PlatformNoticeRules.MaxTitleLength - baseLength - 1); // -1 reserves room for the ellipsis
        var truncatedName = name.Length > allowedNameLength ? name[..allowedNameLength] + "…" : name;
        return templateWithPlaceholder.Replace(placeholder, truncatedName);
    }

    /// <summary>§11 rule 3: "в сохранённом снимке не должно остаться ни одного {/}" — fail loud rather
    /// than silently persist a template with an unresolved placeholder.</summary>
    private static (string Title, string Body) EnsureNoBraces(string title, string body)
    {
        if (title.Contains('{') || title.Contains('}') || body.Contains('{') || body.Contains('}'))
            throw new InvalidOperationException("PlatformNoticeTexts left an unresolved placeholder in the rendered text.");
        return (title, body);
    }
}
