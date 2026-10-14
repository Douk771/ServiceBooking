import type { StayDisplayStatus } from '@/types/slots'

/** Visual tone of a booking status. The text comes from the server (`statusText`); the colour only supports it, never replaces it. */
export type StatusTone = 'warning' | 'info' | 'success' | 'danger' | 'muted'

export function statusTone(status: StayDisplayStatus): StatusTone {
  switch (status) {
    case 'Held':
      return 'warning'
    case 'AwaitingPaymentCheck':
      return 'info'
    case 'Confirmed':
      return 'success'
    case 'Completed':
      return 'muted'
    case 'ExpiredUnpaid':
    case 'PaymentRejected':
    case 'CancelledByGuest':
    case 'CancelledByOwner':
      return 'danger'
  }
}

export const TONE_CLASSES: Record<StatusTone, string> = {
  warning: 'bg-warning-bg text-warning',
  info: 'bg-info-bg text-info',
  success: 'bg-success-bg text-success',
  danger: 'bg-danger-bg text-danger',
  muted: 'bg-cream-deep text-ink-soft',
}

/** Final statuses: the booking page stops polling, the owner's list moves it out of the working queue. */
export function isTerminal(status: StayDisplayStatus): boolean {
  return status === 'ExpiredUnpaid' || status === 'PaymentRejected' || status === 'CancelledByGuest' || status === 'CancelledByOwner' || status === 'Completed'
}

export const BOOKING_POLL_MS = 15_000
