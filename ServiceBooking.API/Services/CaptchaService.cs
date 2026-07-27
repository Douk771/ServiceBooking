using System.Net.Http.Json;

namespace ServiceBooking.API.Services;

/// <summary>
/// Bot protection for guest booking via Yandex SmartCaptcha server-side validation.
/// See https://yandex.cloud/docs/smartcaptcha/concepts/validation — POST form-urlencoded
/// {secret, token, ip} to the validate endpoint; success is <c>status == "ok"</c>.
///
/// Fails closed: a missing/invalid token, an unreachable validation service, or a non-OK status all
/// return false. When no server key is configured it skips validation in Development/Testing (dev
/// convenience) but rejects in Production — so guest booking can never silently run without captcha.
/// </summary>
public class CaptchaService(IConfiguration config, HttpClient httpClient, IHostEnvironment env)
{
    private const string VerifyUrl = "https://smartcaptcha.cloud.yandex.ru/validate";

    /// <summary>
    /// Whether captcha is active for guest booking: a server key is configured, OR we're in Production
    /// (where the absence of a key is a misconfiguration that must block anonymous booking, not open it).
    /// </summary>
    public bool IsEnforced =>
        !string.IsNullOrEmpty(config["SmartCaptcha:SecretKey"]) || env.IsProduction();

    /// <summary>Validates a SmartCaptcha token. Returns true only when verification genuinely passes.</summary>
    public async Task<bool> ValidateAsync(string? token, string? ip = null)
    {
        var secret = config["SmartCaptcha:SecretKey"];

        // No key configured: skip outside Production, fail closed inside it.
        if (string.IsNullOrEmpty(secret))
            return !env.IsProduction();

        if (string.IsNullOrEmpty(token)) return false;

        var form = new List<KeyValuePair<string, string>>
        {
            new("secret", secret),
            new("token", token),
        };
        if (!string.IsNullOrEmpty(ip)) form.Add(new("ip", ip));

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync(VerifyUrl, new FormUrlEncodedContent(form));
        }
        catch (HttpRequestException)
        {
            return false; // validation service unreachable → fail closed
        }

        if (!response.IsSuccessStatusCode) return false;

        var result = await response.Content.ReadFromJsonAsync<SmartCaptchaResponse>();
        return string.Equals(result?.Status, "ok", StringComparison.OrdinalIgnoreCase);
    }

    private record SmartCaptchaResponse(string? Status, string? Message, string? Host);
}
