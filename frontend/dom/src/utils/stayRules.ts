import type { CalendarDayDto, HouseCalendarDto, StayRefusalCode } from '../types'
import { addDays, nightDates, nightsBetween } from './stayDates'

/**
 * TS twin of the server's `StayRules.CheckStay` (ARCHITECTURE_CYCLE37.md §37.6.2), checked against stay-vectors.json (`stay`).
 * The server re-checks everything (§37.24); the client only keeps a guest from picking a range that cannot work, so the
 * order of refusals and the gap rule are the server's.
 */

export interface StaySettings {
  minNights: number
  maxNights: number
  horizonDays: number
  allowGapFill: boolean
  allowSameDayCheckIn: boolean
}

export interface Occupancy {
  startDate: string
  endDate: string
  /** Hold deadline (ISO instant). A hold with a deadline at or before `nowUtc` is free again. */
  holdExpiresAtUtc?: string | null
}

export interface StayCheckInput {
  checkIn: string
  checkOut: string
  /** Company-local "today". */
  today: string
  nowUtc: string
  settings: StaySettings
  occupancies: Occupancy[]
  /** Manual booking by staff: min/max nights and the horizon do not apply; overlap does. */
  manual?: boolean
}

export type StayCheckCode = Extract<
  StayRefusalCode,
  'InvalidDates' | 'CheckInInPast' | 'SameDayNotAllowed' | 'BeyondHorizon' | 'MaxNightsExceeded' | 'DatesUnavailable' | 'MinNightsNotMet'
>

const isActive = (o: Occupancy, nowMs: number) => !o.holdExpiresAtUtc || Date.parse(o.holdExpiresAtUtc) > nowMs

export function checkStay(input: StayCheckInput): 'Ok' | StayCheckCode {
  const { checkIn, checkOut, today, settings, manual } = input
  const nights = nightsBetween(checkIn, checkOut)
  if (nights <= 0) return 'InvalidDates'
  if (checkIn < today) return 'CheckInInPast'
  if (checkIn === today && !settings.allowSameDayCheckIn && !manual) return 'SameDayNotAllowed'
  // Last NIGHT (check-out − 1) must be inside the horizon: today + horizonDays − 1.
  if (!manual && addDays(checkOut, -1) > addDays(today, settings.horizonDays - 1)) return 'BeyondHorizon'
  if (!manual && nights > settings.maxNights) return 'MaxNightsExceeded'

  const nowMs = Date.parse(input.nowUtc)
  const active = input.occupancies.filter((o) => isActive(o, nowMs))
  if (active.some((o) => o.startDate < checkOut && checkIn < o.endDate)) return 'DatesUnavailable'

  if (!manual && nights < settings.minNights) {
    // A stay shorter than the minimum is allowed only to close a gap: a period ends exactly at check-in AND another starts
    // exactly at check-out. The stretch next to «today» or the horizon is not a gap (those are not periods).
    const closesGap = settings.allowGapFill && active.some((o) => o.endDate === checkIn) && active.some((o) => o.startDate === checkOut)
    if (!closesGap) return 'MinNightsNotMet'
  }
  return 'Ok'
}

// ---- selection on the calendar the server sent ----------------------------------------------------------------------------

/**
 * Why a range picked on the calendar cannot be booked, derived from the server's `GET …/calendar` (state per night) instead of
 * from raw occupancies, which the public API never reveals. Same order as `checkStay`; `Unavailable` nights (no price, past,
 * beyond the horizon) map to the specific code when the reason is visible and to `NoPriceForNights` otherwise.
 */
export type SelectionProblem = StayCheckCode | 'NoPriceForNights'

export function dayStateMap(calendar: Pick<HouseCalendarDto, 'days'>): Map<string, CalendarDayDto> {
  return new Map(calendar.days.map((d) => [d.date, d]))
}

const isTaken = (d: CalendarDayDto | undefined) => d?.state === 'Occupied' || d?.state === 'MayFreeUp'

export function validateSelection(calendar: HouseCalendarDto, checkIn: string, checkOut: string): 'Ok' | SelectionProblem {
  const nights = nightsBetween(checkIn, checkOut)
  if (nights <= 0) return 'InvalidDates'
  if (checkIn < calendar.today) return 'CheckInInPast'
  if (checkIn === calendar.today && !calendar.allowSameDayCheckIn) return 'SameDayNotAllowed'
  if (addDays(checkOut, -1) > calendar.lastNight) return 'BeyondHorizon'
  if (nights > calendar.maxNights) return 'MaxNightsExceeded'

  const byDate = dayStateMap(calendar)
  const dates = nightDates(checkIn, checkOut)
  if (dates.some((d) => isTaken(byDate.get(d)))) return 'DatesUnavailable'
  if (dates.some((d) => byDate.get(d)?.state === 'Unavailable')) return 'NoPriceForNights'

  if (nights < calendar.minNights) {
    // A night before check-in is taken (a period ends exactly at check-in) and the night of the check-out date is taken (a
    // period starts exactly at check-out).
    const closesGap = calendar.allowGapFill && isTaken(byDate.get(addDays(checkIn, -1))) && isTaken(byDate.get(checkOut))
    if (!closesGap) return 'MinNightsNotMet'
  }
  return 'Ok'
}
