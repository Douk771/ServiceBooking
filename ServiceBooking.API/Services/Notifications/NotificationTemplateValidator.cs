using System.Text.RegularExpressions;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications;

public readonly record struct TemplateValidationResult(bool IsValid, string? Error)
{
    public static readonly TemplateValidationResult Ok = new(true, null);
    public static TemplateValidationResult Fail(string error) => new(false, error);
}

/// <summary>
/// Five rules over an OWNER-EDITED template body (raw, with placeholders still in — validated before
/// <see cref="NotificationTemplateRenderer"/> ever touches it), API_CONTRACT_CYCLE4.md §29.2. There is
/// no moderation on the unofficial gateway this cycle sends through (§21 p.4 of the architecture), so
/// this is the platform's only defence against an owner writing something the account could get banned
/// for sending, or a phishing-shaped link.
/// </summary>
public static partial class NotificationTemplateValidator
{
    private const int MaxLength = 1000;

    /// <summary>The one domain a link in a template is allowed to point at.</summary>
    public const string OwnDomain = "ezbook.ru";

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"(https?://\S+|www\.\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    public static TemplateValidationResult Validate(string? body, NotificationType type)
    {
        if (string.IsNullOrEmpty(body) || body.Length > MaxLength)
            return TemplateValidationResult.Fail("Текст должен быть от 1 до 1000 символов");

        foreach (Match match in PlaceholderPattern().Matches(body))
        {
            if (!TemplatePlaceholders.AllTokens.Contains(match.Value))
                return TemplateValidationResult.Fail(
                    $"Неизвестный плейсхолдер {match.Value}. Допустимы: {string.Join(", ", TemplatePlaceholders.AllTokens)}");
        }

        foreach (Match match in UrlPattern().Matches(body))
        {
            if (!IsOwnDomainUrl(match.Value))
                return TemplateValidationResult.Fail("Внешние ссылки в сообщениях запрещены");
        }

        var withoutPlaceholders = PlaceholderPattern().Replace(body, string.Empty);
        if (string.IsNullOrWhiteSpace(withoutPlaceholders))
            return TemplateValidationResult.Fail("Текст не может состоять из одних подстановок");

        if (type == NotificationType.Reminder &&
            (!body.Contains("{Дата}", StringComparison.Ordinal) || !body.Contains("{Время}", StringComparison.Ordinal)))
            return TemplateValidationResult.Fail("В напоминании обязательны {Дата} и {Время}");

        return TemplateValidationResult.Ok;
    }

    private static bool IsOwnDomainUrl(string urlLike)
    {
        var candidate = urlLike.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + urlLike : urlLike;
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
               (uri.Host.Equals(OwnDomain, StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith("." + OwnDomain, StringComparison.Ordinal));
    }
}
