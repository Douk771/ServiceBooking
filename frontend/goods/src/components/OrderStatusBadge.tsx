import type { OrderStatus } from '../types'

/** Colour is an addition, never the only signal (SPEC §6 accessibility): the server-built `statusText` is
 *  always printed. An unknown future status falls back to the neutral style. */
const styles: Record<OrderStatus, string> = {
  New: 'bg-warning-bg text-warning',
  Accepted: 'bg-info-bg text-info',
  Ready: 'bg-success-bg text-success',
  Issued: 'bg-cream-deep text-ink-soft',
  Rejected: 'bg-danger-bg text-danger',
  CancelledByCustomer: 'bg-danger-bg text-danger',
  CancelledByShop: 'bg-danger-bg text-danger',
  NotPickedUp: 'bg-cream-deep text-muted',
}

export function OrderStatusBadge({ status, text, large = false }: { status: OrderStatus; text: string; large?: boolean }) {
  return (
    <span
      className={`inline-block rounded-full font-semibold ${large ? 'px-4 py-1.5 text-sm' : 'px-3 py-1 text-xs'} ${styles[status] ?? 'bg-cream-deep text-ink-soft'}`}
    >
      {text}
    </span>
  )
}
