import type { OrderHistoryQuery, OrderHistorySort, OrderStatus, ReportPeriodPreset, SummaryTopSort } from '../types'

/**
 * URL <-> filter state of the report screens (API_CONTRACT_CYCLE25.md §538). The client never computes a period:
 * it only carries the preset (and `from`/`to` for `Custom`). The buyer filter is deliberately NOT here — it is a phone
 * or a name and lives in `location.state`, never in the address.
 */

export const PERIOD_PRESETS: { value: ReportPeriodPreset; label: string }[] = [
  { value: 'Today', label: 'Сегодня' },
  { value: 'Yesterday', label: 'Вчера' },
  { value: 'Last7Days', label: '7 дней' },
  { value: 'Last30Days', label: '30 дней' },
  { value: 'ThisMonth', label: 'Этот месяц' },
  { value: 'LastMonth', label: 'Прошлый месяц' },
  { value: 'Custom', label: 'Свой период' },
]

export const ORDER_STATUSES: { value: OrderStatus; label: string }[] = [
  { value: 'New', label: 'Новый' },
  { value: 'Accepted', label: 'Принят' },
  { value: 'Ready', label: 'Готов' },
  { value: 'Issued', label: 'Выдан' },
  { value: 'Rejected', label: 'Отклонён' },
  { value: 'CancelledByCustomer', label: 'Отменён покупателем' },
  { value: 'CancelledByShop', label: 'Отменён магазином' },
  { value: 'NotPickedUp', label: 'Не забран' },
]

const PRESET_SET = new Set<string>(PERIOD_PRESETS.map((p) => p.value))
const STATUS_SET = new Set<string>(ORDER_STATUSES.map((s) => s.value))
const DATE_RE = /^\d{4}-\d{2}-\d{2}$/
const TIME_RE = /^([01]\d|2[0-3]):[0-5]\d$/

export interface HistoryFilters {
  period: ReportPeriodPreset
  from: string
  to: string
  statuses: OrderStatus[]
  amountFrom: string
  amountTo: string
  number: string
  sort: OrderHistorySort
  page: number
}

export const DEFAULT_HISTORY_FILTERS: HistoryFilters = {
  period: 'Last7Days',
  from: '',
  to: '',
  statuses: [],
  amountFrom: '',
  amountTo: '',
  number: '',
  sort: 'PickupDesc',
  page: 1,
}

function preset(raw: string | null, fallback: ReportPeriodPreset): ReportPeriodPreset {
  return raw && PRESET_SET.has(raw) ? (raw as ReportPeriodPreset) : fallback
}
function dateOrEmpty(raw: string | null): string {
  return raw && DATE_RE.test(raw) ? raw : ''
}
function positiveInt(raw: string | null): number {
  const n = Number(raw)
  return Number.isInteger(n) && n >= 1 ? n : 1
}

export function parseHistoryParams(sp: URLSearchParams): HistoryFilters {
  const statuses = (sp.get('statuses') ?? '')
    .split(',')
    .filter((s) => STATUS_SET.has(s)) as OrderStatus[]
  return {
    period: preset(sp.get('period'), DEFAULT_HISTORY_FILTERS.period),
    from: dateOrEmpty(sp.get('from')),
    to: dateOrEmpty(sp.get('to')),
    statuses: [...new Set(statuses)],
    amountFrom: sp.get('amountFrom') ?? '',
    amountTo: sp.get('amountTo') ?? '',
    number: sp.get('number') ?? '',
    sort: sp.get('sort') === 'PickupAsc' ? 'PickupAsc' : 'PickupDesc',
    page: positiveInt(sp.get('page')),
  }
}

/** Only non-default values go to the address, so the default screen has a clean URL. */
export function buildHistoryParams(f: HistoryFilters): URLSearchParams {
  const sp = new URLSearchParams()
  if (f.period !== DEFAULT_HISTORY_FILTERS.period) sp.set('period', f.period)
  if (f.period === 'Custom') {
    if (f.from) sp.set('from', f.from)
    if (f.to) sp.set('to', f.to)
  }
  if (f.statuses.length > 0) sp.set('statuses', f.statuses.join(','))
  if (f.amountFrom.trim()) sp.set('amountFrom', f.amountFrom.trim())
  if (f.amountTo.trim()) sp.set('amountTo', f.amountTo.trim())
  if (f.number.trim()) sp.set('number', f.number.trim())
  if (f.sort !== 'PickupDesc') sp.set('sort', f.sort)
  if (f.page > 1) sp.set('page', String(f.page))
  return sp
}

