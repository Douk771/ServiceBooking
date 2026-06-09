import type { BookingStatus } from '../../types'

const statusConfig: Record<BookingStatus, { label: string; className: string }> = {
  Pending:   { label: 'Ожидает',   className: 'bg-yellow-100 text-yellow-700' },
  Confirmed: { label: 'Подтверждено', className: 'bg-green-100 text-green-700' },
  Cancelled: { label: 'Отменено',  className: 'bg-red-100 text-red-600' },
  Completed: { label: 'Завершено', className: 'bg-blue-100 text-blue-700' },
  NoShow:    { label: 'Не пришёл', className: 'bg-gray-100 text-gray-600' },
}

export function StatusBadge({ status }: { status: BookingStatus }) {
  const { label, className } = statusConfig[status]
  return (
    <span className={`inline-block px-2.5 py-0.5 rounded-full text-xs font-medium ${className}`}>
      {label}
    </span>
  )
}
