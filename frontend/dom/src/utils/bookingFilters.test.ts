// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { ALL_STATUSES, DEFAULT_PRESET, PRESETS, isQueue, presetOf, statusesOf } from './bookingFilters'
import { ACTION_LABELS, REASON_REQUIRED_TEXT, reasonProblem, readStaffConflict } from './staffBookingActions'

describe('booking list presets', () => {
  it('the default is the owner’s queue: awaiting payment check', () => {
    expect(DEFAULT_PRESET).toBe('awaiting')
    expect(statusesOf('awaiting')).toEqual(['AwaitingPaymentCheck'])
    expect(isQueue('awaiting')).toBe(true)
    expect(isQueue('all')).toBe(false)
  })
  it('an unknown preset falls back to the default', () => {
    expect(presetOf('nope')).toBe('awaiting')
    expect(presetOf(null)).toBe('awaiting')
    expect(presetOf('closed')).toBe('closed')
  })
  it('the presets cover every status, none twice except in «Все»', () => {
    const union = new Set(PRESETS.filter((p) => p.id !== 'all').flatMap((p) => p.statuses))
    expect([...union].sort()).toEqual([...ALL_STATUSES].sort())
    expect(statusesOf('all')).toHaveLength(7)
  })
  it('every preset has an empty text', () => {
    for (const p of PRESETS) expect(p.empty.length).toBeGreaterThan(5)
  })
})

describe('owner actions', () => {
  it('a reason is required, 1–300 after trimming', () => {
    expect(reasonProblem('')).toBe(REASON_REQUIRED_TEXT)
    expect(reasonProblem('   ')).toBe(REASON_REQUIRED_TEXT)
    expect(reasonProblem('Платёж не поступил')).toBeNull()
    expect(reasonProblem('а'.repeat(300))).toBeNull()
    expect(reasonProblem('а'.repeat(301))).toBe(REASON_REQUIRED_TEXT)
  })
  it('names the three actions', () => {
    expect(ACTION_LABELS.ConfirmPayment).toBe('Подтвердить оплату')
    expect(ACTION_LABELS.RejectPayment).toBe('Отклонить оплату')
    expect(ACTION_LABELS.Cancel).toBe('Отменить бронь')
  })
  it('reads a version conflict with the current card; other 409 and strings are not it', () => {
    const card = { id: 'b1', version: 7 }
    const err = { isAxiosError: true, response: { status: 409, data: { code: 'VersionMismatch', message: 'Бронь уже изменена — проверьте актуальное состояние', booking: card } } }
    const r = readStaffConflict(err)
    expect(r?.code).toBe('VersionMismatch')
    expect(r?.card).toBe(card)
    expect(readStaffConflict({ isAxiosError: true, response: { status: 409, data: { code: 'InvalidTransition', message: 'x', booking: card } } })?.code).toBe('InvalidTransition')
    expect(readStaffConflict({ isAxiosError: true, response: { status: 409, data: 'строка' } })).toBeNull()
    expect(readStaffConflict({ isAxiosError: true, response: { status: 409, data: { code: 'SlugTaken', message: 'x' } } })).toBeNull()
    expect(readStaffConflict({ isAxiosError: true, response: { status: 500, data: '' } })).toBeNull()
  })
})
