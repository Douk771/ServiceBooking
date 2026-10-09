using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.Stays;

// ARCHITECTURE_CYCLE39.md §39.2–§39.11, API_CONTRACT_CYCLE39.md §39.20–§39.36, contracts/cycle39/openapi.yaml (the source of truth for the shape).
// Time of day of a service is MINUTES from 00:00 of the business date (360 = 06:00, 1560 = 02:00 of the next day). Labels are assembled on the server:
// a guest reads calendar dates (ЮР39-8), the staff the form of SPEC §4.9. Numeric inputs that can be absent are nullable on purpose: the Russian sentence of the
// contract answers a missing value, not the binder.

public enum ServiceChargeKind { Service, Item }

public enum ServiceSessionKind { InBooking, Standalone }

public enum DateSource { Template, Override, Closed }

public enum ServiceDayBarKind { Session, Buffer, CarryOverBuffer }

// ── common ──
public record ServiceWindowInput(int StartMinute, int EndMinute);

public record ServiceWindowDto(int StartMinute, int EndMinute, string Label);

public record ServiceTimeDto(DateOnly BusinessDate, int StartMinute, int EndMinute, int Hours, DateTime StartUtc, DateTime EndUtc, string Label);

public record HourPriceDto(int StartMinute, string Label, int PriceRub);

public record ItemSelectionInput(Guid? ItemId, int Quantity);

public record ServiceSelectionInput(DateOnly? BusinessDate, int? StartMinute, int? Hours, List<ItemSelectionInput>? Items);

public record ServiceItemPublicDto(Guid Id, string Name, int PriceRub, int MaxPerSession);

public record SessionItemDto(string Name, int UnitPriceRub, int Quantity, int AmountRub);

public record ServiceChargeLineDto(ServiceChargeKind Kind, string Label, int Quantity, int UnitPriceRub, int AmountRub);

public record ServiceRefundViewDto(ServiceRefundKind Kind, int? RefundAtLeastRub, int MaxDeductionRub, string Text);

public record ServiceProblemDto(ServiceRefusalCode Code, string Message);

// ── 409 bodies ──
public record ServiceRefusalDto(ServiceRefusalCode Code, string Message, string? ReasonCode = null, ServiceQuoteDto? Quote = null);

public record ServiceOrderGuestConflictDto(string Code, string Message, PublicServiceOrderDto Order);

public record StayBookingGuestConflictDto(string Code, string Message, PublicStayBookingDto Booking);

public record ServiceStaffConflictDto(string Code, string Message, StaffServiceSessionCardDto Session);

public record StaysServiceConflictDto(
    string Code, string Message, PriceRuleDto? ConflictingRule = null, List<string>? Markers = null, string? NoticeText = null);

// ── anonymous: pages, dates, starts, quote ──
public record PublicServiceSummaryDto(
    Guid Id, string Slug, string Name, string Url, string? CoverUrl, string? CoverThumbUrl, int? PriceFromRub, int MinHours, bool CanOrderWithoutStay,
    bool AvailableForHouseBookings, Guid ServiceId = default, List<ServiceItemPublicDto>? Items = null);

public record ServicePhotoDto(Guid Id, string Url, string ThumbnailUrl, int Position);

public record PriceTableRowDto(string Label, int PriceRub);

public record PublicServiceStandaloneDto(
    bool Ordering, int? PrepayPercent, StayServiceCancellationPolicy? CancellationPolicy, int? CancellationBoundaryHours, string? CancellationSummary,
    int HoldMinutes, string? NotOrderingText);

public record PublicServiceCompanyDto(string Slug, string Name, string? Phone, string? LogoUrl, string Url);

public record PublicServiceDto(
    Guid Id, string Slug, string Name, string? Description, List<ServicePhotoDto> Photos, int MinHours, int MaxHours, int StepMinutes,
    List<PriceTableRowDto> PriceTable, List<ServiceItemPublicDto> Items, int? BufferMinutes, PublicServiceStandaloneDto Standalone,
    PublicServiceCompanyDto Company, ProviderPublicDto? Provider, bool AcceptingBookings, string? NotAcceptingText, bool Available, string? NotAvailableText,
    DateOnly Today, string TimeZoneId);

public record AvailabilityDayDto(DateOnly BusinessDate, string Label, bool HasStarts);

public record ServiceAvailabilityDto(Guid ServiceId, DateOnly Today, List<AvailabilityDayDto> Days);

public record HoursOptionDto(int Hours, string EndLabel);

public record StartDto(int StartMinute, DateTime StartUtc, string Label, int MaxHours, List<HoursOptionDto> Options);

