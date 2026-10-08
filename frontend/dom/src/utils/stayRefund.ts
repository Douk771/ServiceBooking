import type { StayBookingStatus, StayCancellationPolicy, StayRefundKind } from '../types'
import { zonedWallToUtcMs } from './stayDates'

/**
 * TS twin of the server's `StayRefund.Compute` (ЮР-1, ARCHITECTURE_CYCLE37.md §37.6.4), checked against stay-vectors.json
 * (`refund`). The server sends the amount AND the ready text («К возврату не меньше X ₽…»); the client never composes the wording.
 * This twin is used for one thing: knowing WHEN the amount changes (`refundBoundaryUtcMs`), so the booking page refetches exactly
 * then instead of showing a stale «Полный возврат» for up to a poll interval.
 */

export interface RefundInput {
  policy: StayCancellationPolicy
  status: StayBookingStatus
  cancelledBy: 'Guest' | 'Owner'
  prepayRub: number
  firstNightRub: number
  atUtcMs: number
  checkInDate: string
  checkInTime: string
  timeZoneId: string
}

export interface RefundResult {
  kind: StayRefundKind
  refundAtLeastRub: number
  maxDeductionRub: number
}

const NOTHING: RefundResult = { kind: 'NothingPaid', refundAtLeastRub: 0, maxDeductionRub: 0 }

/**
 * The instant after which the policy may deduct (`Standard`: 00:00 of the check-in date, `Flexible`: the check-in time),
 * null for `NoDeductions` (never).
 */
export function refundBoundaryUtcMs(
  policy: StayCancellationPolicy,
  checkInDate: string,
  checkInTime: string,
  timeZoneId: string,
): number | null {
  if (policy === 'NoDeductions') return null
  return zonedWallToUtcMs(checkInDate, policy === 'Standard' ? '00:00' : checkInTime, timeZoneId)
}

export function computeRefund(input: RefundInput): RefundResult {
  // Nothing was paid: a held booking, or a booking without a prepayment.
  if (input.status === 'Held' || input.prepayRub <= 0) return NOTHING
  if (input.cancelledBy === 'Owner') return { kind: 'Full', refundAtLeastRub: input.prepayRub, maxDeductionRub: 0 }
  const boundary = refundBoundaryUtcMs(input.policy, input.checkInDate, input.checkInTime, input.timeZoneId)
  if (boundary === null || input.atUtcMs < boundary) return { kind: 'Full', refundAtLeastRub: input.prepayRub, maxDeductionRub: 0 }
  const deduction = Math.min(input.prepayRub, input.firstNightRub)
  return { kind: 'Partial', refundAtLeastRub: input.prepayRub - deduction, maxDeductionRub: deduction }
}
