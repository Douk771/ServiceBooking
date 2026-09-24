using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.PhoneVerification;

/// <summary>
/// Every Russian sentence the SITE (not the bot — see <c>Max.MaxBotTexts</c>) shows about phone
/// verification, in one place (ARCHITECTURE_CYCLE14.md §147.4, conforming to cycle 9's convention №6
/// "the server composes wording, the frontend never does"). API_CONTRACT_CYCLE14.md §162-§172 pins these
/// strings verbatim — this class IS that contract's implementation, changing one changes the other.
/// </summary>
public static class PhoneVerificationTexts
{
    public const string SubsystemUnavailable = "Подтверждение телефона сейчас недоступно.";
    public const string InvalidPhoneFormat = "Введите номер телефона в формате +7 (900) 000-00-00";
    public const string TooManyStartAttempts = "Слишком много попыток подтверждения. Попробуйте позже.";
    public const string TooManyChangePhoneAttempts = "Слишком много попыток смены номера. Попробуйте позже.";

    public const string RegisterSessionInvalid =
        "Подтверждение номера не подходит к этой регистрации. Подтвердите номер ещё раз.";

    public const string ChangePhoneNeedsVerification =
        "На этом номере уже есть записи. Подтвердите его через MAX — тогда номер можно будет сменить.";

    public const string ChangePhoneSubsystemDisabled =
        "Сейчас сменить номер на этот нельзя: подтверждение номера на платформе пока не работает.";

    /// <summary>§165's table, verbatim — the ready-made human sentence for
    /// <c>GET /phone-verification/sessions/{id}</c>'s <c>message</c> field. <c>PhoneMismatch</c> needs
    /// the two masked numbers interpolated by the caller (<see cref="PhoneMismatch"/>) since this method
    /// has no numbers to work with; every other reason is a fixed string.</summary>
    public static string? ForFailureReason(PhoneVerificationFailureReason? reason) => reason switch
    {
        null => null,
        PhoneVerificationFailureReason.PayloadUnknown => "Ссылка устарела. Получите новую",
        PhoneVerificationFailureReason.PayloadExpired => "Ссылка устарела. Получите новую",
        PhoneVerificationFailureReason.PayloadAlreadyUsed => "Ссылка устарела. Получите новую",
        PhoneVerificationFailureReason.PayloadLinkedToAnotherAccount => "Эта ссылка уже используется в другом аккаунте MAX",
        PhoneVerificationFailureReason.SignatureMismatch => "Не удалось проверить контакт. Попробуйте ещё раз",
        PhoneVerificationFailureReason.ContactNotOwnedBySender => "Поделиться можно только собственным контактом",
        PhoneVerificationFailureReason.NoPhoneInContact => "В присланном контакте нет номера телефона",
        // §165: composed with the two masked numbers — see PhoneMismatch(sentMasked, expectedMasked)
        // when both are known; this fallback covers the (never expected in practice) case where the
        // reader only has the reason code and no numbers to interpolate.
        PhoneVerificationFailureReason.PhoneMismatch => "Присланный номер не совпадает с номером на сайте",
        PhoneVerificationFailureReason.MaxAccountLimitReached =>
            "С этого аккаунта MAX уже подтверждены три номера. Больше подтверждений с него сделать нельзя.",
        PhoneVerificationFailureReason.SessionCancelled => null, // §165: screen already stopped showing the session
        PhoneVerificationFailureReason.SubsystemDisabled => SubsystemUnavailable,
        _ => null,
    };

    /// <summary>§165's exact <c>PhoneMismatch</c> wording, which — unlike every other reason — names both
    /// numbers (masked).</summary>
    public static string PhoneMismatch(string sentMasked, string expectedMasked) =>
        $"Вы поделились номером {sentMasked}, а на сайте введён {expectedMasked}.";
}
