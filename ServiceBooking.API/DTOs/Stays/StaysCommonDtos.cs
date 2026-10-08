using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.Stays;

// ARCHITECTURE_CYCLE37.md §37.2, API_CONTRACT_CYCLE37.md §37.22-§37.31, contracts/cycle37/openapi.yaml. Times of day are strings "HH:mm";
// dates are DateOnly ("YYYY-MM-DD"); money is whole roubles (int, suffix Rub). Enums go out as strings (global JsonStringEnumConverter).

public record DateRangeDto(DateOnly StartDate, DateOnly EndDate);

public record HouseAmenityDto(HouseAmenity Code, string Label);

public record GateDto(bool Accepting, string? ReasonCode, string? ReasonText);

public record ProviderPublicDto(StayProviderStatus Status, string StatusLabel, string? Name, string Inn, string? Ogrn, string? ClaimsAddress);

public record ProviderFullDto(StayProviderStatus Status, string StatusLabel, string Name, string Inn, string? Ogrn, string ClaimsAddress);

public record NightPriceDto(DateOnly Date, int PriceRub);

public record StayChargeLineDto(StayChargeKind Kind, string Label, int Quantity, int UnitPriceRub, int Nights, int AmountRub, bool PrepayEligible);

public record StayRefundViewDto(StayRefundKind Kind, int RefundAtLeastRub, int MaxDeductionRub, string Text);

public record ProblemDto(StayRefusalCode Code, string Message);

// ── 409 bodies ──
public record StayRefusalDto(StayRefusalCode Code, string Message, string? ReasonCode = null, StayQuoteDto? Quote = null, int? ServiceIndex = null);

public record StayGuestConflictDto(string Code, string Message, PublicStayBookingDto Booking);

public record StayStaffConflictDto(string Code, string Message, StaffStayBookingCardDto Booking);

public record BlockBookingConflictDto(Guid BookingId, DateOnly CheckInDate, DateOnly CheckOutDate, string? GuestName, StayBookingStatus Status);

public record StaysConflictDto(string Code, string Message, PricePeriodDto? ConflictingPeriod = null, List<BlockBookingConflictDto>? Conflicts = null);

// ── public ──
public record StayCatalogItemDto(
    Guid HouseId, string Url, string HouseName, string CompanyName, string? CoverUrl, string? CoverThumbUrl, int Capacity,
    int ExtraBedsMax, bool DogsForbidden, string? Address, string? RegistryNumber, int? PriceFromRub, int? TotalRub,
    int? AverageNightRub, int? Nights, bool? AvailableForDates);

public record PublicStaysCompanyDto(
    Guid Id, string Slug, string Name, string? LogoUrl, string? Description, string? Phone, bool Available, string? NotAvailableText,
    bool AcceptingBookings, string? NotAcceptingText, ProviderPublicDto? Provider, List<StayCatalogItemDto> Houses, DateOnly Today,
    List<PublicServiceSummaryDto>? Services = null, bool? AcceptsServiceOrdersWithoutStay = null);

public record HousePhotoDto(Guid Id, string Url, string? ThumbnailUrl, int Position);

public record ExtraBedsDto(bool Enabled, int Max, int PriceRub);

public record PublicRegistryDto(HouseObjectKind ObjectKind, string ObjectKindLabel, string? RegistryNumber, string? RegistryUrl);

public record PublicStayRulesDto(
    string CheckInTime, string CheckOutTime, int MinNights, int MaxNights, int HorizonDays, bool AllowGapFill, bool AllowSameDayCheckIn,
    int HoldMinutes, int PrepayPercent, StayCancellationPolicy CancellationPolicy, string CancellationSummary);

public record PublicCompanyRefDto(string Slug, string Name, string? Phone, string? LogoUrl, string Url);