public record ServiceStartsDto(
    Guid ServiceId, DateOnly BusinessDate, string DateLabel, int MinHours, int MaxHours, List<StartDto> Starts, StartsReason? Reason, string? NoStartsText,
    List<ServiceItemPublicDto>? Items = null);

public record PublicServiceQuoteInput(DateOnly? BusinessDate, int? StartMinute, int? Hours, List<ItemSelectionInput>? Items, Guid? HouseId, DateOnly? CheckIn, DateOnly? CheckOut);

public record ServiceQuoteDto(
    bool Ok, List<ServiceProblemDto> Problems, ServiceTimeDto? Time, List<HourPriceDto> HourPrices, List<ServiceChargeLineDto> Lines, int ServiceAmountRub,
    int ItemsAmountRub, int TotalRub, int? PrepayPercent, int PrepayRub, int DueOnSiteRub, int? HoldMinutes, string? CancellationSummary, string? PayOnSiteText,
    bool AcceptingBookings, string? NotAcceptingText);

public record CreateServiceOrderInput(
    DateOnly? BusinessDate, int? StartMinute, int? Hours, List<ItemSelectionInput>? Items, string? GuestName, string? GuestPhone, string? Comment,
    bool NotifyByMessenger, int ExpectedTotalRub, Guid? IdempotencyKey, string? CaptchaToken);

public record CreateServiceOrderResponse(string Token, string OrderUrl, PublicServiceOrderDto Order);

// ── the order through the guest's eyes ──
public record OrderServiceRefDto(string Name, string Url, string? CoverUrl);

public record OrderCompanyRefDto(string Name, string? Phone, string Url, string? Address, string? YandexMapsUrl, string? TwoGisUrl);

public record OrderCancellationDto(StayServiceCancellationPolicy Policy, string Summary, bool CanCancel, ServiceRefundViewDto Refund, string? CannotCancelText);

public record OrderNotificationsDto(WebPushInfoDto WebPush, bool MessengerSelected);

public record PublicServiceOrderDto(
    StayBookingStatus Status, string DisplayStatus, string StatusText, DateTime ServerTimeUtc, DateTime? HoldExpiresAtUtc, OrderServiceRefDto Service,
    OrderCompanyRefDto Company, ProviderFullDto? Provider, ServiceTimeDto Time, List<SessionItemDto> Items, List<ServiceChargeLineDto> Lines,
    List<HourPriceDto> HourPrices, int ServiceAmountRub, int ItemsAmountRub, int TotalRub, int? PrepayPercent, int PrepayRub, int DueOnSiteRub,
    PaymentInstructionsDto? Payment, DateTime? PaymentConfirmedAtUtc, List<PaymentProofDto> PaymentProofs, ProofRulesDto Proofs, OrderCancellationDto Cancellation,
    string? GuestName, string? GuestPhoneMasked, string? Comment, string? StatusReason, string? OutcomeText, OrderNotificationsDto Notifications,
    List<string> AvailableActions);

// ── services of a stay (the booking page) ──
public record BookingServiceOptionDto(Guid Id, string Name, string Url, string? CoverThumbUrl, int? PriceFromRub, int MinHours, List<AvailabilityDayDto> Dates,
    Guid ServiceId = default, List<ServiceItemPublicDto>? Items = null);

public record BookingServicesDto(bool CanAdd, string? CannotAddText, string? Hint, List<BookingServiceOptionDto> Services);

public record AddSessionInput(
    Guid? ServiceId, DateOnly? BusinessDate, int? StartMinute, int? Hours, List<ItemSelectionInput>? Items, int ExpectedTotalRub, Guid? IdempotencyKey);

public record PublicBookingSessionDto(
    Guid Id, string ServiceName, string? ServiceUrl, ServiceTimeDto Time, List<SessionItemDto> Items, int TotalRub, StayServiceSessionState State, string StateText,
    bool CanCancel, string? CannotCancelText, bool AddedByStaff, string? AddedByStaffText, string? StatusReason);

public record ArrivalReminderSnapshotDto(string Text, DateTime SentAtUtc);

public record BookingServicesBlockDto(bool CanAdd, string? CannotAddText, string? Hint);

// ── the cabinet: services ──
public record ServiceListItemDto(
    Guid Id, string Slug, string Name, bool IsPublished, bool IsArchived, int Position, string? CoverThumbUrl, List<string> PublishProblems, bool HasSessions);

public record ServiceCreateInput(string? Name);

public record ServiceSetupInput(
    string? Name, string? Slug, int MinHours, int MaxHours, int StepMinutes, int BufferMinutes, bool ShowBufferToGuests, int MinLeadMinutes, int? StandalonePrepayPercent,
    StayServiceCancellationPolicy CancellationPolicy, int CancellationBoundaryHours, bool AvailableForHouseBookings, int? Capacity = null);

