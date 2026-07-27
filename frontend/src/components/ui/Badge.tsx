import type { BookingStatus } from '../../types'

const statusConfig: Record<BookingStatus, { label: string; className: string }> = {
  Pending:   { label: 'Ожидает',   className: 'bg-warning-bg text-warning' },
  Confirmed: { label: 'Подтверждено', className: 'bg-success-bg text-success' },
  Cancelled: { label: 'Отменено',  className: 'bg-danger-bg text-danger' },
  Completed: { label: 'Завершено', className: 'bg-info-bg text-info' },
  NoShow:    { label: 'Не пришёл', className: 'bg-cream-deep text-muted' },
}

export function StatusBadge({ status }: { status: BookingStatus }) {
  const { label, className } = statusConfig[status]
  return (
    <span className={`inline-block px-3 py-1 rounded-full text-xs font-semibold ${className}`}>
      {label}
    </span>
  )
}