public record PublicHouseDto(
    Guid Id, string Slug, string Name, string? Description, List<HousePhotoDto> Photos, int Capacity, ExtraBedsDto ExtraBeds,
    bool DogsForbidden, int DogFeeRub, bool HasCot, int CotFeeRub, List<HouseAmenityDto> Amenities, string? Address,
    string? YandexMapsUrl, string? TwoGisUrl, PublicRegistryDto? Registry, PublicCompanyRefDto Company, ProviderPublicDto? Provider,
    PublicStayRulesDto Rules, int? PriceFromRub, bool AcceptingBookings, string? NotAcceptingText, bool Available,
    string? NotAvailableText, DateOnly Today, string TimeZoneId, List<ServiceLinkDto>? ServicesForStay = null);

public record CalendarDayDto(DateOnly Date, CalendarDayState State, int? PriceRub);

public record HouseCalendarDto(
    Guid HouseId, DateOnly Today, DateOnly From, DateOnly To, int MinNights, int MaxNights, bool AllowGapFill, bool AllowSameDayCheckIn,
    DateOnly LastNight, List<CalendarDayDto> Days);

public enum CalendarDayState { Free, MayFreeUp, Occupied, Unavailable }

public record StayQuoteInput(DateOnly? CheckIn, DateOnly? CheckOut, int Adults, int Children, int Dogs, bool NeedCot, List<StayServiceSelectionInput>? Services = null);

public record StayQuoteDto(
    bool Ok, List<ProblemDto> Problems, int Nights, List<NightPriceDto> NightPrices, List<StayChargeLineDto> Lines, int ExtraBeds,
    int TotalRub, int PrepayPercent, int PrepayRub, int DueAtCheckInRub, int AverageNightRub, int HoldMinutes, string CheckInTime,
    string CheckOutTime, StayCancellationPolicy CancellationPolicy, string CancellationSummary, bool AcceptingBookings, string? NotAcceptingText,
    List<StayQuoteServiceDto>? Services = null);

public record CreateStayBookingInput(
    DateOnly? CheckIn, DateOnly? CheckOut, int Adults, int Children, int Dogs, bool NeedCot, string? GuestName, string? GuestPhone,
    string? ArrivalTime, string? Comment, bool NotifyByMessenger, int ExpectedTotalRub, Guid? IdempotencyKey, string? CaptchaToken,
    List<StayServiceSelectionInput>? Services = null);

public record CreateStayBookingResponse(string Token, string BookingUrl, PublicStayBookingDto Booking);

// ── the booking through the guest's eyes ──
public record BookingHouseRefDto(string Name, string Url, string? CoverUrl, string? Address, string? YandexMapsUrl, string? TwoGisUrl);

public record BookingCompanyRefDto(string Name, string? Phone, string Url);

public record PaymentInstructionsDto(string? Details, string? Purpose, int AmountRub);

public record PaymentProofDto(Guid Id, string ContentType, int SizeBytes, DateTime UploadedAtUtc, bool Purged);

public record ProofRulesDto(bool CanAttach, int MaxCount, int MaxBytes, List<string> AcceptedTypes);

public record BookingCancellationDto(StayCancellationPolicy Policy, string Summary, bool CanCancel, StayRefundViewDto Refund, string? CannotCancelText);

public record CheckInInfoDto(string? CompanyText, string? HouseText);

public record WebPushInfoDto(bool Available, string? PublicKey);

public record BookingNotificationsDto(WebPushInfoDto WebPush, bool MessengerSelected);

public record PublicStayBookingDto(
    StayBookingStatus Status, string DisplayStatus, string StatusText, DateTime ServerTimeUtc, DateTime? HoldExpiresAtUtc,
    BookingHouseRefDto House, BookingCompanyRefDto Company, ProviderFullDto? Provider, DateOnly CheckInDate, DateOnly CheckOutDate,
    string CheckInTime, string CheckOutTime, int Nights, int Adults, int Children, int Dogs, bool NeedCot, int ExtraBeds, string? ArrivalTime,
    string? GuestName, string? GuestPhoneMasked, string? Comment, List<StayChargeLineDto> Lines, List<NightPriceDto> NightPrices,
    int TotalRub, int PrepayPercent, int PrepayRub, int DueAtCheckInRub, PaymentInstructionsDto? Payment, DateTime? PaymentConfirmedAtUtc,
    List<PaymentProofDto> PaymentProofs, ProofRulesDto Proofs, BookingCancellationDto Cancellation, string? StatusReason, string? OutcomeText,
    CheckInInfoDto? CheckInInfo, BookingNotificationsDto Notifications, List<string> AvailableActions,
    List<PublicBookingSessionDto>? Sessions = null, BookingServicesBlockDto? ServicesBlock = null, ArrivalReminderSnapshotDto? ArrivalReminder = null);

