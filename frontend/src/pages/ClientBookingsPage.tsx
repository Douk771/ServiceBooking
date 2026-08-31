import { useState, useMemo } from 'react'
import { useNavigate } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { format, parseISO, differenceInHours } from 'date-fns'
import { ru } from 'date-fns/locale'
import { bookingsApi } from '../api/bookings'
import { reviewsApi } from '../api/reviews'
import { Button } from '../components/ui/Button'
import { StatusBadge } from '../components/ui/Badge'
import { Icon } from '../components/ui/Icon'
import { ReviewModal } from '../components/review/ReviewModal'
import { getBookingErrorMessage } from '../utils/bookingError'
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

  const [cancelError, setCancelError] = useState('')

  const cancel = useMutation({
    mutationFn: (id: string) => bookingsApi.cancel(id),
    onSuccess: () => { setCancelError(''); qc.invalidateQueries({ queryKey: ['client-bookings'] }) },
    onError: (err) => setCancelError(getBookingErrorMessage(err)),
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
    <div className="max-w-[760px] mx-auto px-8 pt-12 pb-24">
      <h1 className="font-serif text-[30px] font-medium text-ink mb-7">Мои визиты</h1>

      {/* Filter tabs */}
      <div className="inline-flex gap-1 bg-cream-deep p-1 rounded-full mb-8">
        {TABS.map(t => (
          <button
            key={t.key}
            onClick={() => setTab(t.key)}
            className={`px-[18px] py-[9px] rounded-full text-[13.5px] font-semibold transition-colors ${
              tab === t.key
                ? 'bg-white text-ink shadow-sm'
                : 'text-gold-dark hover:text-ink'
            }`}
          >
            {t.label}
          </button>
        ))}
      </div>

      {cancelError && (
        <div className="mb-6 rounded-xl bg-danger-bg text-danger text-sm px-4 py-3 flex items-center gap-2">
          <Icon name="alert-circle" size={15} strokeWidth={1.8} /> {cancelError}
        </div>
      )}

      {isLoading ? (
        <div className="grid gap-4">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="h-24 bg-cream-deep rounded-2xl animate-pulse" />
          ))}
        </div>
      ) : grouped.length > 0 ? (
        <div className="flex flex-col gap-7">
          {grouped.map(group => (
            <div key={group.label}>
              <h3 className="text-[12.5px] font-bold tracking-[0.05em] uppercase text-muted mb-3 capitalize">
                {group.label}
              </h3>
              <div className="flex flex-col gap-2.5">
                {group.items.map(b => (
                  <div key={b.id} className="bg-white border border-line rounded-[18px] px-5 py-[18px] flex items-center gap-4 flex-wrap">
                    {/* Date/time block */}
                    <div className="text-center bg-cream-deep rounded-[14px] px-3.5 py-2.5 min-w-[66px] shrink-0">
                      <div className="text-[11.5px] text-gold-dark">
                        {format(parseISO(b.date), 'd MMM', { locale: ru })}
                      </div>
                      <div className="text-[17px] font-bold text-ink">{b.startTime.slice(0, 5)}</div>
                    </div>

                    {/* Main info */}
                    <div className="flex-1 min-w-[180px]">
                      <div className="flex items-center gap-2 flex-wrap">
                        <span className="font-semibold text-[14.5px] text-ink">{b.serviceName}</span>
                        <StatusBadge status={b.status} />
                      </div>
                      {b.companyName && (
                        <p className="text-[13px] text-ink-soft mt-1">{b.companyName} · {b.masterName}</p>
                      )}
                      {b.price != null && (
                        <p className="text-[13px] font-semibold text-gold-dark mt-0.5">
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
                          <Icon name="star" size={12} className="text-gold-dark" />
                          Оставить отзыв
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
                ))}
              </div>
            </div>
          ))}
        </div>
      ) : (
        <div className="text-center py-16 text-muted">
          <Icon name="calendar" size={36} strokeWidth={1.4} className="mx-auto mb-3" />
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
