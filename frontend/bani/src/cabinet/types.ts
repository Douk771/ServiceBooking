import type { components } from '@/types/api-cycle42.generated'

/** Cabinet types of bani, read straight off the generated schemas (ARCHITECTURE_CYCLE42.md §42.12.2). */
export type BathsCompanyManageDto = components['schemas']['BathsCompanyManageDto']
export type BathScheduleDto = components['schemas']['BathScheduleDto']
export type BathScheduleDayDto = components['schemas']['BathScheduleDayDto']
export type BathScheduleSessionDto = components['schemas']['BathScheduleSessionDto']
export type RevisionDto = components['schemas']['RevisionDto']
export type StaysPermission = components['schemas']['StaysPermission']
export type StaysMyRole = components['schemas']['StaysMyRole']

// ───── company: create, settings, trial, notifications (FE-42-5) ─────
export type BathsSettingsDto = components['schemas']['BathsSettingsDto']
export type BathsCompanyCreateInput = components['schemas']['BathsCompanyCreateInput']
export type BathsCompanyCreatedDto = components['schemas']['BathsCompanyCreatedDto']
export type BathsSlugCheckDto = components['schemas']['BathsSlugCheckDto']
export type BathsConflictDto = components['schemas']['BathsConflictDto']
export type ChecklistItemDto = components['schemas']['ChecklistItemDto']
export type BathsPlanSummaryDto = components['schemas']['BathsPlanSummaryDto']
export type PaymentDetailsDto = components['schemas']['PaymentDetailsDto']
export type ProviderInput = components['schemas']['ProviderInput']
export type StayProviderStatus = components['schemas']['StayProviderStatus']
export type TrialStateDto = components['schemas']['TrialStateDto']
export type TrialOutcomeDto = components['schemas']['TrialOutcomeDto']
export type NotificationSettingsDto = components['schemas']['NotificationSettingsDto']
export type NotificationSettingsInput = components['schemas']['NotificationSettingsInput']

/** The two positions of a «Бани» company (a `Master` with a position): «Администратор» sees bookings, «Банщик» only the schedule. */
export type BathsStaffPosition = 'Manager' | 'Housekeeper'
