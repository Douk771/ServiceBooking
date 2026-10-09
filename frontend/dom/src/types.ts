import type { components } from '@/types/api-cycle37.generated'
import type { components as Cycle40Components } from '@/types/api-cycle40.generated'

/**
 * dom types are read straight off the generated cycle-37 schema (ARCHITECTURE_CYCLE37.md §37.14.5) — never retyped by hand,
 * so an append-only enum member surfaces as a type error where a switch needs it.
 */
type S = components['schemas']

export type CompanyKind = S['CompanyKind']
export type StaffPosition = S['StaffPosition']
export type StaysMyRole = S['StaysMyRole']
export type StaysPermission = S['StaysPermission']
export type StayBookingStatus = S['StayBookingStatus']
export type StayCancellationPolicy = S['StayCancellationPolicy']
export type StayRefundKind = S['StayRefundKind']
export type HousePriceMode = S['HousePriceMode']
export type HouseObjectKind = S['HouseObjectKind']
export type HouseAmenity = S['HouseAmenity']
export type HouseBlockKind = S['HouseBlockKind']
export type StayProviderStatus = S['StayProviderStatus']
export type CalendarDayState = S['CalendarDayState']
export type StayChargeKind = S['StayChargeKind']
export type StayRefusalCode = S['StayRefusalCode']
export type NotAcceptingReason = S['NotAcceptingReason']
export type StayGuestConflictCode = S['StayGuestConflictCode']
export type StayStaffConflictCode = S['StayStaffConflictCode']
export type StaysConflictCode = S['StaysConflictCode']
export type StayGuestAction = S['StayGuestAction']
export type StaffStayAction = S['StaffStayAction']
export type StaysWarningLevel = S['StaysWarningLevel']
export type BoardItemKind = S['BoardItemKind']
export type BoardItemState = S['BoardItemState']

export type HouseAmenityDto = S['HouseAmenityDto']
export type ProviderInput = S['ProviderInput']
export type GateDto = S['GateDto']
export type NightPriceDto = S['NightPriceDto']
export type StayChargeLineDto = S['StayChargeLineDto']
export type StayRefundViewDto = S['StayRefundViewDto']
export type StayRefusalDto = S['StayRefusalDto']
export type StayGuestConflictDto = S['StayGuestConflictDto']
export type StayStaffConflictDto = S['StayStaffConflictDto']
export type BlockBookingConflictDto = S['BlockBookingConflictDto']
export type CompanyKindsSummaryDto = S['CompanyKindsSummaryDto']
export type StayCatalogItemDto = S['StayCatalogItemDto']
export type StayCatalogPage = S['StayCatalogPage']
export type PublicStaysCompanyDto = S['PublicStaysCompanyDto']
/** Cycle 40 (API_CONTRACT_CYCLE40.md §40.30.3): `messenger` — the server's «Получать уведомления о брони в {М}» offer; absent on an older server = not offered. */
export type PublicHouseDto = S['PublicHouseDto'] & { messenger?: Cycle40Components['schemas']['CustomerMessagingOfferDto'] }
export type HousePhotoDto = S['HousePhotoDto']
export type HouseCalendarDto = S['HouseCalendarDto']
export type CalendarDayDto = S['CalendarDayDto']
export type StayQuoteInput = S['StayQuoteInput']
export type StayQuoteDto = S['StayQuoteDto']
export type CreateStayBookingInput = S['CreateStayBookingInput']
export type CreateStayBookingResponse = S['CreateStayBookingResponse']
export type PublicStayBookingDto = S['PublicStayBookingDto']
export type StayMyBookingDto = S['StayMyBookingDto']
export type StaysCompanyCreateInput = S['StaysCompanyCreateInput']
export type StaysTrialOutcomeDto = S['StaysTrialOutcomeDto']
export type StaysTrialStateDto = S['StaysTrialStateDto']
export type StaysCompanyCreatedDto = S['StaysCompanyCreatedDto']
export type StaysCompanyListItemDto = S['StaysCompanyListItemDto']
export type StaysSlugCheckDto = S['StaysSlugCheckDto']
export type StaysSettingsDto = S['StaysSettingsDto']
export type PaymentDetailsDto = S['PaymentDetailsDto']
export type StaysCompanyManageDto = S['StaysCompanyManageDto']
export type StaysPlanSummaryDto = S['StaysPlanSummaryDto']
export type ChecklistItemDto = S['ChecklistItemDto']
// Cycle 40 (API_CONTRACT_CYCLE40.md §40.29): messagingActive / deliveryChoiceVisible / priorityWarning are appended by the server (the cycle37 schema is frozen).
export type StaysNotificationSettingsDto = S['StaysNotificationSettingsDto'] & {
  messagingActive?: boolean
  deliveryChoiceVisible?: boolean
  priorityWarning?: string | null
}
export type StaysNotificationSettingsInput = S['StaysNotificationSettingsInput']
export type HouseListItemDto = S['HouseListItemDto']
export type HouseSetupInput = S['HouseSetupInput']
export type HouseContentInput = S['HouseContentInput']
export type HousePricingInput = S['HousePricingInput']
export type PricePeriodDto = S['PricePeriodDto']
export type PricePeriodInput = S['PricePeriodInput']
export type HouseRegistryInput = S['HouseRegistryInput']
export type HousePublishInput = S['HousePublishInput']
export type HouseManageDto = S['HouseManageDto']
export type DateRangeDto = S['DateRangeDto']
export type BoardHouseDto = S['BoardHouseDto']
export type BoardItemDto = S['BoardItemDto']
export type StaysBoardDto = S['StaysBoardDto']
export type HouseBlockInput = S['HouseBlockInput']
export type HouseBlockDto = S['HouseBlockDto']
export type StaffStayBookingListItemDto = S['StaffStayBookingListItemDto']
export type StaffStayBookingPage = S['StaffStayBookingPage']
export type StaffStayBookingCardDto = S['StaffStayBookingCardDto']
export type StayBookingEventDto = S['StayBookingEventDto']
export type StaffStayQuoteInput = S['StaffStayQuoteInput']
export type ManualStayBookingInput = S['ManualStayBookingInput']
export type StaysScheduleDto = S['StaysScheduleDto']
export type ScheduleDayDto = S['ScheduleDayDto']
export type ScheduleArrivalDto = S['ScheduleArrivalDto']
export type ScheduleDepartureDto = S['ScheduleDepartureDto']
export type StaysSubscriptionBlockDto = S['StaysSubscriptionBlockDto']

