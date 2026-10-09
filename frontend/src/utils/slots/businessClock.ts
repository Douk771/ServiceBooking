import { addDays } from './slotDates'

/**
 * TS twin of the server's `BusinessClock` (ARCHITECTURE_CYCLE39.md §39.3.2), checked against contracts/cycle39/service-vectors.json
 * (`businessDay`). The business day of a service starts at 06:00 local time, so 01:00 on Saturday still belongs to Friday:
 * `minute` is counted from 00:00 of the business date (360 ≤ minute < 1800). The zone of the vertical is Asia/Novokuznetsk
 * (UTC+7, no daylight saving) — the offset is a parameter so the twin never reads the browser's zone (R37-7).
 */

export const BUSINESS_DAY_START_MINUTE = 360
export const NOVOKUZNETSK_OFFSET_MINUTES = 420
export const MINUTES_PER_DAY = 1440

export interface BusinessClockOptions {
  /** Minutes east of UTC. */
  offsetMinutes?: number
  /** Minute of the local day at which the business day starts. */
  dayStartMinute?: number
}

export interface BusinessMoment {
  businessDate: string
  minute: number
}

const iso = (ms: number): string => new Date(ms).toISOString().slice(0, 10)

/** `BusinessDateOf`: the business date and the minute-of-business-day of an instant. */
export function businessDateOf(utc: string | Date, opts: BusinessClockOptions = {}): BusinessMoment {
  const offset = opts.offsetMinutes ?? NOVOKUZNETSK_OFFSET_MINUTES
  const start = opts.dayStartMinute ?? BUSINESS_DAY_START_MINUTE
  const utcMs = typeof utc === 'string' ? Date.parse(utc) : utc.getTime()
  const localMs = utcMs + offset * 60_000
  const businessDate = iso(localMs - start * 60_000)
  const midnightMs = Date.parse(`${businessDate}T00:00:00Z`)
  return { businessDate, minute: Math.round((localMs - midnightMs) / 60_000) }
}

/** `ToUtc`: the instant of a minute of a business date, as an ISO string with `Z`. */
export function businessToUtc(businessDate: string, minute: number, opts: BusinessClockOptions = {}): string {
  const offset = opts.offsetMinutes ?? NOVOKUZNETSK_OFFSET_MINUTES
  const ms = Date.parse(`${businessDate}T00:00:00Z`) + (minute - offset) * 60_000
  return new Date(ms).toISOString().replace('.000Z', 'Z')
}

/** `TodayBusinessDate`. */
export function todayBusinessDate(now: string | Date, opts: BusinessClockOptions = {}): string {
  return businessDateOf(now, opts).businessDate
}

/** Calendar date (not business date) on which a minute of a business date falls: minutes ≥ 1440 are the next calendar day. */
export function calendarDateOf(businessDate: string, minute: number): string {
  return minute >= MINUTES_PER_DAY ? addDays(businessDate, 1) : businessDate
}

/** HH:MM wall clock of a minute of a business day (1500 → "01:00"). */
export function clockOf(minute: number): string {
  const m = ((minute % MINUTES_PER_DAY) + MINUTES_PER_DAY) % MINUTES_PER_DAY
  return `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`
}
