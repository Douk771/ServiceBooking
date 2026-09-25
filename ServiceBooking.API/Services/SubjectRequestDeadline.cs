using ServiceBooking.API.Controllers;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services;

/// <summary>
/// TD-03-bis — срок ответа на обращение субъекта ПО ВИДУ обращения (LEGAL_REVIEW_CYCLE16.md §2.4,
/// требование О5). До цикла 16 <c>SubjectRequestsController</c> считал единый срок
/// <c>SubjectRequestOptions.ResponseWorkingDays</c> = 10 для всех пяти видов; для уточнения и удаления
/// это ПОЗЖЕ нормы, то есть оператор по построению работал с просрочкой.
///
/// 🔴 Почему это стало критичным именно в цикле 16: TD-03 закрывает самостоятельную реализацию прав
/// через личный кабинет для аккаунта с неподтверждённым номером, и эта форма становится для такого
/// субъекта ЕДИНСТВЕННЫМ путём. Лечение утечки, работающее с нарушением срока, заменило бы одно
/// нарушение другим.
///
/// Нормы (152-ФЗ, проверено 25.09.2026):
/// <list type="bullet">
/// <item>ч. 1 ст. 20 — доступ к своим данным: 10 рабочих дней;</item>
/// <item>ч. 3 ст. 20 — уточнение и уничтожение: 7 рабочих дней;</item>
/// <item>ч. 5 ст. 21 — отзыв согласия: 30 дней, КАЛЕНДАРНЫХ, не рабочих.</item>
/// </list>
/// Для жалобы (<see cref="SubjectRequestKind.Complaint"/>) специального срока нет — остаётся
/// настраиваемый дефолт, он же общий срок рассмотрения обращения.
/// </summary>
public static class SubjectRequestDeadline
{
    /// <summary>Рабочих дней на доступ — ч. 1 ст. 20.</summary>
    public const int AccessWorkingDays = 10;

    /// <summary>Рабочих дней на уточнение и уничтожение — ч. 3 ст. 20.</summary>
    public const int RectificationWorkingDays = 7;

    /// <summary>Календарных дней на отзыв согласия — ч. 5 ст. 21.</summary>
    public const int ConsentWithdrawalCalendarDays = 30;

    /// <summary>
    /// Возвращает момент истечения срока и то количество рабочих дней, которое будет НАЗВАНО заявителю.
    /// 🔴 Второе значение выводится из первого, а не задаётся отдельно: расхождение между обещанным
    /// сроком и фактически записанным <c>DueAtUtc</c> само по себе вводит в заблуждение (§2.4).
    /// </summary>
    public static (DateTime DueAtUtc, int DisclosedWorkingDays) For(
        SubjectRequestKind kind, DateTime nowUtc, SubjectRequestOptions options)
    {
        switch (kind)
        {
            case SubjectRequestKind.Rectification:
            case SubjectRequestKind.Erasure:
                return (WorkingDays.Add(nowUtc, RectificationWorkingDays), RectificationWorkingDays);

            case SubjectRequestKind.ConsentWithdrawal:
                // Норма календарная, а поле ответа исторически в рабочих днях. Считаем дату по норме,
                // а называем заявителю ровно столько рабочих дней, сколько в эту дату укладывается —
                // так обещание остаётся истинным и согласованным с DueAtUtc.
                var dueAtUtc = nowUtc.AddDays(ConsentWithdrawalCalendarDays);
                return (dueAtUtc, CountWorkingDays(nowUtc, dueAtUtc));

            case SubjectRequestKind.Access:
                return (WorkingDays.Add(nowUtc, AccessWorkingDays), AccessWorkingDays);

            default:
                // Жалоба и всё, что появится позже: настраиваемый дефолт. Новый вид обращения, которому
                // норма задаёт собственный срок, обязан получить здесь явную ветку.
                var fallback = options.ResponseWorkingDays;
                return (WorkingDays.Add(nowUtc, fallback), fallback);
        }
    }

    /// <summary>Полных рабочих дней (Пн–Пт) между двумя моментами — тот же упрощающий подход, что и в
    /// <see cref="WorkingDays"/>: праздники не учитываются, а значит названный срок может оказаться
    /// только строже фактического, но не мягче.</summary>
    private static int CountWorkingDays(DateTime from, DateTime to)
    {
        var count = 0;
        var cursor = from;
        while (cursor < to)
        {
            cursor = cursor.AddDays(1);
            if (cursor > to) break;
            if (cursor.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                count++;
        }
        return count;
    }
}
