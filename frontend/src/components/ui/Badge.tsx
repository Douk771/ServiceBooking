import type { BookingStatus } from '../../types'
import { BOOKING_STATUS_LABELS } from '../../utils/bookingStatus'

const statusClassName: Record<BookingStatus, string> = {
  Pending: 'bg-warning-bg text-warning',
  Confirmed: 'bg-success-bg text-success',
  Cancelled: 'bg-danger-bg text-danger',
  Completed: 'bg-info-bg text-info',
  NoShow: 'bg-cream-deep text-muted',
}

export function StatusBadge({ status }: { status: BookingStatus }) {
  return (
    <span className={`inline-block px-3 py-1 rounded-full text-xs font-semibold ${statusClassName[status]}`}>
      {BOOKING_STATUS_LABELS[status]}
    </span>
  )
}
