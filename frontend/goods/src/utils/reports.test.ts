// @vitest-environment node
import { describe, it, expect } from 'vitest'
import {
  DEFAULT_HISTORY_FILTERS,
  buildHistoryParams,
  buildPickListParams,
  buildSummaryParams,
  hasExtraFilters,
  intervalKey,
  noteLength,
  parseHistoryParams,
  parsePickListParams,
  parseSummaryParams,
  shouldPollStaffMax,
  toHistoryQuery,
} from './reports'

describe('history filters <-> URL', () => {
  it('round-trips a full filter set', () => {
    const f = { ...DEFAULT_HISTORY_FILTERS, period: 'Custom' as const, from: '2026-09-01', to: '2026-09-30', statuses: ['Issued' as const, 'NotPickedUp' as const], amountFrom: '100', number: '27', sort: 'PickupAsc' as const, page: 3 }
    expect(parseHistoryParams(buildHistoryParams(f))).toEqual(f)
  })

  it('keeps the default screen on a clean address', () => {
    expect(buildHistoryParams(DEFAULT_HISTORY_FILTERS).toString()).toBe('')
  })

  it('never writes the buyer into the address', () => {
    expect(buildHistoryParams(DEFAULT_HISTORY_FILTERS).toString()).not.toContain('customer')
  })

  it('drops junk: unknown preset, bad dates, unknown statuses, bad page', () => {
    const f = parseHistoryParams(new URLSearchParams('period=Foo&from=x&statuses=Issued,Bogus,Issued&page=-2&sort=zzz'))
    expect(f.period).toBe('Last7Days')
    expect(f.from).toBe('')
    expect(f.statuses).toEqual(['Issued'])
    expect(f.page).toBe(1)
    expect(f.sort).toBe('PickupDesc')
  })

  it('does not put from/to into the address for a non-custom period', () => {
    const sp = buildHistoryParams({ ...DEFAULT_HISTORY_FILTERS, period: 'Today', from: '2026-09-01', to: '2026-09-02' })
    expect(sp.has('from')).toBe(false)
  })
})

describe('toHistoryQuery', () => {
  it('sends empty fields as null and the buyer only in the body', () => {
    const q = toHistoryQuery(DEFAULT_HISTORY_FILTERS, '  1234 ')
    expect(q).toMatchObject({ period: 'Last7Days', from: null, to: null, statuses: null, customer: '1234', amountFrom: null, amountTo: null, number: null, sort: 'PickupDesc', page: 1 })
  })

  it('parses amounts with a comma and truncates the number', () => {
    const q = toHistoryQuery({ ...DEFAULT_HISTORY_FILTERS, amountFrom: '99,5', number: '12.7' }, '')
    expect(q.amountFrom).toBe(99.5)
    expect(q.number).toBe(12)
    expect(q.customer).toBeNull()
  })

  it('sends from/to only for Custom', () => {
    expect(toHistoryQuery({ ...DEFAULT_HISTORY_FILTERS, period: 'Custom', from: '2026-09-01', to: '2026-09-05' }, '')).toMatchObject({ from: '2026-09-01', to: '2026-09-05' })
    expect(toHistoryQuery({ ...DEFAULT_HISTORY_FILTERS, period: 'Today', from: '2026-09-01', to: '2026-09-05' }, '').from).toBeNull()
  })

  it('hasExtraFilters ignores the period and sort', () => {
    expect(hasExtraFilters({ ...DEFAULT_HISTORY_FILTERS, period: 'Today', sort: 'PickupAsc' }, '')).toBe(false)
    expect(hasExtraFilters(DEFAULT_HISTORY_FILTERS, 'Анна')).toBe(true)
  })
})

describe('summary and pick list URL', () => {
  it('summary defaults to Today/Amount with a clean address', () => {
    expect(buildSummaryParams(parseSummaryParams(new URLSearchParams())).toString()).toBe('')
    expect(parseSummaryParams(new URLSearchParams('top=Quantity&period=ThisMonth'))).toMatchObject({ top: 'Quantity', period: 'ThisMonth' })
  })

  it('pick list requires both interval ends, includeNew defaults to true', () => {
    expect(parsePickListParams(new URLSearchParams('from=12:00'))).toMatchObject({ from: '', to: '', includeNew: true })
    const f = parsePickListParams(new URLSearchParams('date=2026-09-30&from=12:00&to=12:15&includeNew=false'))
    expect(f).toEqual({ date: '2026-09-30', from: '12:00', to: '12:15', includeNew: false })
    expect(buildPickListParams(f).toString()).toBe('date=2026-09-30&from=12%3A00&to=12%3A15&includeNew=false')
  })

  it('classifies the interval selection', () => {
    const slots = [{ from: '12:00', to: '12:15' }]
    expect(intervalKey({ from: '', to: '' }, slots)).toBe('')
    expect(intervalKey({ from: '12:00', to: '12:15' }, slots)).toBe('12:00|12:15')
    expect(intervalKey({ from: '12:00', to: '13:40' }, slots)).toBe('custom')
  })
})

describe('notes and MAX polling', () => {
  it('counts the note length after trimming', () => {
    expect(noteLength('  abc  ')).toBe(3)
  })

  it('polls only while Pending and not expired', () => {
    const now = Date.parse('2026-09-30T10:00:00Z')
    expect(shouldPollStaffMax('Pending', '2026-09-30T10:05:00Z', now)).toBe(true)
    expect(shouldPollStaffMax('Pending', '2026-09-30T09:59:00Z', now)).toBe(false)
    expect(shouldPollStaffMax('Linked', '2026-09-30T10:05:00Z', now)).toBe(false)
    expect(shouldPollStaffMax(undefined, null, now)).toBe(false)
  })
})
