// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { computeRefund, type RefundInput } from './stayRefund'
import type { StayBookingStatus } from '../types'

const base: RefundInput = {
  policy: 'Standard',
  status: 'Confirmed',
  cancelledBy: 'Guest',
  prepayRub: 5000,
  firstNightRub: 4500,
  atUtcMs: Date.parse('2027-03-01T00:00:00Z'),
  checkInDate: '2027-03-10',
  checkInTime: '14:00',
  timeZoneId: 'Asia/Novokuznetsk',
}

describe('computeRefund — final statuses', () => {
  const terminal: StayBookingStatus[] = ['ExpiredUnpaid', 'PaymentRejected', 'CancelledByGuest', 'CancelledByOwner']
  for (const status of terminal) {
    for (const cancelledBy of ['Guest', 'Owner'] as const) {
      it(`${status} / ${cancelledBy}: nothing is promised`, () => {
        expect(computeRefund({ ...base, status, cancelledBy })).toEqual({ kind: 'NothingPaid', refundAtLeastRub: 0, maxDeductionRub: 0 })
      })
    }
  }

  it('a live paid booking still gets the full refund before the boundary', () => {
    expect(computeRefund(base)).toEqual({ kind: 'Full', refundAtLeastRub: 5000, maxDeductionRub: 0 })
  })

  it('a held booking has nothing paid', () => {
    expect(computeRefund({ ...base, status: 'Held' }).kind).toBe('NothingPaid')
  })
})
