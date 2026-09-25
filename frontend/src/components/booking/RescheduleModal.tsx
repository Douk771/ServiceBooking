import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { format, addDays, isTomorrow } from 'date-fns'
import { ru } from 'date-fns/locale'
import { bookingsApi } from '../../api/bookings'
import { Button } from '../ui/Button'
import { Icon } from '../ui/Icon'
import { useOverlayDismiss } from '../../hooks/useOverlayDismiss'
import { getBookingErrorMessage } from '../../utils/bookingError'
import { getClientRescheduleErrorMessage } from '../../utils/clientRescheduleError'
import { formatBookingServiceNames } from '../../utils/bookingServices'
import type { Booking } from '../../types'

interface Props {
  booking: Booking
  onClose: () => void
  /**
   * ARCHITECTURE_CYCLE15.md §286 — how many days ahead the date grid offers. Staff (default) keeps
   * the existing 14-day window; the client-owner path (`ClientBookingsPage`) passes
   * `booking.companyBookingHorizonDays` so the grid never offers a date the server would 400 on
   * (§287.2 п. 7).
   */
  horizonDays?: number
  /**
   * ARCHITECTURE_CYCLE15.md §287.2 п. 8 — the client-owner reschedule rule only ever resolves the
   * SAME staff grid a client could book into themselves (`ScheduleFallback.None`); "outside working
   * hours"/"ignore the schedule entirely" is a staff-only escape hatch (`manual`/`extendedHours`
   * query params), and the endpoint doesn't accept it from a client at all. Hiding the toggle here
   * keeps the client UI from offering a choice the server will always 409/ignore.
   */
  allowManualOverride?: boolean
  /**
   * ARCHITECTURE_CYCLE17.md §305.1/§310 (US-17-01, C15-6.2) — hours before the visit the company
   * requires for a client self-reschedule (`Company.clientRescheduleMinHours`). Staff (default `0`)
   * keeps today's behaviour byte for byte — the grid only ever filters against "now" the same as
   * before this cycle. The client-owner path passes `booking.clientRescheduleMinHours`. This is a
   * hint only: the server re-validates on the actual PATCH (§257.4 cycle 15) regardless of what the
   * grid offered.
   */
  minHours?: number
  /** Called after a successful PATCH, in addition to the standard `master-bookings` invalidation —
   *  `ClientBookingsPage` uses this to invalidate `client-bookings` instead. */
  onRescheduled?: () => void
  /** True when this modal is the client-owner path (§287.1/§287.2) — selects the error message
   *  source (server-composed §287.2 text vs. the staff path's fixed copy). */
  isClientOwner?: boolean
}

