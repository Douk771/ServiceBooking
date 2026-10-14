import { BUSINESS_DAY_START_MINUTE, MINUTES_PER_DAY, clockOf } from './businessClock'
import { windowLabel } from './serviceTimeFormat'

/**
 * Validation of what the owner types into the schedule and price-rule editors (ARCHITECTURE_CYCLE39.md §39.3.3), checked against
 * contracts/cycle39/service-vectors.json (`windows`, `priceRules`). The server validates again and its text wins; this twin lets
 * the editor say «окна пересекаются» before the request and parse «02:00 → след. дня».
 */

const B = BUSINESS_DAY_START_MINUTE
const END_OF_BUSINESS_DAY = B + MINUTES_PER_DAY

export interface WindowLike {
  startMinute: number
  endMinute: number
}

export type WindowError = 'WindowNotOnGrid' | 'WindowEmpty' | 'WindowOutsideBusinessDay' | 'TooManyWindows' | 'WindowsOverlap'

export type WindowsResult = { ok: true; labels: string[] } | { ok: false; error: WindowError; first?: number; second?: number }

/** The texts of §39.31, printed at the field. */
export const WINDOW_ERROR_TEXT: Record<WindowError, string> = {
  WindowNotOnGrid: 'Время — с шагом 30 минут',
  WindowEmpty: 'Начало окна должно быть раньше конца',
  WindowOutsideBusinessDay: 'Окно должно уложиться с 06:00 до 06:00 следующего дня',
  TooManyWindows: 'Не больше трёх окон в день',
  WindowsOverlap: 'Окна пересекаются',
}

/** The first failure by the fixed order: grid → empty → outside → count → overlap. Touching windows are fine. */
export function validateWindows(windows: readonly WindowLike[]): WindowsResult {
  if (windows.some((w) => w.startMinute % 30 !== 0 || w.endMinute % 30 !== 0)) return { ok: false, error: 'WindowNotOnGrid' }
  if (windows.some((w) => w.startMinute >= w.endMinute)) return { ok: false, error: 'WindowEmpty' }
  if (windows.some((w) => w.startMinute < B || w.endMinute > END_OF_BUSINESS_DAY)) return { ok: false, error: 'WindowOutsideBusinessDay' }
  if (windows.length > 3) return { ok: false, error: 'TooManyWindows' }
  const order = windows.map((_, i) => i).sort((a, b) => windows[a].startMinute - windows[b].startMinute)
  for (let i = 1; i < order.length; i++) {
    if (windows[order[i]].startMinute < windows[order[i - 1]].endMinute) return { ok: false, error: 'WindowsOverlap', first: order[i - 1], second: order[i] }
  }
  return { ok: true, labels: windows.map((w) => windowLabel(w.startMinute, w.endMinute)) }
}

/**
 * «02:00» typed into a window → minute of the business day: times before 06:00 belong to the next calendar day. An END of
 * «06:00» is the end of the business day (1800), an end of «00:00» is midnight (1440). null for a malformed value.
 */
export function parseWindowTime(value: string, role: 'start' | 'end'): number | null {
  const m = /^([01]\d|2[0-3]):([0-5]\d)$/.exec(value.trim())
  if (!m) return null
  let minute = Number(m[1]) * 60 + Number(m[2])
  if (minute < B || (role === 'end' && minute === B)) minute += MINUTES_PER_DAY
  return role === 'end' && minute === 0 ? MINUTES_PER_DAY : minute
}

/** Minute of the business day → value of an `<input type="time">`. */
export function windowTimeValue(minute: number): string {
  return clockOf(minute)
}

export interface PriceRuleDraft {
  daysMask: number
  fromHour: number
  toHour: number
  priceRub: number
}

export type PriceRuleError = 'NoDays' | 'HoursOutOfRange' | 'PriceOutOfRange' | 'PriceRuleOverlap'

export const PRICE_RULE_ERROR_TEXT: Record<Exclude<PriceRuleError, 'PriceRuleOverlap'>, string> = {
  NoDays: 'Выберите дни недели',
  HoursOutOfRange: 'Часы — с 06:00 до 06:00 следующего дня',
  PriceOutOfRange: 'Цена за час — от 1 до 100 000 ₽',
}

export type PriceRuleResult = { ok: true } | { ok: false; error: PriceRuleError; conflictingRuleId?: string }

/** First failure: NoDays → HoursOutOfRange → PriceOutOfRange → PriceRuleOverlap (days intersect and [from, to) intersect). */
export function validatePriceRule(rule: PriceRuleDraft, existing: readonly (PriceRuleDraft & { id: string })[]): PriceRuleResult {
  if (rule.daysMask < 1 || rule.daysMask > 127) return { ok: false, error: 'NoDays' }
  if (rule.fromHour < 6 || rule.toHour > 30 || rule.fromHour >= rule.toHour) return { ok: false, error: 'HoursOutOfRange' }
  if (!Number.isInteger(rule.priceRub) || rule.priceRub < 1 || rule.priceRub > 100_000) return { ok: false, error: 'PriceOutOfRange' }
  const hit = existing.find((r) => (r.daysMask & rule.daysMask) !== 0 && rule.fromHour < r.toHour && r.fromHour < rule.toHour)
  return hit ? { ok: false, error: 'PriceRuleOverlap', conflictingRuleId: hit.id } : { ok: true }
}