function numOrNull(raw: string): number | null {
  const t = raw.trim().replace(',', '.')
  if (!t) return null
  const n = Number(t)
  return Number.isFinite(n) ? n : null
}

/** Request body of `POST …/order-history`. Empty fields are sent as `null` (= "not set"), never as empty strings. */
export function toHistoryQuery(f: HistoryFilters, customer: string): OrderHistoryQuery {
  const number = numOrNull(f.number)
  return {
    period: f.period,
    from: f.period === 'Custom' && f.from ? f.from : null,
    to: f.period === 'Custom' && f.to ? f.to : null,
    statuses: f.statuses.length > 0 ? f.statuses : null,
    customer: customer.trim() ? customer.trim() : null,
    amountFrom: numOrNull(f.amountFrom),
    amountTo: numOrNull(f.amountTo),
    number: number === null ? null : Math.trunc(number),
    sort: f.sort,
    page: f.page,
  }
}

/** True when anything besides the period narrows the list (drives "reset filters"). */
export function hasExtraFilters(f: HistoryFilters, customer: string): boolean {
  return f.statuses.length > 0 || !!f.amountFrom.trim() || !!f.amountTo.trim() || !!f.number.trim() || !!customer.trim()
}

// ── Summary
export interface SummaryFilters {
  period: ReportPeriodPreset
  from: string
  to: string
  top: SummaryTopSort
}

export function parseSummaryParams(sp: URLSearchParams): SummaryFilters {
  return {
    period: preset(sp.get('period'), 'Today'),
    from: dateOrEmpty(sp.get('from')),
    to: dateOrEmpty(sp.get('to')),
    top: sp.get('top') === 'Quantity' ? 'Quantity' : 'Amount',
  }
}

export function buildSummaryParams(f: SummaryFilters): URLSearchParams {
  const sp = new URLSearchParams()
  if (f.period !== 'Today') sp.set('period', f.period)
  if (f.period === 'Custom') {
    if (f.from) sp.set('from', f.from)
    if (f.to) sp.set('to', f.to)
  }
  if (f.top !== 'Amount') sp.set('top', f.top)
  return sp
}

// ── Pick list
export interface PickListFilters {
  date: string
  from: string
  to: string
  includeNew: boolean
}

export function parsePickListParams(sp: URLSearchParams): PickListFilters {
  const from = sp.get('from') ?? ''
  const to = sp.get('to') ?? ''
  const both = TIME_RE.test(from) && TIME_RE.test(to)
  return {
    date: dateOrEmpty(sp.get('date')),
    from: both ? from : '',
    to: both ? to : '',
    includeNew: sp.get('includeNew') !== 'false',
  }
}

export function buildPickListParams(f: PickListFilters): URLSearchParams {
  const sp = new URLSearchParams()
  if (f.date) sp.set('date', f.date)
  if (f.from && f.to) {
    sp.set('from', f.from)
    sp.set('to', f.to)
  }
  if (!f.includeNew) sp.set('includeNew', 'false')
  return sp
}

/** Values of the interval select: '' = whole day, 'HH:mm|HH:mm' = a grid slot, 'custom' = free «с — по». */
export function intervalKey(f: Pick<PickListFilters, 'from' | 'to'>, slots: { from: string; to: string }[]): string {
  if (!f.from || !f.to) return ''
  return slots.some((s) => s.from === f.from && s.to === f.to) ? `${f.from}|${f.to}` : 'custom'
}

// ── Notes / MAX
export const NOTE_MAX_LENGTH = 1000
/** The server counts the length after trimming (API_CONTRACT_CYCLE25.md §530). */
export function noteLength(text: string): number {
  return text.trim().length
}

/** MAX linking is polled only while a link is pending and not yet expired (§523.2, §538). */
export function shouldPollStaffMax(status: string | undefined, expiresAtUtc: string | null | undefined, nowMs: number): boolean {
  if (status !== 'Pending') return false
  if (!expiresAtUtc) return true
  return new Date(expiresAtUtc).getTime() > nowMs
}
