import type { StaffStayAction, StaffStayBookingCardDto, StayStaffConflictDto } from '../types'
import { readConflict } from './stayError'

/** Owner actions on a booking (API_CONTRACT_CYCLE37.md §37.30.3). The reason is shown to the GUEST — hence the wording. */
export const REASON_MAX = 300
export const REASON_REQUIRED_TEXT = 'Укажите причину — гость её увидит'

export function reasonProblem(reason: string): string | null {
  const r = reason.trim()
  return r.length >= 1 && r.length <= REASON_MAX ? null : REASON_REQUIRED_TEXT
}

export const ACTION_LABELS: Record<StaffStayAction, string> = {
  ConfirmPayment: 'Подтвердить оплату',
  RejectPayment: 'Отклонить оплату',
  Cancel: 'Отменить бронь',
}

export interface StaffConflictResult {
  /** The card as it is NOW: the action was not applied. */
  card: StaffStayBookingCardDto
  message: string
  code: StayStaffConflictDto['code']
}

/**
 * `VersionMismatch` / `InvalidTransition` (409): somebody else changed the booking first. The body carries the current card and the action
 * is NOT applied — the screen swaps to that card and says so, it never retries on its own.
 */
export function readStaffConflict(error: unknown): StaffConflictResult | null {
  const c = readConflict<StayStaffConflictDto>(error)
  if (!c || !c.booking || (c.code !== 'VersionMismatch' && c.code !== 'InvalidTransition')) return null
  return { card: c.booking, message: c.message, code: c.code }
}
