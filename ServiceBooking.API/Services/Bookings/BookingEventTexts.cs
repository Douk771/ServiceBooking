using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Bookings;

/// <summary>
/// ARCHITECTURE_CYCLE10.md §105/§122.1: pure text assembly, no DB access — the single place that
/// builds the Russian `title` and `actor.label` strings for the history endpoint, so the frontend never
/// needs (and must never build) a second copy of these formulations (same convention as
/// NotificationTexts / ChannelPresentation).
/// </summary>
public static class BookingEventTexts
{
    public static string Title(BookingEventKind kind) => kind switch
    {
        BookingEventKind.Created => "Запись создана",
        BookingEventKind.Rescheduled => "Запись перенесена",
        BookingEventKind.Cancelled => "Запись отменена",
        BookingEventKind.Completed => "Запись отмечена выполненной",
        BookingEventKind.NoShow => "Клиент не пришёл",
        BookingEventKind.PaymentMarked => "Оплата отмечена",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static string ActorLabel(BookingEventKind kind, BookingActorKind actorKind, string? name, UserRole? role)
    {
        switch (actorKind)
        {
            case BookingActorKind.SuperAdmin:
                return "Администратор платформы";
            case BookingActorKind.System:
                return "Система";
            case BookingActorKind.Staff:
            {
                var roleLabel = role == UserRole.CompanyOwner ? "владелец" : "мастер";
                var verb = kind switch
                {
                    BookingEventKind.Created => "Записал",
                    BookingEventKind.Rescheduled => "Перенёс",
                    BookingEventKind.Cancelled => "Отменил",
                    BookingEventKind.Completed => "Отметил выполненной",
                    BookingEventKind.NoShow => "Отметил неявку",
                    BookingEventKind.PaymentMarked => "Отметил оплату",
                    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
                };
                return $"{verb} {Display(name)} ({roleLabel})";
            }
            case BookingActorKind.Client:
            {
                var verb = kind switch
                {
                    BookingEventKind.Created => "Записался",
                    BookingEventKind.Rescheduled => "Перенёс",
                    BookingEventKind.Cancelled => "Отменил",
                    _ => "Изменил",
                };
                return $"{verb} клиент {Display(name)}";
            }
            case BookingActorKind.Guest:
            {
                var verb = kind == BookingEventKind.Created ? "Записался" : "Изменил";
                return $"{verb} гость {Display(name)}";
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(actorKind), actorKind, null);
        }
    }

    private static string Display(string? name) => string.IsNullOrWhiteSpace(name) ? "неизвестный" : name;
}
