import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { format, addDays, isTomorrow } from 'date-fns'
import { ru } from 'date-fns/locale'
import { bookingsApi } from '../../api/bookings'
import { Button } from '../ui/Button'
import { Icon } from '../ui/Icon'
import { useOverlayDismiss } from '../../hooks/useOverlayDismiss'
import { getBookingErrorMessage } from '../../utils/bookingError'
import { formatBookingServiceNames } from '../../utils/bookingServices'
import type { Booking } from '../../types'

interface Props {
  booking: Booking
  onClose: () => void
}

function timeToMinutes(t: string): number {
  const [h, m] = t.slice(0, 5).split(':').map(Number)
  return h * 60 + m
}

export function RescheduleModal({ booking, onClose }: Props) {
  const qc = useQueryClient()

  // US-67: after multi-service visits, every service of the booking counts toward duration —
  // same rule `ManualBookingModal`/`BookingModal` use for the primary/extra split.
  const extraServiceIds = (booking.services ?? []).slice(1).map((s) => s.serviceId)

  const now = new Date()
  const todayStr = format(now, 'yyyy-MM-dd')
  const nowMinutes = now.getHours() * 60 + now.getMinutes()

  const [selectedDate, setSelectedDate] = useState('')
  const [selectedTime, setSelectedTime] = useState('')
  // F7 (ARCHITECTURE_CYCLE6.md §46.4) — same "show the rest of the hours" toggle as
  // `ManualBookingModal`, on the same rule: 09:00–21:00 is only the default, not a boundary.
  const [showExtendedHours, setShowExtendedHours] = useState(false)

  // Today is only offered as a reschedule target if the server still has at least one slot left
  // for it — mirrors `ManualBookingModal`'s "slots today" check, now via the same endpoint.
  const { data: slotsToday = [] } = useQuery({
    queryKey: ['slots', booking.companyId, booking.masterId, booking.serviceId, extraServiceIds, todayStr, 'manual', showExtendedHours, booking.id],
    queryFn: () =>
      bookingsApi.getSlots(
        booking.companyId,
        booking.masterId,
        booking.serviceId,
        extraServiceIds,
        todayStr,
        true,
        showExtendedHours,
        booking.id,
      ),
    staleTime: 0,
  })
  const hasAvailableSlotToday = slotsToday.some((s) => timeToMinutes(s.start) > nowMinutes)

  const days = [
    ...(hasAvailableSlotToday ? [{ value: todayStr, label: 'Сегодня' }] : []),
    ...Array.from({ length: 14 }, (_, i) => {
      const d = addDays(now, i + 1)
      return {
        value: format(d, 'yyyy-MM-dd'),
        label: isTomorrow(d) ? 'Завтра' : format(d, 'd MMM, EEE', { locale: ru }),
      }
    }),
  ]

  const {
    data: rawSlots,
    isLoading: slotsLoading,
    error: slotsError,
  } = useQuery({
    queryKey: ['slots', booking.companyId, booking.masterId, booking.serviceId, extraServiceIds, selectedDate, 'manual', showExtendedHours, booking.id],
    queryFn: () =>
      bookingsApi.getSlots(
        booking.companyId,
        booking.masterId,
        booking.serviceId,
        extraServiceIds,
        selectedDate,
        true,
        showExtendedHours,
        booking.id,
      ),
    enabled: !!selectedDate,
    staleTime: 0,
    retry: false,
  })
  const slots = (rawSlots ?? []).filter((s) => selectedDate !== todayStr || timeToMinutes(s.start) > nowMinutes)

  const mutation = useMutation({
    mutationFn: () => bookingsApi.reschedule(booking.id, selectedDate, selectedTime),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['master-bookings'] })
      onClose()
    },
  })

  const dismiss = useOverlayDismiss(onClose)

  return (
    <div className="fixed inset-0 bg-ink/45 backdrop-blur-sm z-50 flex items-center justify-center p-5" {...dismiss}>
      <div className="bg-cream rounded-[26px] shadow-modal w-full max-w-[440px] max-h-[88vh] overflow-y-auto">
        <div className="p-6 pb-[22px] border-b border-line flex items-center justify-between">
          <div>
            <h2 className="font-serif text-[19px] font-medium text-ink mb-0.5">Перенести запись</h2>
            <p className="text-[13px] text-ink-soft">
              {booking.clientName} · {formatBookingServiceNames(booking)}
            </p>
          </div>
          <button onClick={onClose} className="text-muted hover:text-ink shrink-0">
            <Icon name="x" size={18} strokeWidth={1.8} />
          </button>
        </div>

        <div className="p-6 pt-[22px]">
          {/* Date grid */}
          <h3 className="text-[14.5px] font-semibold text-[#4A4038] mb-3">Новая дата</h3>
          <div className="grid grid-cols-2 gap-2 mb-6">
            {days.map((d) => (
              <button
                key={d.value}
                onClick={() => {
                  setSelectedDate(d.value)
                  setSelectedTime('')
                }}
                className={`px-3.5 py-3 rounded-xl border text-[13.5px] transition-all text-left ${
                  selectedDate === d.value
                    ? 'bg-ink text-cream border-ink'
                    : 'border-line bg-white hover:border-line-strong'
                }`}
              >
                {d.label}
              </button>
            ))}
          </div>

          {/* Time grid */}
          {selectedDate && (
            <>
              <div className="flex items-center justify-between mb-3">
                <h3 className="text-[14.5px] font-semibold text-[#4A4038]">Новое время</h3>
                <button
                  type="button"
                  onClick={() => {
                    setShowExtendedHours((v) => !v)
                    setSelectedTime('')
                  }}
                  className="text-[12.5px] font-medium text-gold-dark hover:underline"
                >
                  {showExtendedHours ? 'Скрыть остальные часы' : 'Показать остальные часы'}
                </button>
              </div>
              {showExtendedHours && (
                <p className="text-[12px] text-ink-soft -mt-1.5 mb-3">
                  Мастер в это время не работает — запись вне графика.
                </p>
              )}
              {slotsLoading ? (
                <div className="grid grid-cols-4 gap-2 mb-6">
                  {Array.from({ length: 8 }).map((_, i) => (
                    <div key={i} className="h-9 bg-cream-deep rounded-xl animate-pulse" />
                  ))}
                </div>
              ) : slots.length > 0 ? (
                <div className="grid grid-cols-4 gap-2 mb-6">
                  {slots.map((s) => {
                    const time = s.start.slice(0, 5)
                    const isSelected = selectedTime === time
                    return (
                      <button
                        key={time}
                        onClick={() => setSelectedTime(isSelected ? '' : time)}
                        className={`py-2 rounded-xl text-[13.5px] font-medium border transition-all ${
                          isSelected ? 'bg-ink text-cream border-ink' : 'border-line bg-white hover:border-line-strong'
                        }`}
                      >
                        {time}
                      </button>
                    )
                  })}
                </div>
              ) : slotsError ? (
                // Same reader ManualBookingModal uses on the same endpoint: GET /api/bookings/slots
                // answers with a bare string that says WHY there is no grid ("Мастер не оказывает
                // услугу: …", "Service is not available", the rate limit) — a fixed "попробуйте
                // снова" throws that away and leaves the operator guessing, which is the exact
                // failure mode US-60 was about.
                <p className="text-center text-danger py-4 text-sm mb-6">{getBookingErrorMessage(slotsError)}</p>
              ) : (
                <p className="text-center text-muted py-4 text-sm mb-6">Нет доступных слотов на этот день</p>
              )}
            </>
          )}

          <Button
            size="lg"
            className="w-full"
            disabled={!selectedDate || !selectedTime}
            loading={mutation.isPending}
            onClick={() => mutation.mutate()}
          >
            Перенести
          </Button>

          {mutation.isError && (
            <p className="text-sm text-danger text-center mt-3">Время уже занято. Выберите другое.</p>
          )}
        </div>
      </div>
    </div>
  )
}