// Service types shared with the baths vertical live in src/types/slots.ts (ARCHITECTURE_CYCLE42.md §42.12.1).
export * from '@/types/slots'

// ───────────── Cycle 39 (ARCHITECTURE_CYCLE39.md §39.14.2): types straight off the generated cycle-39 schema ─────────────
import type { components as C39 } from '@/types/api-cycle39.generated'

type S39 = C39['schemas']

export type ReminderWarning = S39['ReminderWarning']
export type ReminderErrorCode = S39['ReminderErrorCode']
export type ReminderDropReason = S39['ReminderDropReason']

export type StayBookingGuestConflictDto = S39['StayBookingGuestConflictDto']
export type BookingServicesDto = S39['BookingServicesDto']
export type BookingServiceOptionDto = S39['BookingServiceOptionDto']
export type AddSessionInput = S39['AddSessionInput']
export type PublicBookingSessionDto = S39['PublicBookingSessionDto']
export type StaffAddSessionInput = S39['StaffAddSessionInput']
export type StaffBookingSessionDto = S39['StaffBookingSessionDto']
export type ArrivalReminderSettingsDto = S39['ArrivalReminderSettingsDto']
export type ArrivalReminderInput = S39['ArrivalReminderInput']
export type ArrivalReminderPreviewInput = S39['ArrivalReminderPreviewInput']
export type ArrivalReminderPreviewDto = S39['ArrivalReminderPreviewDto']
export type ArrivalReminderChangeDto = S39['ArrivalReminderChangeDto']
export type ArrivalReminderSnapshotDto = S39['ArrivalReminderSnapshotDto']
export type StayServiceSelectionInput = S39['StayServiceSelectionInput']
export type StayQuoteServiceDto = S39['StayQuoteServiceDto']
export type BoardServiceDto = S39['BoardServiceDto']
export type BoardServiceCellDto = S39['BoardServiceCellDto']
export type ScheduleSessionDto = S39['ScheduleSessionDto']

/** The cycle-37 DTOs that cycle 39 extends in place (`shared-changed`): the new fields are ordered at the end of each. */
export type PublicStaysCompanyWithServices = PublicStaysCompanyDto & S39['PublicStaysCompanyServicesPart']
export type PublicHouseWithServices = PublicHouseDto & S39['PublicHouseServicesPart']
export type StayQuoteWithServices = StayQuoteDto & S39['StayQuoteServicesPart']
export type PublicStayBookingWithServices = PublicStayBookingDto & S39['PublicStayBookingServicesPart']
export type CreateStayBookingWithServices = CreateStayBookingInput & { services?: StayServiceSelectionInput[] }
export type StayQuoteInputWithServices = StayQuoteInput & { services?: StayServiceSelectionInput[] }
export type StaysSettingsWithServices = StaysSettingsDto & { acceptServiceOrdersWithoutStay?: boolean | null }
export type StaysCompanyManageWithServices = StaysCompanyManageDto & S39['StaysCompanyManageServicesPart']
export type StaysBoardWithServices = StaysBoardDto & S39['StaysBoardServicesPart']
export type StaffStayBookingCardWithServices = StaffStayBookingCardDto & S39['StaffStayBookingCardServicesPart']
/** The 409 of `POST …/houses/{id}/bookings` with a refused session names the service by its index in `services[]` (§39.24.4). */
export type StayRefusalWithService = StayRefusalDto & { serviceIndex?: number | null }
export type ReminderPlaceholderDto = S39['ReminderPlaceholderDto']
export type SessionBookingRefDto = S39['SessionBookingRefDto']
export type ScheduleDayServicesPart = S39['ScheduleDayServicesPart']
export type StaysScheduleWithServices = StaysScheduleDto & S39['StaysScheduleServicesPart']
