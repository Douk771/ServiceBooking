namespace ServiceBooking.API.Services;

/// <summary>Texts of request-parsing rejections (ARCHITECTURE_CYCLE29.md §29.4, API_CONTRACT_CYCLE29.md §29.23).</summary>
public static class RequestTexts
{
    public const string MalformedFormFieldName = "Не удалось прочитать данные формы. Проверьте имена полей запроса.";
    public const string MalformedQueryParameterName = "Не удалось прочитать параметры запроса. Проверьте имена параметров.";
}
