import { describe, it, expect } from 'vitest'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { fmtDate, fmtDateTime } from './dateFormat'

describe('fmtDate', () => {
  it('formats an ISO date as "d MMM yyyy" in Russian', () => {
    expect(fmtDate('2026-03-05')).toBe('5 мар. 2026')
  })

  it('matches the inline format(parseISO(…), "d MMM yyyy", ru) it replaced', () => {
    const iso = '2026-12-31T23:15:00'
    expect(fmtDate(iso)).toBe(format(parseISO(iso), 'd MMM yyyy', { locale: ru }))
  })

  it('renders "—" for null, undefined and empty string', () => {
    expect(fmtDate(null)).toBe('—')
    expect(fmtDate(undefined)).toBe('—')
    expect(fmtDate('')).toBe('—')
  })
})

describe('fmtDateTime', () => {
  it('formats an ISO date-time as "d MMM yyyy, HH:mm" in Russian', () => {
    expect(fmtDateTime('2026-03-05T14:30:00')).toBe('5 мар. 2026, 14:30')
  })

  it('renders "—" for null, undefined and empty string', () => {
    expect(fmtDateTime(null)).toBe('—')
    expect(fmtDateTime(undefined)).toBe('—')
    expect(fmtDateTime('')).toBe('—')
  })
})