public record ServiceContentInput(string? Description);

public record BoundaryRangeDto(int Min, int Max);

public record ServiceManageDto(
    Guid Id, string Slug, string Name, string? Description, int MinHours, int MaxHours, int StepMinutes, int BufferMinutes, bool ShowBufferToGuests,
    int MinLeadMinutes, int? StandalonePrepayPercent, StayServiceCancellationPolicy CancellationPolicy, int CancellationBoundaryHours,
    BoundaryRangeDto CancellationBoundaryRange, bool AvailableForHouseBookings, bool IsPublished, bool IsArchived, int Position, List<ServicePhotoDto> Photos,
    string PublicUrl, List<string> PublishProblems, bool HasSessions, int? Capacity = null, List<string>? ContentWarnings = null);

public record PriceRuleDto(Guid Id, int DaysMask, int FromHour, int ToHour, int PriceRub, string Label);

public record PriceRuleInput(int DaysMask, int FromHour, int ToHour, int PriceRub);

public record PriceMatrixCellDto(int Hour, string Label, int? PriceRub, bool InWindowWithoutPrice);

public record PriceMatrixRowDto(int DayOfWeek, string Label, List<PriceMatrixCellDto> Cells);

public record PriceRulesDto(List<PriceRuleDto> Rules, List<PriceMatrixRowDto> Matrix);

public record ServiceItemDto(Guid Id, string Name, int PriceRub, int MaxPerSession, bool IsActive, int Position, List<string>? Warnings = null);

public record ServiceItemInput(string? Name, int PriceRub, int MaxPerSession, bool IsActive, bool? ConfirmRestricted = null);

// ── the cabinet: schedule ──
public record WeeklyDayDto(int DayOfWeek, string Label, List<ServiceWindowDto> Windows);

public record WeeklyScheduleDto(List<WeeklyDayDto> Days);

public record WeeklyDayInput(int DayOfWeek, List<ServiceWindowInput>? Windows);

public record WeeklyScheduleInput(List<WeeklyDayInput>? Days);

public record OutsideSessionDto(Guid SessionId, string Label, string? HouseName);

public record ServiceMonthDayDto(
    DateOnly BusinessDate, DateSource Source, List<ServiceWindowDto> Windows, string? Comment, string? UpdatedByName, DateTime? UpdatedAtUtc);

public record ScheduleSaveResultDto(WeeklyScheduleDto? Weekly, ServiceMonthDayDto? Day, List<OutsideSessionDto> OutsideSessions, string? WarningText);

public record ServiceMonthDto(Guid ServiceId, string Month, DateOnly Today, List<ServiceMonthDayDto> Days);

public record DateOverrideInput(bool Closed, List<ServiceWindowInput>? Windows, string? Comment);

// ── the cabinet: sessions ──
public record ServiceDayAxisDto(int FromMinute, int ToMinute, int MidnightMinute);

public record ServiceDayBarDto(
    ServiceDayBarKind Kind, Guid? SessionId, int StartMinute, int EndMinute, string Label, string? State, string? StateText, bool NeedsAction);

public record ServiceDayServiceDto(Guid Id, string Name, bool IsPublished, bool Closed, List<ServiceWindowDto> Windows, List<ServiceDayBarDto> Bars);

public record ServiceDayDto(DateOnly Date, string Label, DateOnly Today, ServiceDayAxisDto Axis, List<ServiceDayServiceDto> Services);

public record StaffServiceSessionListItemDto(
    Guid Id, ServiceSessionKind Kind, Guid ServiceId, string ServiceName, ServiceTimeDto Time, string? HouseName, Guid? BookingId, string? GuestName,
    string? GuestPhone, StayBookingStatus? OrderStatus, StayServiceSessionState State, string StatusText, int TotalRub, int PrepayRub, DateTime? HoldExpiresAtUtc,
    DateTime? FirstProofUploadedAtUtc, DateTime CreatedAtUtc);

public record StaffServiceSessionPage(List<StaffServiceSessionListItemDto> Items, int TotalCount, int Page, int PageSize);

public record SessionBookingRefDto(Guid Id, string HouseName, string StatusText);

public record SessionAddedByDto(string Kind, string? Name, StayServiceRequestBasis? RequestBasis, string Text);

