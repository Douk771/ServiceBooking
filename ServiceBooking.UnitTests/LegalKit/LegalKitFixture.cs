using System.Text.Json;

namespace ServiceBooking.UnitTests.LegalKit;

/// <summary>Builds a minimal, valid legal-drafts-shaped directory (all five document types, all six
/// uiTexts, the channel-offer appendix with its marker) in a throwaway temp directory — every LegalKit
/// unit test starts from one of these rather than touching the real <c>legal-drafts/</c> (which
/// legal-counsel owns, T0, and which this session is explicitly not allowed to modify).</summary>
internal static class LegalKitFixture
{
    public const string DefaultTermsOwnerHtml = "<p id=\"offer-channel\">Owner terms {{ПОЧТА_ДЛЯ_ОБРАЩЕНИЙ}}</p>";

    public static string CreateSourceDir(string? termsOwnerHtml = null, bool includeMarker = true, string? privacyHtml = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "legalkit-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var manifest = new
        {
            documents = new object[]
            {
                new { type = "Privacy", version = "2026-09-22-draft", effectiveFrom = "2026-09-22", isDraft = true, changeKind = "Material", gate = "Global", title = "Privacy", file = "privacy.html" },
                new { type = "TermsClient", version = "2026-09-22-draft", effectiveFrom = "2026-09-22", isDraft = true, changeKind = "Material", gate = "Global", title = "Terms", file = "terms.html" },
                new { type = "TermsOwner", version = "2026-09-22-draft", effectiveFrom = "2026-09-22", isDraft = true, changeKind = "Material", gate = "OwnerScope", title = "TermsOwner", file = "terms-owner.html" },
                new
                {
                    type = "PdnConsent", version = "2026-09-22-draft", effectiveFrom = "2026-09-22", isDraft = true, changeKind = "Material", gate = "None", title = "Pdn", file = "pdn.html",
                    purposes = new object[]
                    {
                        new { key = "ProviderDelivery", title = "P" },
                        new { key = "WorkPhotos", title = "W" },
                        new { key = "HealthData", title = "H" },
                    },
                },
                new { type = "ChannelRiskNotice", version = "2026-09-22-draft", effectiveFrom = "2026-09-22", isDraft = true, changeKind = "Material", gate = "None", title = "Risk", file = "risk.html" },
            },
            uiTexts = new object[]
            {
                new { key = "BookingNotice", version = "2026-09-22-draft", isDraft = true, file = "booking.html" },
                new { key = "TemplateAdWarning", version = "2026-09-22-draft", isDraft = true, file = "ad.html" },
                new { key = "UnsubscribePage", version = "2026-09-22-draft", isDraft = true, file = "unsub.html" },
                new { key = "PhotoConsent", version = "2026-09-22-draft", isDraft = true, file = "photo.html" },
                new { key = "HealthDataConsent", version = "2026-09-22-draft", isDraft = true, file = "health.html" },
                new { key = "GuardianConfirmation", version = "2026-09-22-draft", isDraft = true, file = "guardian.html" },
            },
            appendices = new { TermsOwner = new[] { "09-channel-offer.html" } },
        };

        File.WriteAllText(Path.Combine(dir, "legal.json"), JsonSerializer.Serialize(manifest));

        foreach (var file in new[] { "terms", "pdn", "risk", "booking", "ad", "unsub", "photo", "health", "guardian" })
            File.WriteAllText(Path.Combine(dir, $"{file}.html"), $"<p>{{{{ПОЧТА_ДЛЯ_ОБРАЩЕНИЙ}}}} {file}</p>");

        File.WriteAllText(Path.Combine(dir, "privacy.html"), privacyHtml ?? "<p>{{ПОЧТА_ДЛЯ_ОБРАЩЕНИЙ}} privacy</p>");

        File.WriteAllText(Path.Combine(dir, "terms-owner.html"), termsOwnerHtml ?? DefaultTermsOwnerHtml);

        var offerBody = "<p>Offer appendix body {{НДС_ОГОВОРКА}}</p>";
        var offerContent = includeMarker
            ? $"<p>{{{{ВЕРСИЯ_ДОКУМЕНТА}}}} header</p>\n<!-- APPENDIX-BODY-START -->\n{offerBody}"
            : $"<p>{{{{ВЕРСИЯ_ДОКУМЕНТА}}}} header, no marker</p>\n{offerBody}";
        File.WriteAllText(Path.Combine(dir, "09-channel-offer.html"), offerContent);

        return dir;
    }

