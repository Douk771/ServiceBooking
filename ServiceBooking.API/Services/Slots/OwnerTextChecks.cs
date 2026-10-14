using System.Text.RegularExpressions;

namespace ServiceBooking.API.Services.Slots;

public sealed record OwnerTextResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>ARCHITECTURE_CYCLE42.md §42.8.2 — checks of the owner's free text (resource description, item names): hard errors and soft warnings.</summary>
public static class OwnerTextChecks
{
    public const string ForbiddenWords = "ForbiddenWords";
    public const string ErrorText = "Не используйте слова «задаток», «невозвратный», «депозит»";

    public static readonly IReadOnlyDictionary<string, string> WarningTexts = new Dictionary<string, string>
    {
        ["CancellationTermsInText"] = "Условия отмены задаёт выбранный шаблон — другие условия в описании не действуют",
        ["MandatoryExtraCharge"] = "Все обязательные платежи должны быть в цене часов — не требуйте доплат на месте",
        ["HealthClaim"] = "Не обещайте лечебного или оздоровительного эффекта",
        ["Passport"] = "Не просите гостя прислать фото паспорта",
        ["CardNumber"] = "Похоже на номер карты — не публикуйте данные карт",
        ["PassportNumber"] = "Похоже на паспортные данные — не публикуйте их"
    };

    private const RegexOptions Opts = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled;

    private static readonly Regex Forbidden = new("задат[ок]|невозвратн|депозит", Opts);

    // Order of the list is the order of the warnings in the answer.
    private static readonly (string Code, Regex Pattern)[] Soft =
    {
        ("CancellationTermsInText", new(@"штраф|неустойк|неявк|не\s+возвращ|невозврат(?!н)|условия\s+отмены|отмен\w*\s+(?:за|менее|позже)", Opts)),
        ("MandatoryExtraCharge", new(@"(?<!\p{L})доплат|(?<!\p{L})сверх(?!у)|за\s+человека|за\s+каждого|обязательн|оплат\w*\s+на\s+месте", Opts)),
        ("HealthClaim", new(@"лечебн|лечени|лечит|оздоров|целебн|исцел|детокс|противопоказаний\s+нет|иммунитет|полезно\s+при", Opts)),
        ("Passport", new(@"паспорт", Opts)),
        ("CardNumber", new(@"(?<!\d)\d(?:[\s-]?\d){12,18}(?!\d)", Opts)),
        ("PassportNumber", new(@"(?<!\d)\d{4}\s\d{6}(?!\d)|серия\s*\d{4}\s*номер\s*\d{6}", Opts)),
    };

    public static OwnerTextResult Check(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(Array.Empty<string>(), Array.Empty<string>());
        var errors = Forbidden.IsMatch(text) ? new[] { ForbiddenWords } : Array.Empty<string>();
        var warnings = Soft.Where(s => s.Pattern.IsMatch(text)).Select(s => s.Code).ToArray();
        return new(errors, warnings);
    }
}
