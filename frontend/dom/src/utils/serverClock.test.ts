// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { remainingMs, serverNowMs, serverOffsetMs } from './serverClock'
import { formatCountdown } from './stayDates'
import { isTerminal, statusTone } from './stayStatus'

describe('hold countdown on the server clock', () => {
  it('a phone clock 5 minutes fast does not shorten the hold', () => {
    // The server says it is 12:00:00; the phone thinks it is 12:05:00 at the moment of the answer.
    const received = Date.parse('2027-01-01T12:05:00Z')
    const offset = serverOffsetMs('2027-01-01T12:00:00Z', received)
    expect(offset).toBe(-5 * 60_000)
    // 30 minutes to the deadline by the server's clock, whatever the phone says.
    const left = remainingMs('2027-01-01T12:30:00Z', received, offset)
    expect(formatCountdown(left)).toBe('30:00')
    // A minute later on the phone, a minute less.
    expect(formatCountdown(remainingMs('2027-01-01T12:30:00Z', received + 60_000, offset))).toBe('29:00')
  })

  it('goes negative after the deadline and the countdown text stays 00:00', () => {
    const left = remainingMs('2027-01-01T12:30:00Z', Date.parse('2027-01-01T12:31:00Z'), 0)
    expect(left).toBe(-60_000)
    expect(formatCountdown(left)).toBe('00:00')
  })

  it('estimates the server now', () => {
    expect(serverNowMs(1_000, 250)).toBe(1_250)
  })
})

describe('status tone', () => {
  it('maps every display status, danger for the unsuccessful finals', () => {
    expect(statusTone('Held')).toBe('warning')
    expect(statusTone('AwaitingPaymentCheck')).toBe('info')
    expect(statusTone('Confirmed')).toBe('success')
    expect(statusTone('Completed')).toBe('muted')
    for (const s of ['ExpiredUnpaid', 'PaymentRejected', 'CancelledByGuest', 'CancelledByOwner'] as const) expect(statusTone(s)).toBe('danger')
  })
  it('terminal statuses stop the polling', () => {
    expect(isTerminal('Held')).toBe(false)
    expect(isTerminal('AwaitingPaymentCheck')).toBe(false)
    expect(isTerminal('Confirmed')).toBe(false)
    expect(isTerminal('Completed')).toBe(true)
    expect(isTerminal('ExpiredUnpaid')).toBe(true)
  })
})