    public static void Delete(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    public static string ValidValuesJson() => """
        {
          "values": {
            "НАИМЕНОВАНИЕ_ОПЕРАТОРА": "ООО Тест",
            "ИНН_ОПЕРАТОРА": "1234567890",
            "ЮРИДИЧЕСКИЙ_АДРЕС": "г. Тест, ул. Тестовая, 1",
            "ПОЧТОВЫЙ_АДРЕС": "г. Тест, ул. Тестовая, 1",
            "ПОЧТА_ДЛЯ_ОБРАЩЕНИЙ": "test@example.com",
            "ТЕЛЕФОН_ОПЕРАТОРА": "+7 000 000-00-00",
            "ОТВЕТСТВЕННЫЙ_ЗА_ОБРАБОТКУ": "Иванов И.И.",
            "ПОЧТА_ОТВЕТСТВЕННОГО": "dpo@example.com",
            "НОМЕР_УВЕДОМЛЕНИЯ_РКН": "00-00-000000",
            "ДАТА_УВЕДОМЛЕНИЯ_РКН": "01.01.2026",
            "СРОК_ОТВЕТА_НА_ОБРАЩЕНИЕ": "10 рабочих дней",
            "НДС_ОГОВОРКА": "НДС не облагается (УСН)"
          }
        }
        """;

    /// <summary>Same 12 values, minus the two Roskomnadzor-registry keys — the shape a real
    /// <c>legal.values.json</c> has BEFORE the registry entry appears (ARCHITECTURE_CYCLE11.md §103.3):
    /// still valid, because those two keys are optional.</summary>
    public static string ValidValuesJsonWithoutRknKeys() => """
        {
          "values": {
            "НАИМЕНОВАНИЕ_ОПЕРАТОРА": "ООО Тест",
            "ИНН_ОПЕРАТОРА": "1234567890",
            "ЮРИДИЧЕСКИЙ_АДРЕС": "г. Тест, ул. Тестовая, 1",
            "ПОЧТОВЫЙ_АДРЕС": "г. Тест, ул. Тестовая, 1",
            "ПОЧТА_ДЛЯ_ОБРАЩЕНИЙ": "test@example.com",
            "ТЕЛЕФОН_ОПЕРАТОРА": "+7 000 000-00-00",
            "ОТВЕТСТВЕННЫЙ_ЗА_ОБРАБОТКУ": "Иванов И.И.",
            "ПОЧТА_ОТВЕТСТВЕННОГО": "dpo@example.com",
            "СРОК_ОТВЕТА_НА_ОБРАЩЕНИЕ": "10 рабочих дней",
            "НДС_ОГОВОРКА": "НДС не облагается (УСН)"
          }
        }
        """;

    /// <summary>The exact real-world sentence from legal-drafts/01-privacy-policy.html — one self-contained
    /// &lt;li&gt; naming both Roskomnadzor-registry placeholders, flanked by unrelated bullets that must
    /// survive untouched whether or not the registry bullet does.</summary>
    public const string PrivacyHtmlWithRknBullet =
        "<ul>\n" +
        "<li>bullet before {{ПОЧТА_ДЛЯ_ОБРАЩЕНИЙ}}</li>\n" +
        "<li>сведения об Операторе Сервиса внесены в реестр операторов, осуществляющих обработку " +
        "персональных данных: регистрационный номер {{НОМЕР_УВЕДОМЛЕНИЯ_РКН}}, уведомление направлено " +
        "{{ДАТА_УВЕДОМЛЕНИЯ_РКН}} (статья 22 152-ФЗ).</li>\n" +
        "<li>bullet after</li>\n" +
        "</ul>";
}
