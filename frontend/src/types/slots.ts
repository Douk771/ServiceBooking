import type { components as C37 } from '@/types/api-cycle37.generated'
import type { components as C39 } from '@/types/api-cycle39.generated'
import type { components as C42 } from '@/types/api-cycle42.generated'

/**
 * Service («услуги») types shared by the dom and baths verticals, read straight off the generated schemas
 * (ARCHITECTURE_CYCLE42.md §42.12.1). Moved from frontend/dom/src/types.ts; dom re-exports them.
 */
type S37 = C37['schemas']
type S39 = C39['schemas']
type S42 = C42['schemas']

// Shared by the services of both verticals (moved from dom/src/types.ts, 42.12.1).
export type StayDisplayStatus = S37['StayDisplayStatus']
export type ProviderPublicDto = S37['ProviderPublicDto']
export type ProviderFullDto = S37['ProviderFullDto']
export type StaysConflictDto = S37['StaysConflictDto']

export type PaymentProofDto = S37['PaymentProofDto']
export type PushSubscriptionInput = S37['PushSubscriptionInput']
export type MinuteOfBusinessDay = S39['MinuteOfBusinessDay']
export type StayServiceSessionState = S39['StayServiceSessionState']
export type StayServiceCancellationPolicy = S39['StayServiceCancellationPolicy']
export type StayServiceRequestBasis = S39['StayServiceRequestBasis']
export type ServiceRefundKind = S39['ServiceRefundKind']
export type ServiceRefusalCode = S39['ServiceRefusalCode']
export type ServiceGuestConflictCode = S39['ServiceGuestConflictCode']
export type StaysServiceConflictCode = S39['StaysServiceConflictCode'] | S42['StaysServiceConflictCode']
/** Soft warnings about an owner's text (Т42-12): they never block saving. */
export type OwnerTextWarning = S42['OwnerTextWarning']
export type StartsReason = S39['StartsReason']
export type ServiceSessionKind = S39['ServiceSessionKind']
export type ServiceGuestAction = S39['ServiceGuestAction']
export type ServiceStaffAction = S39['ServiceStaffAction']
export type WindowInput = S39['WindowInput']
export type WindowDto = S39['WindowDto']
export type ServiceTimeDto = S39['ServiceTimeDto']
export type HourPriceDto = S39['HourPriceDto']
export type ItemSelectionInput = S39['ItemSelectionInput']
export type ServiceSelectionInput = S39['ServiceSelectionInput']
export type ServiceItemPublicDto = S39['ServiceItemPublicDto']
export type SessionItemDto = S39['SessionItemDto']
export type ServiceChargeLineDto = S39['ServiceChargeLineDto']
export type ServiceRefundViewDto = S39['ServiceRefundViewDto']
export type ProblemDto = S39['ProblemDto']
export type ServiceRefusalDto = S39['ServiceRefusalDto']
export type ServiceOrderGuestConflictDto = S39['ServiceOrderGuestConflictDto']
export type ServiceStaffConflictDto = S39['ServiceStaffConflictDto']
export type StaysServiceConflictDto = S39['StaysServiceConflictDto'] & { markers?: string[] | null; noticeText?: string | null }
export type PublicServiceSummaryDto = S39['PublicServiceSummaryDto']
export type ServicePhotoDto = S39['ServicePhotoDto']
export type PublicServiceDto = S39['PublicServiceDto'] & { capacity?: number | null; cityName?: string; localTimeNote?: string }
export type ServiceAvailabilityDto = S39['ServiceAvailabilityDto']
export type AvailabilityDayDto = S39['AvailabilityDayDto']
export type StartDto = S39['StartDto']
export type ServiceStartsDto = S39['ServiceStartsDto']
export type PublicServiceQuoteInput = S39['PublicServiceQuoteInput']
export type ServiceQuoteDto = S39['ServiceQuoteDto']
export type CreateServiceOrderInput = S39['CreateServiceOrderInput'] & { guestsCount?: number | null }
export type CreateServiceOrderResponse = S39['CreateServiceOrderResponse']
export type SessionReminderViewDto = S42['SessionReminderViewDto']
export type PublicServiceOrderDto = S39['PublicServiceOrderDto'] & {
  guestsCount?: number | null
  cityName?: string
  localTimeNote?: string
  /** Not null once the reminder was sent (bani). */
  sessionReminder?: SessionReminderViewDto | null
  /** `/<companySlug>` — «Забронировать ещё в этом комплексе» (bani); no personal data in it (Т42-09). */
  bookAgainUrl?: string | null
}
export type ServiceListItemDto = S39['ServiceListItemDto']
export type ServiceSetupInput = S39['ServiceSetupInput'] & { capacity?: number | null }
export type ServiceManageDto = S39['ServiceManageDto'] & { capacity?: number | null; contentWarnings?: OwnerTextWarning[] }
export type PriceRuleDto = S39['PriceRuleDto']
export type PriceRuleInput = S39['PriceRuleInput']
export type PriceRulesDto = S39['PriceRulesDto']
export type ServiceItemDto = S39['ServiceItemDto'] & { warnings?: OwnerTextWarning[] }
export type ServiceItemInput = S39['ServiceItemInput'] & { confirmRestricted?: boolean }
export type WeeklyScheduleDto = S39['WeeklyScheduleDto']
export type WeeklyScheduleInput = S39['WeeklyScheduleInput']
export type ScheduleSaveResultDto = S39['ScheduleSaveResultDto']
export type ServiceMonthDto = S39['ServiceMonthDto']
export type ServiceMonthDayDto = S39['ServiceMonthDayDto']
export type DateOverrideInput = S39['DateOverrideInput']
export type ServiceDayDto = S39['ServiceDayDto']
export type ServiceDayBarDto = S39['ServiceDayBarDto']
export type StaffServiceSessionListItemDto = S39['StaffServiceSessionListItemDto']
export type StaffServiceSessionPage = S39['StaffServiceSessionPage']
export type StaffServiceSessionCardDto = S39['StaffServiceSessionCardDto'] & { guestsCount?: number | null }
export type StaffServiceQuoteInput = S39['StaffServiceQuoteInput']
export type ManualServiceOrderInput = S39['ManualServiceOrderInput'] & { guestsCount?: number | null }
export type ProofRulesDto = S39['ProofRulesDto']
export type WeeklyDayDto = S39['WeeklyDayDto']
export type PriceMatrixRowDto = S39['PriceMatrixRowDto']
export type BoundaryRangeDto = S39['BoundaryRangeDto']
export type ServiceDayAxisDto = S39['ServiceDayAxisDto']
export type ServiceDayServiceDto = S39['ServiceDayServiceDto']
export type JournalEventDto = S39['JournalEventDto']
export type SessionAddedByDto = S39['SessionAddedByDto']
