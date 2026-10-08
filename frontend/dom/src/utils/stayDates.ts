/**
 * Dates of nights are plain "YYYY-MM-DD" strings in the company's own time zone (API_CONTRACT_CYCLE37.md §37.20): night D starts
 * on date D, an occupancy `[startDate, endDate)` ends on the check-out date. All arithmetic here is calendar arithmetic on those
 * strings (UTC `Date` internally, never the browser's zone), so a phone in another time zone computes the same nights as the
 * server (R37-7). Instants (hold deadline, server time) are converted to a wall clock of the booking's zone only for display.
 */

const DATE_RE = /^(\d{4})-(\d{2})-(\d{2})$/

export function isIsoDate(value: string): boolean {
  const m = DATE_RE.exec(value)
  if (!m) return false
  const d = new Date(Date.UTC(Number(m[1]), Number(m[2]) - 1, Number(m[3])))
  return toIsoDate(d) === value
}

function parse(value: string): Date {
  const m = DATE_RE.exec(value)
  if (!m) throw new Error(`not a YYYY-MM-DD date: ${value}`)
  return new Date(Date.UTC(Number(m[1]), Number(m[2]) - 1, Number(m[3])))
}

function toIsoDate(d: Date): string {
  const y = String(d.getUTCFullYear()).padStart(4, '0')
  const mo = String(d.getUTCMonth() + 1).padStart(2, '0')
  const da = String(d.getUTCDate()).padStart(2, '0')
  return `${y}-${mo}-${da}`
}

export function addDays(date: string, days: number): string {
  const d = parse(date)
  d.setUTCDate(d.getUTCDate() + days)
  return toIsoDate(d)
}

/** Whole days from `a` to `b` (b − a); negative when b is earlier. */
export function diffDays(a: string, b: string): number {
  return Math.round((parse(b).getTime() - parse(a).getTime()) / 86_400_000)
}

/** Number of nights between check-in and check-out dates (0 when check-out is not later). */
export function nightsBetween(checkIn: string, checkOut: string): number {
  return Math.max(0, diffDays(checkIn, checkOut))
}

/** Every night date of `[checkIn, checkOut)`. */
export function nightDates(checkIn: string, checkOut: string): string[] {
  const out: string[] = []
  for (let i = 0; i < nightsBetween(checkIn, checkOut); i++) out.push(addDays(checkIn, i))
  return out
}

/** ISO weekday, 0 = Monday … 6 = Sunday. */
export function weekdayMon0(date: string): number {
  return (parse(date).getUTCDay() + 6) % 7
}

export function firstOfMonth(date: string): string {
  return `${date.slice(0, 7)}-01`
}

export function addMonths(monthFirst: string, months: number): string {
  const d = parse(monthFirst)
  d.setUTCMonth(d.getUTCMonth() + months, 1)
  return toIsoDate(d)
}

export function daysInMonth(monthFirst: string): number {
  const d = parse(monthFirst)
  return new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth() + 1, 0)).getUTCDate()
}

const MONTHS_GEN = ['января', 'февраля', 'марта', 'апреля', 'мая', 'июня', 'июля', 'августа', 'сентября', 'октября', 'ноября', 'декабря']
const MONTHS_SHORT = ['янв', 'фев', 'мар', 'апр', 'мая', 'июн', 'июл', 'авг', 'сен', 'окт', 'ноя', 'дек']
const MONTHS_NOM = ['Январь', 'Февраль', 'Март', 'Апрель', 'Май', 'Июнь', 'Июль', 'Август', 'Сентябрь', 'Октябрь', 'Ноябрь', 'Декабрь']
const WEEKDAYS_SHORT = ['пн', 'вт', 'ср', 'чт', 'пт', 'сб', 'вс']
const WEEKDAYS_LONG = ['понедельник', 'вторник', 'среда', 'четверг', 'пятница', 'суббота', 'воскресенье']

export const WEEKDAY_HEADERS = WEEKDAYS_SHORT

/** "30 декабря 2026" */
export function formatDateLong(date: string): string {
  const [y, m, d] = date.split('-').map(Number)
  return `${d} ${MONTHS_GEN[m - 1]} ${y}`
}

/** "30.12.2026" */
export function formatDateNumeric(date: string): string {
  const [y, m, d] = date.split('-')
  return `${d}.${m}.${y}`
}

/** "30 дек" — year only when it differs from `referenceYear`. */
export function formatDateShort(date: string, referenceYear?: number): string {
  const [y, m, d] = date.split('-').map(Number)
  const base = `${d} ${MONTHS_SHORT[m - 1]}`
  return referenceYear !== undefined && y !== referenceYear ? `${base} ${y}` : base
}