public record PushKeysInput(string P256dh, string Auth);

public record PushSubscriptionInput(string? Endpoint, PushKeysInput? Keys, string? DeviceLabel);

public record PushSubscriptionRemoveInput(string? Endpoint);

public record StayMyBookingDto(
    string BookingUrl, string HouseName, string CompanyName, DateOnly CheckInDate, DateOnly CheckOutDate, StayBookingStatus Status,
    string DisplayStatus, string StatusText, int TotalRub);

// ── company cabinet ──
public record StaysCompanyCreateInput(string? Name, string? Slug, string? Description, string? Phone, string? OwnerTermsVersion, string? TrialTermsVersion);

public record StaysTrialOutcomeDto(bool Granted, string? RefusalCode, string? Message, DateTime? EndsAtUtc);

public record StaysTrialStateDto(
    bool Offered, bool Eligible, string? RefusalCode, string? Message, string? TermsVersion, string? TermsText, int DurationDays, DateTime? EndsAtUtc);

public record StaysTrialInput(string? TermsVersion);

public record StaysCompanyCreatedDto(StaysCompanyManageDto Company, string Token, StaysTrialOutcomeDto? Trial);

public record StaysCompanyListItemDto(
    Guid Id, string Name, string Slug, string? LogoUrl, string PublicUrl, StaysMyRole MyRole, bool AcceptingBookings, int? AwaitingPaymentCount);

public record StaysSlugCheckDto(string Suggested, bool Available, StaysConflictDto? Conflict);

public record StaysSettingsDto(
    string CheckInTime, string CheckOutTime, int MinNights, int MaxNights, int HorizonDays, bool AllowGapFill, bool AllowSameDayCheckIn,
    int HoldMinutes, int PrepayPercent, StayCancellationPolicy CancellationPolicy, int DogFeeRub, int CotFeeRub, string CheckInInfoSendTime,
    string? CheckInInfoText, bool CheckInInfoSendFullText, bool ArrivalReminderEnabled, bool HousekeeperSeesGuestComment, bool ShowInCatalog,
    bool? AcceptServiceOrdersWithoutStay = null);

public record PaymentDetailsDto(string? PaymentDetails, string? PaymentPurpose);

public record ProviderInput(StayProviderStatus? Status, string? Name, string? Inn, string? Ogrn, string? ClaimsAddress);

public record SlugInput(string? Slug);

public record ChecklistItemDto(string Code, bool Done, string Text);

public record StaysPlanSummaryDto(
    string? PlanName, bool IsTrial, DateTime? PaidUntilUtc, int? MaxHouses, int HousesPublished, string WarningLevel, string? Text);

public record StaysCompanyManageDto(
    Guid Id, string Name, string Slug, string? Description, string? Phone, string? Email, string? LogoUrl, string? Address,
    string? YandexMapsUrl, string? TwoGisUrl, string CityName, string TimeZoneId, bool IsActive, bool ShowInCatalog, string PublicUrl,
    StaysMyRole MyRole, List<StaysPermission> MyPermissions, StaysSettingsDto? Settings, PaymentDetailsDto? PaymentDetails,
    ProviderFullDto? Provider, GateDto Gate, List<ChecklistItemDto> Checklist, StaysPlanSummaryDto Plan, int? AwaitingPaymentCount);

public record StaysNotificationSettingsDto(
    bool StaffPushEnabled, bool StaffMaxEnabled, bool GuestWebPushEnabled, bool GuestMessengerEnabled, bool MessengerAvailable,
    string? DeliveryMode, string? PriorityTransport);

