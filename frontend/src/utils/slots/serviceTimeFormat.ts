import { addDays, weekdayMon0 } from './slotDates'
import { calendarDateOf, clockOf, MINUTES_PER_DAY } from './businessClock'

/**
 * TS twin of the server's `ServiceTimeFormat` (ARCHITECTURE_CYCLE39.md §39.3.4), checked against contracts/cycle39/service-vectors.json
 * (`format`, `priceRules.labels`, `windows`). The server builds every label a person reads; this twin exists only for the editors
 * (windows, price rules) and for captions of a choice the screen builds before the answer arrives.
 *
 * Guest format (ЮР39-8): always calendar dates, never the word «бизнес-день» — «пт 15 янв, 22:00 — сб 16 янв, 01:00».
 * Staff format (SPEC §4.9): on the business day of the start — «Пт, 15 янв · 22:00 – 01:00 (сб)».
 */

const DOW = ['пн', 'вт', 'ср', 'чт', 'пт', 'сб', 'вс']
const MONTHS = ['янв', 'фев', 'мар', 'апр', 'мая', 'июн', 'июл', 'авг', 'сен', 'окт', 'ноя', 'дек']

const capitalize = (s: string) => s.charAt(0).toUpperCase() + s.slice(1)
const dayNumber = (date: string) => Number(date.slice(8, 10))
const monthShort = (date: string) => MONTHS[Number(date.slice(5, 7)) - 1]

/** «пт» for a calendar date. */
export const weekdayShort = (date: string): string => DOW[weekdayMon0(date)]

/** «пт 15 янв» — the date caption of the date step. */
export function businessDateLabel(date: string): string {
  return `${weekdayShort(date)} ${dayNumber(date)} ${monthShort(date)}`
}

/** Time of a start in the list of starts: «22:00», after midnight «00:30 (ночь на сб)». */
export function startLabel(businessDate: string, startMinute: number): string {
  if (startMinute < MINUTES_PER_DAY) return clockOf(startMinute)
  return `${clockOf(startMinute)} (ночь на ${weekdayShort(addDays(businessDate, 1))})`
}

/** Guest wording of a session: calendar dates only. */
export function guestTimeLabel(businessDate: string, startMinute: number, hours: number): string {
  const endMinute = startMinute + hours * 60
  const startDay = calendarDateOf(businessDate, startMinute)
  const endDay = calendarDateOf(businessDate, endMinute)
  const head = `${businessDateLabel(startDay)}, ${clockOf(startMinute)} — `
  return startDay === endDay ? `${head}${clockOf(endMinute)}` : `${head}${businessDateLabel(endDay)}, ${clockOf(endMinute)}`
}

/** Staff wording of a session, on the business day of the start. */
export function staffTimeLabel(businessDate: string, startMinute: number, hours: number): string {
  const endMinute = startMinute + hours * 60
  const day = `${capitalize(weekdayShort(businessDate))}, ${dayNumber(businessDate)} ${monthShort(businessDate)}`
  const next = weekdayShort(addDays(businessDate, 1))
  const start = startMinute >= MINUTES_PER_DAY ? `${clockOf(startMinute)} (ночь на ${next})` : clockOf(startMinute)
  // After a start past midnight the end needs no tag: it is the same night.
  const end = endMinute >= MINUTES_PER_DAY && startMinute < MINUTES_PER_DAY ? `${clockOf(endMinute)} (${next})` : clockOf(endMinute)
  return `${day} · ${start} – ${end}`
}

/** Window in the schedule editor: «18:00 – 02:00 (след. дня)». */
export function windowLabel(startMinute: number, endMinute: number): string {
  return `${clockOf(startMinute)} – ${clockOf(endMinute)}${endMinute >= MINUTES_PER_DAY ? ' (след. дня)' : ''}`
}

/** «Пн–Пт» for a contiguous run of days, «Пн, Ср, Сб» otherwise (bit 0 = Monday). */
export function daysMaskLabel(mask: number): string {
  const days: number[] = []
  for (let i = 0; i < 7; i++) if (mask & (1 << i)) days.push(i)
  if (days.length === 0) return ''
  const contiguous = days.length > 2 && days[days.length - 1] - days[0] === days.length - 1
  if (contiguous) return `${capitalize(DOW[days[0]])}–${capitalize(DOW[days[days.length - 1]])}`
  return days.map((d) => capitalize(DOW[d])).join(', ')
}

/** Price rule caption. Guest: «Пт 18:00 — 02:00 (ночь на сб)»; staff: «Пт 18:00 – 02:00 (след. дня)». */
export function priceRuleLabel(rule: { daysMask: number; fromHour: number; toHour: number }, audience: 'guest' | 'staff'): string {
  const hh = (h: number) => `${String(h % 24).padStart(2, '0')}:00`
  const days = daysMaskLabel(rule.daysMask)
  const night = rule.toHour >= 24
  if (audience === 'staff') return `${days} ${hh(rule.fromHour)} – ${hh(rule.toHour)}${night ? ' (след. дня)' : ''}`
  let suffix = ''
  if (night) {
    const single = [0, 1, 2, 3, 4, 5, 6].filter((i) => rule.daysMask & (1 << i))
    suffix = single.length === 1 ? ` (ночь на ${DOW[(single[0] + 1) % 7]})` : ' (ночь на следующий день)'
  }
  return `${days} ${hh(rule.fromHour)} — ${hh(rule.toHour)}${suffix}`
}
