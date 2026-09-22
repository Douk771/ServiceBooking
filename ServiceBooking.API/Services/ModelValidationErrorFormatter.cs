using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace ServiceBooking.API.Services;

/// <summary>
/// Turns ASP.NET Core's automatic model-state validation (missing/invalid query or body fields caught
/// by [ApiController] before the action even runs) into the SAME wire format the rest of the product
/// uses for "осознанные" 4xx: a bare text/plain string, in Russian, aimed at a human — not the
/// ValidationProblemDetails object (application/problem+json, machine field names in `errors`) MVC
/// produces by default.
///
/// Why this exists (cycle 6 contract finding): the frontend's error reader
/// (frontend/src/utils/authError.ts) treats anything that isn't a string as "no message" and falls back
/// to a generic "Проверьте введённые данные" — exactly the failure mode that blocked US-60. A manual
/// `BadRequest("...")` in a controller already produces a string and was never affected; this factory
/// closes the SAME hole for the validation ASP.NET Core performs automatically, so both paths now agree.
///
/// Registered via `ApiBehaviorOptions.InvalidModelStateResponseFactory` in Program.cs. Does not affect
/// which requests get a 400 or which don't — only the shape of the body when one already would have
/// happened.
/// </summary>
public static class ModelValidationErrorFormatter
{
    // Keyed by the model-binding field name as ASP.NET Core reports it (case-insensitive): the last
    // segment of the ModelState key, e.g. "companyId" from a query parameter or "dto.Phone" from a body
    // property. Deliberately covers only names that actually appear in DTOs/action parameters across the
    // product (LoginDto, BookingsController query params, UpdateSubscriptionDto, ...) — an unmapped name
    // falls back to a field-agnostic sentence rather than leaking the raw C# identifier to the user.
    private static readonly Dictionary<string, string> FieldLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["phone"] = "номер телефона",
        ["password"] = "пароль",
        ["companyId"] = "компания",
        ["masterId"] = "мастер",
        ["serviceId"] = "услуга",
        ["serviceIds"] = "услуги",
        ["date"] = "дата",
        ["from"] = "дата начала диапазона",
        ["to"] = "дата окончания диапазона",
        ["ownerUserId"] = "владелец аккаунта",
        ["planConfigId"] = "тариф",
        ["paidUntil"] = "дата окончания подписки",
        ["isActive"] = "признак активности подписки",
        ["firstName"] = "имя",
        ["lastName"] = "фамилия",
        ["email"] = "email",
    };

    public static IActionResult BuildResponse(ActionContext context)
    {
        var fieldMessages = new List<string>();
        var hadUnmappedField = false;

        foreach (var (key, entry) in context.ModelState)
        {
            if (entry.Errors.Count == 0) continue;

            var fieldName = key.Contains('.') ? key[(key.LastIndexOf('.') + 1)..] : key;
            var reason = DescribeReason(entry.Errors[0].ErrorMessage);

            if (FieldLabels.TryGetValue(fieldName, out var label))
                fieldMessages.Add($"{label} — {reason}");
            else
                hadUnmappedField = true;
        }

        string message;
        if (fieldMessages.Count == 0)
        {
            // Either every failing field was unmapped, or ModelState carried a top-level/body error with
            // no field name at all (e.g. malformed JSON) — either way, say SOMETHING human instead of
            // an empty sentence.
            message = "Проверьте правильность заполнения формы: часть обязательных данных отсутствует или указана в неверном формате.";
        }
        else
        {
            message = "Проверьте данные запроса: " + string.Join("; ", fieldMessages) +
                       (hadUnmappedField ? "; есть и другие некорректно заполненные поля" : "") + ".";
        }

        return new ContentResult
        {
            StatusCode = StatusCodes.Status400BadRequest,
            Content = message,
            ContentType = "text/plain; charset=utf-8"
        };
    }

    // ASP.NET Core's own ModelState error messages are English and reference the CLR type ("The value ''
    // is not valid.", "The JSON value could not be converted to System.Guid."). We don't surface them to
    // the user at all — just classify them into one of two Russian reasons a person can act on.
    private static string DescribeReason(string englishErrorMessage) =>
        englishErrorMessage.Contains("required", StringComparison.OrdinalIgnoreCase)
            ? "обязательное поле, оно не заполнено"
            : "заполнено в неверном формате";
}
