using System.Text.RegularExpressions;

namespace ServiceBooking.TestKit;

/// <summary>
/// Ключ текущего тестового прогона. Вычисляется один раз внутри процесса (статика),
/// см. ARCHITECTURE_CYCLE8.md §67. Переменная окружения — только переопределение (нужна CI,
/// чтобы ключ совпадал с тем, что печатается в логах шага сборки).
/// </summary>
public static class TestRunKey
{
    private const string EnvironmentVariableName = "SERVICEBOOKING_TEST_RUN_KEY";

    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        var external = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        return string.IsNullOrWhiteSpace(external)
            ? Guid.NewGuid().ToString("N")[..8]
            : Normalize(external);
    }

    /// <summary>
    /// Приводит внешний ключ к каноническому формату (8 hex-символов, нижний регистр) и
    /// проверяет его форму. Выделена из <see cref="Resolve"/>, чтобы быть юнит-тестируемой
    /// независимо от переменных окружения процесса.
    /// </summary>
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new TestSafetyException(
                $"[sb-test] Отказ: значение {EnvironmentVariableName} пустое. " +
                "Ожидался формат: 8 шестнадцатеричных символов (0-9a-f).");

        var trimmed = value.Trim().ToLowerInvariant();
        if (!Regex.IsMatch(trimmed, "^[0-9a-f]{8}$"))
        {
            throw new TestSafetyException(
                $"[sb-test] Отказ: значение {EnvironmentVariableName}=\"{value}\" не соответствует формату. " +
                "Ожидался формат: 8 шестнадцатеричных символов (0-9a-f). " +
                "Что сделать: задайте, например, SERVICEBOOKING_TEST_RUN_KEY=a3f19c7b, либо не задавайте переменную вовсе.");
        }

        return trimmed;
    }
}
