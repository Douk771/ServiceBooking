using System.Security.Cryptography;
using System.Text;

namespace ServiceBooking.API.Services.Baths;

/// <summary>
/// Terms of the «Бани» trial (T42-10, LEGAL_REVIEW_CYCLE42.md §11.7 + the messenger sentence of Q-L42-4). DRAFT until a lawyer reads it:
/// a separate edition from the «Дома» and salon trials'. The version, the shown text and its hash are recorded in the TrialGrant.
/// </summary>
public static class BathsTrialTerms
{
    public const string Version = "baths-2026-10-09";

    public static string Text(int durationDays) =>
        $"Пробный период линейки «Бани» — {durationDays} дней с момента активации. В это время вы пользуетесь кабинетом и принимаете брони " +
        "без оплаты тарифа. Пробный период линейки «Бани» даётся один раз на один аккаунт и один раз на один подтверждённый номер " +
        "телефона; пробные периоды записи, заказов и «Домов» на него не влияют. Дата окончания показывается при активации и потом " +
        "не меняется. По окончании деньги не списываются и платный тариф сам не подключается: гости не смогут бронировать ваши " +
        "бани, пока вы не выберете тариф. Уже принятые брони, ваши данные и публикации сохраняются; принятые брони вы " +
        "по-прежнему ведёте в кабинете и обязаны исполнить, а если отменяете — вернуть гостю предоплату полностью. О скором " +
        "окончании мы предупреждаем только в кабинете. " +
        "Сообщения гостям через подключённый канал WhatsApp или MAX доступны весь пробный период.";

    public static string Sha256 => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Text(0)))).ToLowerInvariant();
}
