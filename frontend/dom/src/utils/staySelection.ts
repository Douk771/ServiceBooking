import type { HouseCalendarDto, StayRefusalCode } from '../types'
import { addDays, formatDateNumeric, nightsLabel } from './stayDates'
import { dayStateMap, validateSelection, type SelectionProblem } from './stayRules'

/**
 * Picking a stay on the calendar (US-37-05/06): the two-click range logic and the wording of why a range is refused. The wording is
 * the server's (API_CONTRACT_CYCLE37.md §37.25.2), reproduced here only to answer BEFORE the request; the server re-checks
 * everything and its text is what the form prints for a real refusal.
 */

export interface StayRange {
  checkIn: string | null
  checkOut: string | null
}

export const EMPTY_RANGE: StayRange = { checkIn: null, checkOut: null }

export function selectionProblemText(problem: SelectionProblem | StayRefusalCode, c: Pick<HouseCalendarDto, 'minNights' | 'maxNights' | 'lastNight'>): string {
  switch (problem) {
    case 'InvalidDates':
      return 'Дата выезда должна быть позже даты заезда'
    case 'CheckInInPast':
      return 'Дата заезда уже прошла'
    case 'SameDayNotAllowed':
      return 'Заезд в день бронирования недоступен — выберите дату с завтрашнего дня'
    case 'BeyondHorizon':
      return `Бронирование открыто до ${formatDateNumeric(addDays(c.lastNight, 1))}`
    case 'MaxNightsExceeded':
      return `Максимальный срок проживания — ${nightsLabel(c.maxNights)}`
    case 'MinNightsNotMet':
      return `Минимальный срок проживания — ${nightsLabel(c.minNights)}`
    case 'NoPriceForNights':
      return 'На часть выбранных ночей нет цены — выберите другие даты'
    case 'DatesUnavailable':
    default:
      return 'Эти даты уже заняты. Выберите другие'
  }
}

export interface PickResult {
  range: StayRange
  /** Why the click did not complete a range; shown in a live region next to the calendar. */
  message: string | null
}

/**
 * One click on a calendar date. The first click picks the check-in (a free night); the second picks the check-out date, which
 * may itself be a taken night (it is the departure day). A refused second click keeps the check-in and says why; a click on a free
 * date before the check-in moves the check-in. After a complete range, a click starts over.
 */
export function pickDay(calendar: HouseCalendarDto, current: StayRange, date: string): PickResult {
  const state = dayStateMap(calendar).get(date)?.state
  const startOver = (): PickResult => {
    if (state === 'Free') {
      const problem = validateSelection(calendar, date, addDays(date, 1))
      // The one-night check only matters for «today» without same-day check-in, the past and the horizon; a short stay is fine here.
      if (problem === 'CheckInInPast' || problem === 'SameDayNotAllowed' || problem === 'BeyondHorizon') {
        return { range: current, message: selectionProblemText(problem, calendar) }
      }
      return { range: { checkIn: date, checkOut: null }, message: null }
    }
    if (state === 'MayFreeUp') return { range: current, message: 'Дата временно удержана другим гостем — возможно, освободится' }
    if (state === 'Occupied') return { range: current, message: selectionProblemText('DatesUnavailable', calendar) }
    return { range: current, message: 'Дата недоступна для бронирования' }
  }

  if (!current.checkIn || current.checkOut) return startOver()
  if (date <= current.checkIn) return startOver()

  const problem = validateSelection(calendar, current.checkIn, date)
  if (problem === 'Ok') return { range: { checkIn: current.checkIn, checkOut: date }, message: null }
  return { range: current, message: selectionProblemText(problem, calendar) }
}

/** Compact price for a calendar cell: 5000 → «5к», 5500 → «5,5к», 950 → «950». */
export function priceShort(priceRub: number | null | undefined): string {
  if (priceRub == null) return ''
  if (priceRub < 1000) return String(priceRub)
  const k = Math.round(priceRub / 100) / 10
  return `${String(k).replace('.', ',')}к`
}

/** Text of a cell for screen readers and tooltips: state in words, never colour alone. */
export function dayStateText(state: HouseCalendarDto['days'][number]['state']): string {
  switch (state) {
    case 'Free':
      return 'Свободно'
    case 'MayFreeUp':
      return 'Возможно освободится'
    case 'Occupied':
      return 'Занято'
    case 'Unavailable':
      return 'Недоступно'
  }
}