/** "пт, 1 января" */
export function formatDateWithWeekday(date: string): string {
  const [, m, d] = date.split('-').map(Number)
  return `${WEEKDAYS_SHORT[weekdayMon0(date)]}, ${d} ${MONTHS_GEN[m - 1]}`
}

export function weekdayLong(date: string): string {
  return WEEKDAYS_LONG[weekdayMon0(date)]
}

/** "Декабрь 2026" */
export function formatMonthTitle(monthFirst: string): string {
  const [y, m] = monthFirst.split('-').map(Number)
  return `${MONTHS_NOM[m - 1]} ${y}`
}

/** Russian plural: 1 ночь, 2 ночи, 5 ночей. */
export function pluralRu(n: number, one: string, few: string, many: string): string {
  const abs = Math.abs(n) % 100
  const last = abs % 10
  if (abs > 10 && abs < 20) return many
  if (last === 1) return one
  if (last >= 2 && last <= 4) return few
  return many
}

export const nightsLabel = (n: number) => `${n} ${pluralRu(n, 'ночь', 'ночи', 'ночей')}`
export const guestsLabel = (n: number) => `${n} ${pluralRu(n, 'гость', 'гостя', 'гостей')}`

/** "30 дек – 2 янв 2027 · 3 ночи" (check-in – check-out dates; the year is that of the check-out). */
export function formatStayRange(checkIn: string, checkOut: string): string {
  const sameYear = checkIn.slice(0, 4) === checkOut.slice(0, 4)
  const left = sameYear ? formatDateShort(checkIn) : formatDateShort(checkIn, 0)
  return `${left} – ${formatDateShort(checkOut, 0)} · ${nightsLabel(nightsBetween(checkIn, checkOut))}`
}

/** Blocks store the date "по" as the check-out date; the owner reads «ночи с … по …» (last night = endDate − 1). */
export function formatBlockNights(startDate: string, endDate: string): string {
  const last = addDays(endDate, -1)
  const n = nightsBetween(startDate, endDate)
  return n <= 1 ? `ночь ${formatDateShort(startDate)}` : `ночи с ${formatDateShort(startDate)} по ${formatDateShort(last)}`
}

// ---- instants and zones -------------------------------------------------------------------------------------------------

function offsetMsOf(instantMs: number, timeZoneId: string): number {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: timeZoneId,
    hourCycle: 'h23',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).formatToParts(new Date(instantMs))
  const get = (t: string) => Number(parts.find((p) => p.type === t)?.value)
  const asUtc = Date.UTC(get('year'), get('month') - 1, get('day'), get('hour'), get('minute'), get('second'))
  return asUtc - Math.floor(instantMs / 1000) * 1000
}

/** The UTC instant at which the wall clock of `timeZoneId` shows `date` `time` ("HH:mm"). */
export function zonedWallToUtcMs(date: string, time: string, timeZoneId: string): number {
  const [y, mo, d] = date.split('-').map(Number)
  const [h, mi] = time.split(':').map(Number)
  const guess = Date.UTC(y, mo - 1, d, h, mi)
  let result = guess - offsetMsOf(guess, timeZoneId)
  // A second pass settles the instants next to a DST switch (no zone of the project has one today; kept for correctness).
  result = guess - offsetMsOf(result, timeZoneId)
  return result
}

/** Local date ("YYYY-MM-DD") and time ("HH:mm") of an instant in the zone. */
export function instantToZoned(instantMs: number, timeZoneId: string): { date: string; time: string } {
  const off = offsetMsOf(instantMs, timeZoneId)
  const local = new Date(instantMs + off)
  return {
    date: toIsoDate(local),
    time: `${String(local.getUTCHours()).padStart(2, '0')}:${String(local.getUTCMinutes()).padStart(2, '0')}`,
  }
}

/** "30 дек, 14:30" — an instant on the wall clock of the booking's zone (never the browser's). */
export function formatInstantInZone(iso: string, timeZoneId: string): string {
  const { date, time } = instantToZoned(Date.parse(iso), timeZoneId)
  return `${formatDateShort(date)}, ${time}`
}

/** "mm:ss" countdown, never negative. */
export function formatCountdown(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000))
  const m = Math.floor(total / 60)
  const s = total % 60
  return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
}

/** Half-hour options of the arrival time: from `checkInTime` up to 23:30 (API_CONTRACT_CYCLE37.md §37.24). */
export function arrivalTimeOptions(checkInTime: string): string[] {
  const [h, m] = checkInTime.split(':').map(Number)
  const start = h * 60 + (m >= 30 ? 30 : 0) + (m % 30 === 0 ? 0 : 30)
  const out: string[] = []
  for (let t = start; t <= 23 * 60 + 30; t += 30) {
    out.push(`${String(Math.floor(t / 60)).padStart(2, '0')}:${String(t % 60).padStart(2, '0')}`)
  }
  return out
}
