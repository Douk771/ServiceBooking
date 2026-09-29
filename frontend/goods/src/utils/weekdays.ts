import type { DayOfWeek } from '../types'

/** Monday-first, the order of `WorkingHoursDto.days` and of every weekday list on the wire (API_CONTRACT_CYCLE24.md §473.1). */
export const WEEKDAYS: readonly { value: DayOfWeek; short: string; full: string }[] = [
  { value: 'Monday', short: 'пн', full: 'Понедельник' },
  { value: 'Tuesday', short: 'вт', full: 'Вторник' },
  { value: 'Wednesday', short: 'ср', full: 'Среда' },
  { value: 'Thursday', short: 'чт', full: 'Четверг' },
  { value: 'Friday', short: 'пт', full: 'Пятница' },
  { value: 'Saturday', short: 'сб', full: 'Суббота' },
  { value: 'Sunday', short: 'вс', full: 'Воскресенье' },
]

export const ALL_WEEKDAYS: readonly DayOfWeek[] = WEEKDAYS.map((d) => d.value)

const ORDER = new Map<string, number>(WEEKDAYS.map((d, i) => [d.value, i]))

/** Deduplicated, Monday-first. Unknown values (a future server enum) are dropped — the server rejects them anyway. */
export function normalizeWeekdays(days: readonly string[]): DayOfWeek[] {
  return [...new Set(days)].filter((d): d is DayOfWeek => ORDER.has(d)).sort((a, b) => ORDER.get(a)! - ORDER.get(b)!)
}

export function toggleWeekday(days: readonly string[], day: DayOfWeek): DayOfWeek[] {
  return normalizeWeekdays(days.includes(day) ? days.filter((d) => d !== day) : [...days, day])
}

export function isEveryDay(days: readonly string[]): boolean {
  return normalizeWeekdays(days).length === ALL_WEEKDAYS.length
}

/** Full weekday name for a `YYYY-MM-DD` date, computed from the calendar date itself (no timezone shift). */
export function shortWeekdayOfDate(date: string): string {
  const [y, m, d] = date.split('-').map(Number)
  const js = new Date(Date.UTC(y, m - 1, d)).getUTCDay() // 0 = Sunday
  return WEEKDAYS[(js + 6) % 7].short
}
