import { useState, useMemo } from 'react'
import { useNavigate } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { format, parseISO, differenceInHours } from 'date-fns'
import { ru } from 'date-fns/locale'
import { bookingsApi } from '../api/bookings'
import { reviewsApi } from '../api/reviews'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { StatusBadge } from '../components/ui/Badge'
import { ReviewModal } from '../components/review/ReviewModal'
import type { Booking } from '../types'

type FilterTab = 'all' | 'upcoming' | 'completed' | 'cancelled'

const TABS: { key: FilterTab; label: string }[] = [
  { key: 'all', label: 'Все' },
  { key: 'upcoming', label: 'Предстоящие' },
  { key: 'completed', label: 'Завершённые' },
  { key: 'cancelled', label: 'Отменённые' },
]

function canCancelBooking(b: Booking): boolean {
  if (b.status !== 'Pending' && b.status !== 'Confirmed') return false
  const bookingDateTime = parseISO(`${b.date}T${b.startTime}`)
  return differenceInHours(bookingDateTime, new Date()) > 2
}

export function ClientBookingsPage() {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const [tab, setTab] = useState<FilterTab>('all')
  const [reviewBooking, setReviewBooking] = useState<Booking | null>(null)

  const statusParam = tab === 'upcoming' ? 'upcoming' : tab === 'completed' ? 'Completed' : tab === 'cancelled' ? 'Cancelled' : undefined

  const { data: bookings, isLoading } = useQuery({
    queryKey: ['client-bookings', tab],
    queryFn: () => bookingsApi.getClientBookings(statusParam),
  })

  const { data: canReviewList } = useQuery({
    queryKey: ['can-review'],
    queryFn: reviewsApi.canReview,
  })

  const canReviewSet = useMemo(() => new Set((canReviewList ?? []).map(r => r.bookingId)), [canReviewList])

  const cancel = useMutation({
    mutationFn: (id: string) => bookingsApi.cancel(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['client-bookings'] }),
  })

  // Group by month
  const grouped = useMemo(() => {
    if (!bookings) return []
    const map = new Map<string, Booking[]>()
    for (const b of bookings) {
      const key = format(parseISO(b.date), 'yyyy-MM')
      const arr = map.get(key) ?? []
      arr.push(b)
      map.set(key, arr)
    }
    return Array.from(map.entries())
      .sort(([a], [b]) => b.localeCompare(a)) // newest first
      .map(([key, items]) => ({
        label: format(parseISO(`${key}-01`), 'LLLL yyyy', { locale: ru }),
        items,
      }))
  }, [bookings])

  return (
    <div className="max-w-3xl mx-auto px-4 py-8">
      <h1 className="text-2xl font-bold text-gray-900 mb-6">Мои визиты</h1>

      {/* Filter tabs */}
      <div className="flex gap-1 bg-gray-100 rounded-xl p-1 mb-6 w-fit">
        {TABS.map(t => (
          <button
            key={t.key}
            onClick={() => setTab(t.key)}
            className={`px-4 py-1.5 rounded-lg text-sm font-medium transition-colors ${
              tab === t.key
                ? 'bg-white text-primary-600 shadow-sm'
                : 'text-gray-500 hover:text-gray-700'
            }`}
          >
            {t.label}
          </button>
        ))}
      </div>

      {isLoading ? (
        <div className="grid gap-4">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="h-24 bg-gray-100 rounded-2xl animate-pulse" />
          ))}
        </div>
      ) : grouped.length > 0 ? (
        <div className="grid gap-8">
          {grouped.map(group => (
            <div key={group.label}>
              <h3 className="text-sm font-semibold text-gray-500 uppercase tracking-wide mb-3 capitalize">
                {group.label}
              </h3>
              <div className="grid gap-3">
                {group.items.map(b => (
                  <Card key={b.id} className="p-4">
                    <div className="flex items-start gap-4">
                      {/* Date/time block */}
                      <div className="text-center bg-orange-50 rounded-xl px-3 py-2 min-w-[64px] shrink-0">
                        <div className="text-xs text-gray-500">
                          {format(parseISO(b.date), 'd MMM', { locale: ru })}
                        </div>
                        <div className="text-lg font-bold text-primary-600">{b.startTime.slice(0, 5)}</div>
                      </div>

                      {/* Main info */}
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 flex-wrap">
                          <span className="font-semibold text-gray-900">{b.serviceName}</span>
                          <StatusBadge status={b.status} />
                        </div>
                        {b.companyName && (
                          <p className="text-sm text-gray-500 mt-0.5">🏢 {b.companyName}</p>
                        )}
                        <p className="text-sm text-gray-500">👤 {b.masterName}</p>
                        {b.price != null && (
                          <p className="text-sm font-medium text-primary-600 mt-1">
                            {b.price.toLocaleString('ru-RU')} ₽
                          </p>
                        )}
                      </div>

                      {/* Action buttons */}
                      <div className="flex flex-col gap-2 shrink-0 items-end">
                        {canCancelBooking(b) && (
                          <Button
                            size="sm"
                            variant="danger"
                            loading={cancel.isPending}
                            onClick={() => cancel.mutate(b.id)}
                          >
                            Отменить
                          </Button>
                        )}
                        {b.status === 'Completed' && canReviewSet.has(b.id) && (
                          <Button
                            size="sm"
                            variant="secondary"
                            onClick={() => setReviewBooking(b)}
                          >
                            ⭐ Оставить отзыв
                          </Button>
                        )}
                        {b.companySlug && (
                          <Button
                            size="sm"
                            variant="ghost"
                            onClick={() => navigate(`/company/${b.companySlug}`)}
                          >
                            Записаться снова
                          </Button>
                        )}
                      </div>
                    </div>
                  </Card>
                ))}
              </div>
            </div>
          ))}
        </div>
      ) : (
        <div className="text-center py-16 text-gray-400">
          <p className="text-4xl mb-3">📋</p>
          <p className="text-lg mb-4">Записей пока нет</p>
          <Button onClick={() => navigate('/')}>Найти мастера</Button>
        </div>
      )}

      {reviewBooking && (
        <ReviewModal
          bookingId={reviewBooking.id}
          serviceName={reviewBooking.serviceName}
          masterName={reviewBooking.masterName}
          companyId={reviewBooking.companyId}
          onClose={() => setReviewBooking(null)}
          onSuccess={() => setReviewBooking(null)}
        />
      )}
    </div>
  )
}
