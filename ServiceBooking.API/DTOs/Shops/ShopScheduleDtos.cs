using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.Shops;

// ARCHITECTURE_CYCLE24.md §449–§453, API_CONTRACT_CYCLE24.md §472–§477, contracts/cycle24/openapi.yaml (the source of truth for the shape).
// Times of day travel as "HH:mm" strings (shop-local, 5-minute step); moments are UTC. Text fields of INPUT records are nullable on
// purpose: the controller answers a missing value with the contract's own Russian 400 string instead of the generic sentence.

// ── Working hours ────────────────────────────────────────────────────────────────────────────────────────────────

public record TimeIntervalInput(string? Start, string? End);

public record TimeIntervalDto(string Start, string End, bool CrossesMidnight);

/// <summary>DayOfWeek is nullable so a missing field is a 400 "Неизвестный день недели", not a silent Sunday.</summary>
public record WorkingDayInput(DayOfWeek? DayOfWeek, List<TimeIntervalInput>? Intervals);

public record WorkingHoursInput(List<WorkingDayInput>? Days);

public record WorkingDayDto(DayOfWeek DayOfWeek, string DayLabel, List<TimeIntervalDto> Intervals, string Text);

public record WorkingHoursDto(bool IsSet, List<WorkingDayDto> Days);

public record SpecialDayInput(bool IsClosed, List<TimeIntervalInput>? Intervals, bool ConfirmConflicts = false);

public record SpecialDayDto(DateOnly Date, string Label, bool IsClosed, List<TimeIntervalDto> Intervals, string Text);

// ── Pickup settings, acceptance, status ──────────────────────────────────────────────────────────────────────────

public record PickupSettingsDto(bool AsapEnabled, bool ScheduledEnabled, int SlotStepMinutes, int PreorderDays, int MinPrepMinutes);

public record AcceptanceInput(ShopAcceptanceMode Mode, PauseDuration? Pause);

public record ShopAcceptanceDto(
    ShopAcceptanceMode Mode, DateTime? PausedUntilUtc, string StatusText, DateTime? ChangedAtUtc, string? ChangedByName, string? ChangedText);

public record ShopOpenStateDto(bool IsOpen, string Text, DateTime? OpensAtUtc, DateTime? ClosesAtUtc);

public record AsapOptionDto(bool Available, DateTime? ReadyAtUtc, string? Text);

public record OrderLimitDto(int Used, int? Limit, string MonthLabel, OrderLimitWarningLevel WarningLevel, string? Text);

public record SetupChecklistItemDto(string Code, string Text, bool Done);

public record ShopOrderingStatusDto(
    bool AcceptingOrders, ShopNotAcceptingCode? NotAcceptingCode, string? CustomerText, string? OwnerText, ShopOpenStateDto OpenState,
    ShopAcceptanceDto Acceptance, AsapOptionDto? Asap, bool ScheduledAvailable, OrderLimitDto OrderLimit, bool WorkingHoursSet);

// ── Slots and the pickup choice ──────────────────────────────────────────────────────────────────────────────────

public record PickupDateDto(DateOnly Date, string Label, bool HasSlots, string? ReasonText);

public record PickupOptionsDto(bool AsapEnabled, bool ScheduledEnabled, AsapOptionDto? Asap, List<PickupDateDto> Dates);

public record PickupSlotDto(DateTime StartUtc, DateTime EndUtc, string Label);

public record PickupSlotsDto(DateOnly Date, string Label, List<PickupSlotDto> Slots, AsapOptionDto? Asap, string? ReasonText);

public record PickupSelectionInput(PickupKind Kind, DateOnly? Date, DateTime? SlotStartUtc);

public record PickupProblemDto(OrderRefusalCode Code, string Message);

public record OrderPickupDto(
    PickupKind Kind, DateOnly Date, DateTime StartUtc, DateTime? EndUtc, DateTime DueUtc, string Text, bool IsPreorder, bool IsOverdue);

// ── Availability: soldOut, weekdays, daily menus ─────────────────────────────────────────────────────────────────

public record SoldOutDto(SoldOutScope Scope, DateOnly? Date, string Text);

public record CategoryWeekdaysInput(List<DayOfWeek>? Weekdays);

public record DailyMenuDayDto(DateOnly Date, string Label, bool HasMenu, int ProductCount);

public record DailyMenuCalendarDto(DateOnly From, DateOnly To, List<DailyMenuDayDto> Days);

public record DailyMenuProductDto(Guid ProductId, string Name, string? CategoryName, bool IsPublished, bool InMenu, bool AllowedByWeekdays);

public record DailyMenuDto(DateOnly Date, string Label, bool Exists, List<Guid> ProductIds, List<DailyMenuProductDto> Products);

public record DailyMenuInput(List<Guid>? ProductIds);

public record DailyMenuCopyInput(DateOnly? SourceDate);

// ── Notifications of a shop ──────────────────────────────────────────────────────────────────────────────────────

public record ShopChannelStatusDto(
    Guid ChannelId, NotificationTransport Transport, string? PhoneMasked, string StateText, bool IsConnected, bool Funded, string FundingText);

public record ShopNotificationSettingsDto(
    bool StaffPushEnabled, bool CustomerWebPushEnabled, bool CustomerMessengerEnabled, NotificationDeliveryMode DeliveryMode,
    NotificationTransport PriorityTransport, bool MessengerAvailable, string? MessengerUnavailableText, bool PlatformPushEnabled,
    List<ShopChannelStatusDto> Channels);

public record ShopNotificationSettingsInput(
    bool StaffPushEnabled, bool CustomerWebPushEnabled, bool CustomerMessengerEnabled, NotificationDeliveryMode? DeliveryMode,
    NotificationTransport? PriorityTransport);
