import type { BathsSettingsDto } from './types'

/**
 * Client checks of the booking settings of a «Бани» company (API_CONTRACT_CYCLE42.md §42.28). The texts are the server's; the checks only
 * save a round trip — the server validates again and its 400 text is shown under the field it names.
 */

export const HORIZON_MIN = 30
export const HORIZON_MAX = 730
export const HOLD_MIN = 10
export const HOLD_MAX = 180
export const REMINDER_HOURS_MIN = 1
export const REMINDER_HOURS_MAX = 24
/** What a freshly switched-on reminder starts with (the company is created with 3, §42.27.1). */
export const REMINDER_HOURS_DEFAULT = 3

export interface SettingsForm {
  horizonDays: number
  holdMinutes: number
  housekeeperSeesGuestComment: boolean
  showInCatalog: boolean
  sessionReminderEnabled: boolean
  /** Kept in the form even while the reminder is off, so switching it on again offers the last value. */
  sessionReminderHours: number
}

export type SettingsErrors = Partial<Record<keyof SettingsForm, string>>

const inRange = (v: number, min: number, max: number) => Number.isInteger(v) && v >= min && v <= max

/**
 * The form from what the server sent. While the reminder is off the server keeps NO number of hours (it is NULL there and may come back as
 * null or 0), so the field starts from the default instead of showing nothing sensible.
 */
export function settingsToForm(s: Pick<BathsSettingsDto, 'horizonDays' | 'holdMinutes' | 'housekeeperSeesGuestComment' | 'showInCatalog' | 'sessionReminderEnabled'> & { sessionReminderHours?: number | null }): SettingsForm {
  const hours = s.sessionReminderHours
  return {
    horizonDays: s.horizonDays,
    holdMinutes: s.holdMinutes,
    housekeeperSeesGuestComment: s.housekeeperSeesGuestComment,
    showInCatalog: s.showInCatalog,
    sessionReminderEnabled: s.sessionReminderEnabled,
    sessionReminderHours: s.sessionReminderEnabled && hours != null && inRange(hours, REMINDER_HOURS_MIN, REMINDER_HOURS_MAX) ? hours : REMINDER_HOURS_DEFAULT,
  }
}

/**
 * The body of `PUT …/settings` (full replacement). `sessionReminderHours` is required by the schema, but a switched-off reminder loses its
 * hours on the server — so the field is neither shown nor checked then, and the default goes along only to satisfy the shape.
 */
export function formToSettingsInput(f: SettingsForm): BathsSettingsDto {
  return {
    horizonDays: f.horizonDays,
    holdMinutes: f.holdMinutes,
    housekeeperSeesGuestComment: f.housekeeperSeesGuestComment,
    showInCatalog: f.showInCatalog,
    sessionReminderEnabled: f.sessionReminderEnabled,
    sessionReminderHours: f.sessionReminderEnabled ? f.sessionReminderHours : REMINDER_HOURS_DEFAULT,
  }
}

export function validateSettings(f: SettingsForm): SettingsErrors {
  const e: SettingsErrors = {}
  if (!inRange(f.horizonDays, HORIZON_MIN, HORIZON_MAX)) e.horizonDays = `Горизонт бронирования — от ${HORIZON_MIN} до ${HORIZON_MAX} дней`
  if (!inRange(f.holdMinutes, HOLD_MIN, HOLD_MAX)) e.holdMinutes = `Время на оплату — от ${HOLD_MIN} до ${HOLD_MAX} минут`
  if (f.sessionReminderEnabled && !inRange(f.sessionReminderHours, REMINDER_HOURS_MIN, REMINDER_HOURS_MAX)) {
    e.sessionReminderHours = `Напоминание — за ${REMINDER_HOURS_MIN}…${REMINDER_HOURS_MAX} часа до начала`
  }
  return e
}

/** Which field a 400 text of `PUT …/settings` belongs to (the server says it in words). */
export function settingsFieldOfError(text: string): keyof SettingsForm | null {
  if (text.startsWith('Горизонт')) return 'horizonDays'
  if (text.startsWith('Время на оплату')) return 'holdMinutes'
  if (text.startsWith('Напоминание')) return 'sessionReminderHours'
  return null
}
