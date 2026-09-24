namespace ServiceBooking.API.Services.PhoneVerification.Max;

/// <summary>
/// Every Russian sentence the BOT ITSELF sends back in chat (ARCHITECTURE_CYCLE12.md §147.4, П11) — kept
/// separate from <see cref="PhoneVerificationTexts"/> (site wording) because the two audiences and tones
/// differ, exactly as the architecture's file layout (§154) specifies. None of these strings are ever
/// generated text in the "sent an AI-composed message" sense (R1) — they are fixed, reviewed sentences a
/// human wrote, only the interpolated masked phone numbers vary.
/// </summary>
public static class MaxBotTexts
{
    /// <summary>Sent on a successful <c>bot_started</c> — invites the person to share their own contact
    /// card via the platform's native "request_contact" affordance.</summary>
    public const string Greeting =
        "Здравствуйте! Чтобы подтвердить номер телефона, поделитесь своим контактом — нажмите кнопку " +
        "«Отправить контакт» ниже. Отправлять можно только СВОЙ собственный контакт.";

    /// <summary>Same greeting again, for a repeated <c>bot_started</c> from the account already linked to
    /// this session (§145.2's no-op branch) — reassures the person nothing broke, no new payload needed.</summary>
    public const string AlreadyLinkedReminder =
        "Вы уже начали подтверждение этого номера. Поделитесь своим контактом — кнопка «Отправить контакт» ниже.";

    public const string PayloadLinkedToAnotherAccount =
        "Эта ссылка уже используется в другом аккаунте MAX. Получите новую ссылку на сайте.";

    public const string PayloadExpiredOrUnknown =
        "Эта ссылка устарела или не найдена. Вернитесь на сайт и получите новую.";

    public const string SignatureMismatch =
        "Не удалось проверить контакт. Попробуйте отправить его ещё раз.";

    public const string ContactNotOwnedBySender =
        "Поделиться можно только собственным контактом — тем, что закреплён за вашим аккаунтом MAX.";

    public const string NoPhoneInContact =
        "В присланном контакте нет номера телефона. Убедитесь, что в вашей карточке контакта указан номер.";

    public static string PhoneMismatch(string sentMasked, string expectedMasked) =>
        $"Вы поделились номером {sentMasked}, а на сайте введён {expectedMasked}. Проверьте номер на сайте " +
        "или поделитесь контактом с тем номером, что там указан.";

    public const string MaxAccountLimitReached =
        "С этого аккаунта MAX уже подтверждены три номера. Больше подтверждений с него сделать нельзя.";

    public const string Success =
        "Готово! Номер подтверждён. Можете вернуться на сайт — там уже видно результат.";
}