public record StaysNotificationSettingsInput(
    bool StaffPushEnabled, bool StaffMaxEnabled, bool GuestWebPushEnabled, bool GuestMessengerEnabled, string? DeliveryMode, string? PriorityTransport);

// ── houses ──
public record HouseListItemDto(
    Guid Id, string Slug, string Name, string? CoverUrl, int Capacity, bool IsPublished, bool IsArchived, HousePriceMode PriceMode,
    int? PriceFromRub, int Position, string PublicUrl, List<string> PublishProblems);

public record HouseCreateInput(string? Name, int Capacity);

public record HouseSetupInput(
    string? Name, string? Slug, int Capacity, bool ExtraBedsEnabled, int ExtraBedsMax, int ExtraBedPriceRub, bool DogsForbidden, bool HasCot);

public record HouseContentInput(
    string? Description, List<string>? Amenities, string? Address, string? YandexMapsUrl, string? TwoGisUrl, string? CheckInInfoText);

public record HousePricingInput(HousePriceMode Mode, int? ConstantPriceRub);

public record PricePeriodDto(Guid Id, DateOnly StartDate, DateOnly EndDate, int PriceRub);

public record PricePeriodInput(DateOnly? StartDate, DateOnly? EndDate, int PriceRub);

public record AttestationInput(bool Accepted, string? NoticeVersion);

public record HouseRegistryInput(HouseObjectKind? ObjectKind, string? RegistryNumber, string? RegistryUrl, AttestationInput? Attestation);

public record HousePublishInput(AttestationInput? Attestation);

public record IdsOrderInput(List<Guid>? Ids);

public record EmptyInput();

public record LastAttestationDto(DateTime AttestedAtUtc, string AttestedByName, HouseObjectKind ObjectKind, string? RegistryNumber);

public record RegistryNoticeDto(string Version, string Text);

public record HouseRegistryDto(HouseObjectKind? ObjectKind, string? RegistryNumber, string? RegistryUrl, LastAttestationDto? LastAttestation);

public record HouseManageDto(
    Guid Id, string Slug, string Name, string? Description, int Capacity, ExtraBedsDto ExtraBeds, bool DogsForbidden, bool HasCot,
    List<HouseAmenity> Amenities, string? Address, string? YandexMapsUrl, string? TwoGisUrl, string? CheckInInfoText, HousePriceMode PriceMode,
    int? ConstantPriceRub, HouseRegistryDto Registry, RegistryNoticeDto RegistryNotice, bool IsPublished, bool IsArchived, int Position,
    List<HousePhotoDto> Photos, string PublicUrl, List<DateRangeDto> UncoveredDates, List<string> PublishProblems, bool HasBookings);

// ── board, blocks ──
public record BoardHouseDto(Guid Id, string Name, bool IsPublished, bool IsArchived);

/// <summary><c>Comment</c> (appended) — the owner's note of a block, so the edit dialog keeps it; null for bookings and external periods.</summary>
public record BoardItemDto(
    BoardItemKind Kind, Guid Id, Guid HouseId, DateOnly StartDate, DateOnly EndDate, BoardItemState State, string StateText, string Label,
    DateTime? HoldExpiresAtUtc, HouseBlockKind? BlockKind, bool NeedsAction, string? Comment = null);

public enum BoardItemKind { Booking, Block, External }

public enum BoardItemState { Held, AwaitingPaymentCheck, Confirmed, Block, External }

public record StaysBoardDto(
    bool Changed, long Revision, DateTime ServerTimeUtc, DateOnly Today, DateOnly? From, int? Days, List<BoardHouseDto>? Houses,
    List<BoardItemDto>? Items, int? AwaitingPaymentCount, List<BoardServiceDto>? Services = null, List<BoardServiceCellDto>? ServiceCells = null);

public record HouseBlockInput(Guid? HouseId, DateOnly? StartDate, DateOnly? EndDate, HouseBlockKind Kind, string? Comment);