export function RescheduleModal({
  booking,
  onClose,
  horizonDays = 14,
  minHours = 0,
  allowManualOverride = true,
  onRescheduled,
  isClientOwner = false,
}: Props) {
  const qc = useQueryClient()

  // US-67: after multi-service visits, every service of the booking counts toward duration —
  // same rule `BookingModal` uses for the primary/extra split.
  const extraServiceIds = (booking.services ?? []).slice(1).map((s) => s.serviceId)

  const now = new Date()
  const todayStr = format(now, 'yyyy-MM-dd')
  // §305.1 — the ONE boundary applied to both the day grid and the slot grid, replacing the old
  // "later than the current minute" check (which is what this reduces to when minHours = 0).
  const earliestAllowed = new Date(now.getTime() + minHours * 3600_000)

  const [selectedDate, setSelectedDate] = useState('')
  const [selectedTime, setSelectedTime] = useState('')
  // F7 (ARCHITECTURE_CYCLE6.md §46.4) — same "show the rest of the hours" toggle as
  // `BookingModal`'s staff slot step, on the same rule: 09:00–21:00 is only the default, not a boundary.
  const [showExtendedHours, setShowExtendedHours] = useState(false)

  // Today is only offered as a reschedule target if the server still has at least one slot left
  // for it — mirrors `BookingModal`'s "slots today" check, now via the same endpoint.
  const { data: slotsToday = [] } = useQuery({
    queryKey: ['slots', booking.companyId, booking.masterId, booking.serviceId, extraServiceIds, todayStr, !isClientOwner, showExtendedHours, booking.id],
    queryFn: () =>
      bookingsApi.getSlots(
        booking.companyId,
        booking.masterId,
        booking.serviceId,
        extraServiceIds,
        todayStr,
        // §290 — `manual`/`extendedHours` are a STAFF request the server only honours for staff of
        // this company; the client interface doesn't send them at all.
        !isClientOwner,
        showExtendedHours,
        booking.id,
      ),
    staleTime: 0,
  })
  const hasAvailableSlotToday = slotsToday.some((s) => new Date(`${todayStr}T${s.start}`) > earliestAllowed)

  // §305.1 — a day is offered only if it has at least one moment later than earliestAllowed (its
  // end-of-day, for the horizon loop below — the actual slot grid still narrows further once a day
  // is picked). At minHours = 0 this keeps every future day, same as before this cycle.
  const days = [
    ...(hasAvailableSlotToday ? [{ value: todayStr, label: 'Сегодня' }] : []),
    ...Array.from({ length: horizonDays }, (_, i) => {
      const d = addDays(now, i + 1)
      return {
        value: format(d, 'yyyy-MM-dd'),
        label: isTomorrow(d) ? 'Завтра' : format(d, 'd MMM, EEE', { locale: ru }),
      }
    }).filter((d) => new Date(`${d.value}T23:59:59`) > earliestAllowed),
  ]

  const {
    data: rawSlots,
    isLoading: slotsLoading,
    error: slotsError,
  } = useQuery({
    queryKey: ['slots', booking.companyId, booking.masterId, booking.serviceId, extraServiceIds, selectedDate, !isClientOwner, showExtendedHours, booking.id],
    queryFn: () =>
      bookingsApi.getSlots(
        booking.companyId,
        booking.masterId,
        booking.serviceId,
        extraServiceIds,
        selectedDate,
        !isClientOwner,
        showExtendedHours,
        booking.id,
      ),
    enabled: !!selectedDate,
    staleTime: 0,
    retry: false,
  })
  const slots = (rawSlots ?? []).filter((s) => new Date(`${selectedDate}T${s.start}`) > earliestAllowed)

  const mutation = useMutation({
    mutationFn: () => bookingsApi.reschedule(booking.id, selectedDate, selectedTime),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['master-bookings'] })
      onRescheduled?.()
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
            {/* API_CONTRACT_CYCLE15.md §291 п. 7 — the client already knows their own name; what they
                need here is WHOSE visit is being moved (salon · master · service). Staff keeps the
                client's name, which is the useful identifier on their side. */}
            <p className="text-[13px] text-ink-soft">
              {(isClientOwner
                ? [booking.companyName, booking.masterName, formatBookingServiceNames(booking)]
                : [booking.clientName, formatBookingServiceNames(booking)]
              )
                .filter(Boolean)
                .join(' · ')}
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
                {allowManualOverride && (
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
                )}
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
                // Same reader `BookingModal` uses on the same endpoint: GET /api/bookings/slots
                // answers with a bare string that says WHY there is no grid ("Мастер не оказывает
                // услугу: …", «Услуга сейчас недоступна», the rate limit) — a fixed "попробуйте
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
            // §287.2 — client-owner reschedules use the server's own composed text (hour/day limits
            // vary per company); staff keeps its existing fixed "slot taken" copy.
            <p className="text-sm text-danger text-center mt-3">
              {isClientOwner ? getClientRescheduleErrorMessage(mutation.error) : 'Время уже занято. Выберите другое.'}
            </p>
          )}
        </div>
      </div>
    </div>
  )
}
