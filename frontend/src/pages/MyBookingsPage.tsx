import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { bookingsApi } from '../api/bookings'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { StatusBadge } from '../components/ui/Badge'
import type { Booking } from '../types'

function BookingCard({ booking }: { booking: Booking }) {
  const qc = useQueryClient()
  const cancel = useMutation({
    mutationFn: () => bookingsApi.cancel(booking.id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['my-bookings'] }),
  })

  return (
    <Card className="p-5">
      <div className="flex items-start justify-between gap-4">
        <div>
          <div className="flex items-center gap-2 mb-1">
            <h3 className="font-semibold text-gray-900">{booking.serviceName}</h3>
            <StatusBadge status={booking.status} />
          </div>
          <p className="text-sm text-gray-500">
            👤 {booking.masterName} · 📅 {format(parseISO(booking.date), 'd MMM yyyy, EEE', { locale: ru })} · ⏰ {booking.startTime.slice(0, 5)}–{booking.endTime.slice(0, 5)}
          </p>
        </div>
        {(booking.status === 'Pending' || booking.status === 'Confirmed') && (
          <Button
            variant="danger"
            size="sm"
            loading={cancel.isPending}
            onClick={() => cancel.mutate()}
          >
            Отменить
          </Button>
        )}
      </div>
    </Card>
  )
}

export function MyBookingsPage() {
  const { data: bookings, isLoading } = useQuery({
    queryKey: ['my-bookings'],
    queryFn: bookingsApi.getMyBookings,
  })

  return (
    <div className="max-w-3xl mx-auto px-4 py-8">
      <h1 className="text-2xl font-bold text-gray-900 mb-6">Мои записи</h1>
      {isLoading ? (
        <div className="grid gap-4">
          {Array.from({ length: 3 }).map((_, i) => <div key={i} className="h-20 bg-gray-100 rounded-2xl animate-pulse" />)}
        </div>
      ) : bookings && bookings.length > 0 ? (
        <div className="grid gap-4">
          {bookings.map((b) => <BookingCard key={b.id} booking={b} />)}
        </div>
      ) : (
        <div className="text-center py-16 text-gray-400">
          <p className="text-4xl mb-3">📋</p>
          <p className="text-lg">Записей пока нет</p>
        </div>
      )}
    </div>
  )
}
