import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { format, addDays, isTomorrow } from 'date-fns'
import { ru } from 'date-fns/locale'
import { bookingsApi } from '../../api/bookings'
import { Button } from '../ui/Button'
import { Icon } from '../ui/Icon'
import { useOverlayDismiss } from '../../hooks/useOverlayDismiss'
import type { Booking } from '../../types'

interface OccupiedRange { start: string; end: string }

interface Props {
  booking: Booking
  onClose: () => void
}

function generateTimeGrid(): string[] {
  const times: string[] = []
  for (let h = 8; h <= 21; h++) {
    times.push(`${String(h).padStart(2, '0')}:00`)
    if (h < 21) times.push(`${String(h).padStart(2, '0')}:30`)
  }
  return times
}

function timeToMinutes(t: string): number {
  const [h, m] = t.split(':').map(Number)
  return h * 60 + m
}

function durationMinutes(start: string, end: string): number {
  return timeToMinutes(end.slice(0, 5)) - timeToMinutes(start.slice(0, 5))
}

function isTimeOccupied(time: string, durationMin: number, occupied: OccupiedRange[], currentStart: string): boolean {
  const start = timeToMinutes(time)
  const end = start + durationMin
  return occupied.some((r) => {
    // Skip the current booking's own slot
    if (r.start.slice(0, 5) === currentStart.slice(0, 5)) return false
    const rStart = timeToMinutes(r.start.slice(0, 5))
    const rEnd = timeToMinutes(r.end.slice(0, 5))
    return start < rEnd && end > rStart
  })
}

const TIME_GRID = generateTimeGrid()

export function RescheduleModal({ booking, onClose }: Props) {
  const qc = useQueryClient()
  const duration = durationMinutes(booking.startTime, booking.endTime)

  const now = new Date()
  const todayStr = format(now, 'yyyy-MM-dd')
  const nowMinutes = now.getHours() * 60 + now.getMinutes()

  // Today is only offered as a reschedule target if at least one grid slot both hasn't
  // passed yet and isn't already booked — otherwise there's nothing left to pick today.
  const { data: occupiedToday = [] } = useQuery<OccupiedRange[]>({
    queryKey: ['occupied', booking.masterId, todayStr],
    queryFn: () => bookingsApi.getOccupied(booking.masterId, todayStr),
    staleTime: 0,
  })
  const hasAvailableSlotToday = TIME_GRID.some((time) =>
    timeToMinutes(time) > nowMinutes && !isTimeOccupied(time, duration, occupiedToday, booking.startTime)
  )

  const days = [
    ...(hasAvailableSlotToday ? [{ value: todayStr, label: 'Сегодня' }] : []),
    ...Array.from({ length: 14 }, (_, i) => {
      const d = addDays(now, i + 1)
      return { value: format(d, 'yyyy-MM-dd'), label: isTomorrow(d) ? 'Завтра' : format(d, 'd MMM, EEE', { locale: ru }) }
    }),
  ]

  const [selectedDate, setSelectedDate] = useState('')
  const [selectedTime, setSelectedTime] = useState('')

  const { data: occupied = [] } = useQuery<OccupiedRange[]>({
    queryKey: ['occupied', booking.masterId, selectedDate],
    queryFn: () => bookingsApi.getOccupied(booking.masterId, selectedDate),
    enabled: !!selectedDate,
    staleTime: 0,
  })

  const mutation = useMutation({
    mutationFn: () => bookingsApi.reschedule(booking.id, selectedDate, selectedTime),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['master-bookings'] })
      onClose()
    },
  })

  const dismiss = useOverlayDismiss(onClose)

  return (
    <div
      className="fixed inset-0 bg-ink/45 backdrop-blur-sm z-50 flex items-center justify-center p-5"
      {...dismiss}
    >
      <div className="bg-cream rounded-[26px] shadow-modal w-full max-w-[440px] max-h-[88vh] overflow-y-auto">
        <div className="p-6 pb-[22px] border-b border-line flex items-center justify-between">
          <div>
            <h2 className="font-serif text-[19px] font-medium text-ink mb-0.5">Перенести запись</h2>
            <p className="text-[13px] text-ink-soft">
              {booking.clientName} · {booking.serviceName}
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
                onClick={() => { setSelectedDate(d.value); setSelectedTime('') }}
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
              <h3 className="text-[14.5px] font-semibold text-[#4A4038] mb-3">Новое время</h3>
              <div className="grid grid-cols-4 gap-2 mb-6">
                {TIME_GRID.filter((time) =>
                  !isTimeOccupied(time, duration, occupied, booking.startTime) &&
                  (selectedDate !== todayStr || timeToMinutes(time) > nowMinutes)
                ).map((time) => {
                  const isSelected = selectedTime === time
                  return (
                    <button
                      key={time}
                      onClick={() => setSelectedTime(isSelected ? '' : time)}
                      className={`py-2 rounded-xl text-[13.5px] font-medium border transition-all ${
                        isSelected
                          ? 'bg-ink text-cream border-ink'
                          : 'border-line bg-white hover:border-line-strong'
                      }`}
                    >
                      {time}
                    </button>
                  )
                })}
              </div>
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
