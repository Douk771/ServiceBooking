using FluentAssertions;
using ServiceBooking.API.Services.Bookings;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

// ARCHITECTURE_CYCLE10.md §113.4: BookingEventTexts is pure text assembly (no DB, no HTTP) — every
// combination of the six event kinds by the five actor kinds it's asked to cover.
public class BookingEventTextsTests
{
    [Theory]
    [InlineData(BookingEventKind.Created, "Запись создана")]
    [InlineData(BookingEventKind.Rescheduled, "Запись перенесена")]
    [InlineData(BookingEventKind.Cancelled, "Запись отменена")]
    [InlineData(BookingEventKind.Completed, "Запись отмечена выполненной")]
    [InlineData(BookingEventKind.NoShow, "Клиент не пришёл")]
    [InlineData(BookingEventKind.PaymentMarked, "Оплата отмечена")]
    public void Title_ReturnsExpectedRussianText(BookingEventKind kind, string expected)
    {
        BookingEventTexts.Title(kind).Should().Be(expected);
    }

    [Fact]
    public void ActorLabel_SuperAdmin_IsAlwaysTheSameRegardlessOfKind()
    {
        foreach (var kind in Enum.GetValues<BookingEventKind>())
            BookingEventTexts.ActorLabel(kind, BookingActorKind.SuperAdmin, "Игорь Суперадминов", null)
                .Should().Be("Администратор платформы");
    }

    [Fact]
    public void ActorLabel_System_IsAlwaysTheSameRegardlessOfKind()
    {
        foreach (var kind in Enum.GetValues<BookingEventKind>())
            BookingEventTexts.ActorLabel(kind, BookingActorKind.System, null, null).Should().Be("Система");
    }

    [Theory]
    [InlineData(BookingEventKind.Created, UserRole.Master, "Записал Иван Петров (мастер)")]
    [InlineData(BookingEventKind.Rescheduled, UserRole.Master, "Перенёс Иван Петров (мастер)")]
    [InlineData(BookingEventKind.Cancelled, UserRole.Master, "Отменил Иван Петров (мастер)")]
    [InlineData(BookingEventKind.Completed, UserRole.Master, "Отметил выполненной Иван Петров (мастер)")]
    [InlineData(BookingEventKind.NoShow, UserRole.Master, "Отметил неявку Иван Петров (мастер)")]
    [InlineData(BookingEventKind.PaymentMarked, UserRole.Master, "Отметил оплату Иван Петров (мастер)")]
    public void ActorLabel_StaffMaster_UsesMasterRoleLabel(BookingEventKind kind, UserRole role, string expected)
    {
        BookingEventTexts.ActorLabel(kind, BookingActorKind.Staff, "Иван Петров", role).Should().Be(expected);
    }

    [Fact]
    public void ActorLabel_StaffCompanyOwner_UsesOwnerRoleLabel()
    {
        BookingEventTexts.ActorLabel(BookingEventKind.Created, BookingActorKind.Staff, "Анна Смирнова", UserRole.CompanyOwner)
            .Should().Be("Записал Анна Смирнова (владелец)");
    }

    [Theory]
    [InlineData(BookingEventKind.Created, "Записался клиент Анна Иванова")]
    [InlineData(BookingEventKind.Rescheduled, "Перенёс клиент Анна Иванова")]
    [InlineData(BookingEventKind.Cancelled, "Отменил клиент Анна Иванова")]
    public void ActorLabel_Client_UsesClientVerb(BookingEventKind kind, string expected)
    {
        BookingEventTexts.ActorLabel(kind, BookingActorKind.Client, "Анна Иванова", null).Should().Be(expected);
    }

    [Fact]
    public void ActorLabel_Guest_UsesGuestVerb()
    {
        BookingEventTexts.ActorLabel(BookingEventKind.Created, BookingActorKind.Guest, "Пётр", null)
            .Should().Be("Записался гость Пётр");
    }

    [Fact]
    public void ActorLabel_MissingName_FallsBackToPlaceholderInsteadOfBlank()
    {
        BookingEventTexts.ActorLabel(BookingEventKind.Created, BookingActorKind.Guest, null, null)
            .Should().Be("Записался гость неизвестный");
    }
}
