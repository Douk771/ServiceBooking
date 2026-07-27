import { useState } from 'react'
import { useQuery, useMutation } from '@tanstack/react-query'
import { format, addDays, isTomorrow } from 'date-fns'
import { ru } from 'date-fns/locale'
import { bookingsApi } from '../../api/bookings'
import { companiesApi } from '../../api/companies'
import { useAuthStore } from '../../store/authStore'
import { Button } from '../ui/Button'
import { Input } from '../ui/Input'
import { useOverlayDismiss } from '../../hooks/useOverlayDismiss'
import { getBookingErrorMessage } from '../../utils/bookingError'
import { SmartCaptcha, smartCaptchaEnabled } from './SmartCaptcha'
import type { Company, Service } from '../../types'

interface Props {
  service: Service
  company: Company
  onClose: () => void
}

type Step = 'master' | 'date' | 'slot' | 'info' | 'done'

export function BookingModal({ service, company, onClose }: Props) {
  const { isAuthenticated } = useAuthStore()
  const [step, setStep]               = useState<Step>('master')
  const [selectedMasterId, setSelectedMasterId] = useState('')
  const [selectedDate, setSelectedDate]         = useState('')
  const [selectedSlot, setSelectedSlot]         = useState('')
  const [guestName, setGuestName]     = useState('')
  const [guestPhone, setGuestPhone]   = useState('')
  const [guestEmail, setGuestEmail]   = useState('')
  const [notes, setNotes]             = useState('')
  const [captchaToken, setCaptchaToken] = useState('')

  // Load masters that can perform this service
  const { data: masters, isLoading: mastersLoading } = useQuery({
    queryKey: ['company-masters', company.id, service.id],
    queryFn: () => companiesApi.getMasters(company.id, service.id),
  })

  const now = new Date()
  const todayStr = format(now, 'yyyy-MM-dd')
  const nowMinutes = now.getHours() * 60 + now.getMinutes()
  const timeToMinutes = (t: string) => {
    const [h, m] = t.slice(0, 5).split(':').map(Number)
    return h * 60 + m
  }

  // Today is only offered as a booking date if the master still has at least one slot today
  // that both isn't already taken and hasn't passed yet — otherwise there's nothing left to pick.
  const { data: slotsToday = [] } = useQuery({
    queryKey: ['slots', selectedMasterId, service.id, todayStr],
    queryFn: () => bookingsApi.getSlots(selectedMasterId, service.id, todayStr),
    enabled: !!selectedMasterId,
    staleTime: 0,
  })
  const hasAvailableSlotToday = slotsToday.some((s) => timeToMinutes(s.start) > nowMinutes)

  const days = [
    ...(hasAvailableSlotToday ? [{ value: todayStr, label: 'Сегодня' }] : []),
    ...Array.from({ length: 14 }, (_, i) => {
      const d = addDays(now, i + 1)
      return { value: format(d, 'yyyy-MM-dd'), label: isTomorrow(d) ? 'Завтра' : format(d, 'd MMM, EEE', { locale: ru }) }
    }),
  ]

  const { data: rawSlots, isLoading: slotsLoading } = useQuery({
    queryKey: ['slots', selectedMasterId, service.id, selectedDate],
    queryFn: () => bookingsApi.getSlots(selectedMasterId, service.id, selectedDate),
    enabled: !!selectedMasterId && !!selectedDate,
    staleTime: 0, // always fetch fresh — bookings made by others should be reflected immediately
  })
  const slots = rawSlots?.filter((s) => selectedDate !== todayStr || timeToMinutes(s.start) > nowMinutes)

  const mutation = useMutation({
    mutationFn: () =>
      bookingsApi.create({
        companyId: company.id,
        serviceId: service.id,
        masterId: selectedMasterId,
        date: selectedDate,
        startTime: selectedSlot,
        notes,
        guestName:  isAuthenticated() ? undefined : guestName,
        guestPhone: isAuthenticated() ? undefined : guestPhone,
        guestEmail: isAuthenticated() ? undefined : guestEmail,
        captchaToken: isAuthenticated() ? undefined : (captchaToken || undefined),
      }),
    onSuccess: () => setStep('done'),
  })

  // Auto-advance past master step if only one master
  const pickMaster = (id: string) => {
    setSelectedMasterId(id)
    setStep('date')
  }

  const selectedMaster = masters?.find(m => m.userId === selectedMasterId)

  // Progress bar steps (exclude 'done', map 'master' only if >1 master)
  const progressSteps: Step[] = ['master', 'date', 'slot', 'info']
  const currentIdx = progressSteps.indexOf(step)

  const dismiss = useOverlayDismiss(onClose)

  return (
    <div
      className="fixed inset-0 bg-black/40 backdrop-blur-sm z-50 flex items-center justify-center p-4"
      {...dismiss}
    >
      <div className="bg-white rounded-3xl shadow-2xl w-full max-w-md max-h-[90vh] overflow-y-auto">
        {/* Header */}
        <div className="p-6 border-b border-gray-100">
          <div className="flex items-center justify-between">
            <div>
              <h2 className="font-bold text-gray-900 text-lg">Запись на услугу</h2>
              <p className="text-sm text-gray-500 mt-0.5">
                {service.name} · {service.durationMinutes} мин · {service.price.toLocaleString('ru-RU')} ₽
              </p>
            </div>
            <button onClick={onClose} className="text-gray-400 hover:text-gray-600 text-xl">✕</button>
          </div>

          {/* Progress bar */}
          {step !== 'done' && (
            <div className="flex gap-1 mt-4">
              {progressSteps.map((s, i) => (
                <div
                  key={s}
                  className={`h-1 flex-1 rounded-full transition-colors ${
                    currentIdx >= i ? 'bg-primary-500' : 'bg-gray-100'
                  }`}
                />
              ))}
            </div>
          )}
        </div>

        <div className="p-6">

          {/* ── Step: Master ── */}
          {step === 'master' && (
            <div>
              <h3 className="font-semibold text-gray-700 mb-4">Выберите мастера</h3>
              {mastersLoading ? (
                <div className="flex flex-col gap-3">
                  {Array.from({ length: 2 }).map((_, i) => (
                    <div key={i} className="h-16 bg-gray-100 rounded-2xl animate-pulse" />
                  ))}
                </div>
              ) : masters && masters.length > 0 ? (
                <div className="flex flex-col gap-3">
                  {masters.map((m) => (
                    <button
                      key={m.userId}
                      onClick={() => pickMaster(m.userId)}
                      className="flex items-center gap-4 p-4 rounded-2xl border border-gray-200 hover:border-primary-400 hover:bg-primary-50 transition-all text-left"
                    >
                      <div className="w-11 h-11 rounded-full bg-primary-100 flex items-center justify-center text-primary-700 font-bold text-sm shrink-0">
                        {m.firstName[0]}{m.lastName[0]}
                      </div>
                      <div>
                        <p className="font-semibold text-gray-900">{m.firstName} {m.lastName}</p>
                        {m.bio && <p className="text-xs text-gray-400 mt-0.5">{m.bio}</p>}
                      </div>
                      <span className="ml-auto text-gray-300 text-lg">›</span>
                    </button>
                  ))}
                </div>
              ) : (
                <p className="text-center text-gray-400 py-8">
                  Нет доступных мастеров для этой услуги
                </p>
              )}
            </div>
          )}

          {/* ── Step: Date ── */}
          {step === 'date' && (
            <div>
              <button onClick={() => setStep('master')} className="text-sm text-gray-500 hover:text-gray-700 mb-4 flex items-center gap-1">
                ← {selectedMaster ? `${selectedMaster.firstName} ${selectedMaster.lastName}` : 'Мастер'}
              </button>
              <h3 className="font-semibold text-gray-700 mb-4">Выберите дату</h3>
              <div className="grid grid-cols-2 gap-2">
                {days.map((d) => (
                  <button
                    key={d.value}
                    onClick={() => { setSelectedDate(d.value); setStep('slot') }}
                    className="px-4 py-3 rounded-xl border border-gray-200 text-sm hover:border-primary-400 hover:bg-primary-50 transition-all text-left"
                  >
                    {d.label}
                  </button>
                ))}
              </div>
            </div>
          )}

          {/* ── Step: Slot ── */}
          {step === 'slot' && (
            <div>
              <button onClick={() => setStep('date')} className="text-sm text-gray-500 hover:text-gray-700 mb-4 flex items-center gap-1">
                ← Изменить дату
              </button>
              <h3 className="font-semibold text-gray-700 mb-4">Выберите время</h3>
              {slotsLoading ? (
                <div className="grid grid-cols-3 gap-2">
                  {Array.from({ length: 9 }).map((_, i) => (
                    <div key={i} className="h-10 bg-gray-100 rounded-xl animate-pulse" />
                  ))}
                </div>
              ) : slots && slots.length > 0 ? (
                <div className="grid grid-cols-3 gap-2">
                  {slots.map((s) => (
                    <button
                      key={s.start}
                      onClick={() => { setSelectedSlot(s.start); setStep('info') }}
                      className={`py-2.5 rounded-xl text-sm font-medium border transition-all ${
                        selectedSlot === s.start
                          ? 'bg-primary-500 text-white border-primary-500'
                          : 'border-gray-200 hover:border-primary-400 hover:bg-primary-50'
                      }`}
                    >
                      {s.start.slice(0, 5)}
                    </button>
                  ))}
                </div>
              ) : (
                <p className="text-center text-gray-400 py-8">Нет доступных слотов на этот день</p>
              )}
            </div>
          )}

          {/* ── Step: Info ── */}
          {step === 'info' && (
            <div className="flex flex-col gap-4">
              <button onClick={() => setStep('slot')} className="text-sm text-gray-500 hover:text-gray-700 flex items-center gap-1">
                ← Изменить время
              </button>

              <div className="bg-orange-50 rounded-2xl p-4 text-sm text-gray-700">
                <div className="font-semibold">{service.name}</div>
                {selectedMaster && (
                  <div className="text-gray-500 mt-0.5">
                    {selectedMaster.firstName} {selectedMaster.lastName}
                  </div>
                )}
                <div className="text-gray-500 mt-1">
                  {days.find(d => d.value === selectedDate)?.label} · {selectedSlot.slice(0, 5)}
                </div>
              </div>

              {!isAuthenticated() && (
                <>
                  <Input
                    label="Ваше имя *"
                    placeholder="Иван Иванов"
                    value={guestName}
                    onChange={e => setGuestName(e.target.value)}
                  />
                  <Input
                    label="Телефон *"
                    type="tel"
                    placeholder="+7 999 000 00 00"
                    value={guestPhone}
                    onChange={e => setGuestPhone(e.target.value)}
                  />
                  <Input
                    label="Email"
                    type="email"
                    placeholder="your@email.com"
                    value={guestEmail}
                    onChange={e => setGuestEmail(e.target.value)}
                  />
                </>
              )}

              <div className="flex flex-col gap-1">
                <label className="text-sm font-medium text-gray-700">Комментарий (необязательно)</label>
                <textarea
                  className="rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400 focus:ring-2 focus:ring-primary-100 resize-none"
                  rows={3}
                  placeholder="Пожелания или вопросы..."
                  value={notes}
                  onChange={e => setNotes(e.target.value)}
                />
              </div>

              {!isAuthenticated() && smartCaptchaEnabled && (
                <div className="flex flex-col gap-1">
                  <SmartCaptcha onToken={setCaptchaToken} />
                  <p className="text-xs text-gray-400">Подтвердите, что вы не робот (Yandex SmartCaptcha)</p>
                </div>
              )}

              <Button
                size="lg"
                loading={mutation.isPending}
                onClick={() => mutation.mutate()}
                disabled={
                  (!isAuthenticated() && (!guestName || !guestPhone)) ||
                  (!isAuthenticated() && smartCaptchaEnabled && !captchaToken)
                }
              >
                Подтвердить запись
              </Button>

              {mutation.isError && (
                <p className="text-sm text-red-500 text-center">{getBookingErrorMessage(mutation.error)}</p>
              )}
            </div>
          )}

          {/* ── Done ── */}
          {step === 'done' && (
            <div className="text-center py-6">
              <div className="text-5xl mb-4">🎉</div>
              <h3 className="text-xl font-bold text-gray-900">Запись подтверждена!</h3>
              <p className="text-gray-500 mt-2">
                Ждём вас {days.find(d => d.value === selectedDate)?.label} в {selectedSlot.slice(0, 5)}
              </p>
              <Button onClick={onClose} variant="secondary" size="lg" className="mt-6">
                Закрыть
              </Button>
            </div>
          )}

        </div>
      </div>
    </div>
  )
}