public record HouseBlockDto(
    Guid Id, Guid HouseId, DateOnly StartDate, DateOnly EndDate, HouseBlockKind Kind, string KindText, string? Comment, string CreatedByName,
    DateTime CreatedAtUtc, DateTime? UpdatedAtUtc);

// ── staff bookings ──
public record StaffStayBookingListItemDto(
    Guid Id, Guid HouseId, string HouseName, DateOnly CheckInDate, DateOnly CheckOutDate, int Nights, string? GuestName, string? GuestPhone,
    StayBookingStatus Status, string DisplayStatus, string StatusText, int TotalRub, int PrepayRub, DateTime? HoldExpiresAtUtc,
    DateTime? FirstProofUploadedAtUtc, bool IsManual, DateTime CreatedAtUtc);



public record PaymentConfirmedDto(DateTime AtUtc, string ByName);

public record StayBookingEventDto(DateTime OccurredAtUtc, string Kind, string Text, string ActorText, string? Reason);

public record StayMessageLogDto(string Type, string Channel, string Status, DateTime CreatedAtUtc);

public record BookingHouseIdDto(Guid Id, string Name);

public record StaffStayBookingCardDto(
    Guid Id, int Version, StayBookingStatus Status, string DisplayStatus, string StatusText, DateTime? HoldExpiresAtUtc, BookingHouseIdDto House,
    DateOnly CheckInDate, DateOnly CheckOutDate, string CheckInTime, string CheckOutTime, int Nights, int Adults, int Children, int Dogs,
    bool NeedCot, int ExtraBeds, string? ArrivalTime, string? GuestName, string? GuestPhone, string GuestKind, string? Comment,
    List<StayChargeLineDto> Lines, List<NightPriceDto> NightPrices, int TotalRub, int PrepayPercent, int PrepayRub, int DueAtCheckInRub,
    StayCancellationPolicy CancellationPolicy, string? OwnerCancelRefundText, List<PaymentProofDto> PaymentProofs, PaymentConfirmedDto? PaymentConfirmed,
    DateTime? PaymentProofsPurgedAtUtc, string? StatusReason, bool IsManual, List<string> AvailableActions, List<StayBookingEventDto> Events,
    List<StayMessageLogDto>? Messages, List<StaffBookingSessionDto>? Sessions = null);

public record ExpectedVersionInput(int? ExpectedVersion);

public record ExpectedVersionReasonInput(int? ExpectedVersion, string? Reason);

public record StaffStayQuoteInput(Guid? HouseId, DateOnly? CheckIn, DateOnly? CheckOut, int Adults, int Children, int Dogs, bool NeedCot);

public record ManualStayBookingInput(
    Guid? HouseId, DateOnly? CheckIn, DateOnly? CheckOut, int Adults, int Children, int Dogs, bool NeedCot, string? GuestName, string? GuestPhone,
    bool NotifyGuest, int? TotalOverrideRub, string? Comment);

// ── schedule ──
public record ScheduleDepartureDto(Guid BookingId, Guid HouseId, string HouseName, string CheckOutTime, bool SameDayTurnover);

public record ScheduleArrivalDto(
    Guid BookingId, Guid HouseId, string HouseName, string CheckInTime, string? ArrivalTime, string? GuestName, int Adults, int Children,
    int ExtraBeds, int Dogs, bool NeedCot, string? Comment, bool PaymentUnconfirmed, bool SameDayTurnover, string? TurnoverText);

public record ScheduleDayDto(
    DateOnly Date, string Label, List<ScheduleDepartureDto> Departures, List<ScheduleArrivalDto> Arrivals, List<ScheduleSessionDto>? Sessions = null);

public record StaysScheduleDto(DateOnly Today, List<ScheduleDayDto> Days);

public record StayCatalogPage(List<StayCatalogItemDto> Items, int TotalCount, int Page, int PageSize);

public record StaffStayBookingPage(List<StaffStayBookingListItemDto> Items, int TotalCount, int Page, int PageSize);