public record StaffServiceSessionCardDto(
    Guid Id, int Version, ServiceSessionKind Kind, Guid ServiceId, string ServiceName, ServiceTimeDto Time, List<HourPriceDto> HourPrices, List<SessionItemDto> Items,
    List<ServiceChargeLineDto> Lines, int ServiceAmountRub, int ItemsAmountRub, int TotalRub, int? PrepayPercent, int PrepayRub, int DueOnSiteRub, int BufferMinutes,
    string PreparedUntilLabel, StayServiceSessionState State, StayBookingStatus? OrderStatus, string? DisplayStatus, string StatusText, DateTime? HoldExpiresAtUtc,
    SessionBookingRefDto? Booking, string? GuestName, string? GuestPhone, string? Comment, SessionAddedByDto AddedBy, StayServiceCancellationPolicy? CancellationPolicy,
    string? OwnerCancelRefundText, List<PaymentProofDto> PaymentProofs, PaymentConfirmedDto? PaymentConfirmed, DateTime? PaymentProofsPurgedAtUtc, string? StatusReason,
    bool IsManual, List<string> AvailableActions, List<StayBookingEventDto> Events);

public record StaffServiceQuoteInput(Guid? ServiceId, DateOnly? BusinessDate, int? StartMinute, int? Hours, List<ItemSelectionInput>? Items, Guid? BookingId);

public record StaffAddSessionInput(
    Guid? ServiceId, DateOnly? BusinessDate, int? StartMinute, int? Hours, List<ItemSelectionInput>? Items, StayServiceRequestBasis? RequestBasis, Guid? IdempotencyKey);

public record ManualServiceOrderInput(
    Guid? ServiceId, DateOnly? BusinessDate, int? StartMinute, int? Hours, List<ItemSelectionInput>? Items, string? GuestName, string? GuestPhone, string? Comment,
    StayServiceRequestBasis? RequestBasis, Guid? IdempotencyKey);

public record StaffBookingSessionDto(
    Guid Id, int Version, string ServiceName, ServiceTimeDto Time, List<SessionItemDto> Items, int TotalRub, StayServiceSessionState State, string StateText,
    string AddedByText, bool CanCancel, string? StatusReason);

// ── the reminder ──
public record ReminderPlaceholderDto(string Token, string Description, bool InMessenger, bool OnPage, bool InPush);

public record ReminderLimitsDto(int TemplateMaxLength, int MessengerMaxLength, int PageMaxLength, int PushMaxLength);

public record NoticeRefDto(string Key, string Version);

public record ArrivalReminderSettingsDto(
    bool Enabled, string Time, string? Template, bool IsDefault, string EffectiveTemplate, string DefaultTemplate, bool PushTextEnabled,
    List<ReminderPlaceholderDto> Placeholders, ReminderLimitsDto Limits, NoticeRefDto OwnerNotice, NoticeRefDto PushNotice, List<ReminderWarning> Warnings);

public record ArrivalReminderInput(
    string? Time, string? Template, bool PushTextEnabled, bool ConfirmCodeMarkers, string? OwnerNoticeVersion, string? PushNoticeVersion);

public record ArrivalReminderPreviewInput(string? Template, bool PushTextEnabled, Guid? BookingId);

public record PreviewTextDto(string Text, int Length);

public record DroppedLineDto(int Line, string Text, ReminderDropReason Reason);

public record PushPreviewDto(string Text, int Length, bool UsesFixedText, List<DroppedLineDto> Dropped);

public record ReminderErrorDto(ReminderErrorCode Code, string Message);

public record ArrivalReminderPreviewDto(
    PreviewTextDto Messenger, PreviewTextDto Page, PushPreviewDto Push, List<ReminderWarning> Warnings, List<ReminderErrorDto> Errors, bool ConfirmationRequired,
    List<string> Markers);

public record ArrivalReminderChangeDto(
    DateTime ChangedAtUtc, string ChangedByName, string PreviousTime, string NewTime, string? PreviousTemplate, string? NewTemplate, bool PreviousPushText,
    bool NewPushText, bool CodeMarkersConfirmed, string? CodeMarkersHit);

// ── changed DTOs of cycle 37 (new appended parts) ──
public record ServiceLinkDto(string Name, string Url, Guid ServiceId = default);

public record StayQuoteServiceDto(int Index, Guid ServiceId, bool Ok, ServiceQuoteDto Quote);

public record StayServiceSelectionInput(Guid? ServiceId, DateOnly? BusinessDate, int? StartMinute, int? Hours, List<ItemSelectionInput>? Items);

public record BoardServiceDto(Guid Id, string Name, bool IsPublished, bool IsArchived);

public record BoardServiceCellDto(Guid ServiceId, DateOnly BusinessDate, int Count, string FirstStartLabel, string? CrossesMidnightLabel, bool NeedsAction);

public record ScheduleItemDto(string Name, int Quantity);

public record ScheduleSessionDto(
    Guid SessionId, string ServiceName, string TimeLabel, string PreparedUntilLabel, string? HouseName, string? GuestName, List<ScheduleItemDto> Items,
    string? Comment, bool PaymentUnconfirmed);

public record ServiceAdminStatsDto(int ServicesCount, int SessionsLast30Days);
