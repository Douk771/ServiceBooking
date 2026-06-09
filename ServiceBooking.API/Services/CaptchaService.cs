namespace ServiceBooking.API.Services;

public class CaptchaService(IConfiguration config, HttpClient httpClient)
{
    private const string VerifyUrl = "https://www.google.com/recaptcha/api/siteverify";
    private const double MinScore = 0.5;

    public async Task<bool> ValidateAsync(string token)
    {
        var secret = config["Recaptcha:SecretKey"];
        if (string.IsNullOrEmpty(secret)) return true; // skip in dev if not configured

        var response = await httpClient.PostAsync(VerifyUrl,
            new FormUrlEncodedContent([
                new("secret", secret),
                new("response", token)
            ]));

        if (!response.IsSuccessStatusCode) return false;

        var result = await response.Content.ReadFromJsonAsync<RecaptchaResponse>();
        return result is { Success: true } && result.Score >= MinScore;
    }

    private record RecaptchaResponse(bool Success, double Score, string Action, string Hostname);
}
