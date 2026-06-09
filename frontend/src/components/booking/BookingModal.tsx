import { useState } from 'react'
import { useQuery, useMutation } from '@tanstack/react-query'
import { format, addDays } from 'date-fns'
import { ru } from 'date-fns/locale'
import { bookingsApi } from '../../api/bookings'
import { useAuthStore } from '../../store/authStore'
import { Button } from '../ui/Button'
import { Input } from '../ui/Input'
import type { Company, Service } from '../../types'

interface Props {
  service: Service
  company: Company
  onClose: () => void
}

const DEMO_MASTER_ID = 'demo-master'

export function BookingModal({ service, company, onClose }: Props) {
  const { user, isAuthenticated } = useAuthStore()
  const [step, setStep] = useState<'date' | 'slot' | 'info' | 'done'>('date')
  const [selectedDate, setSelectedDate] = useState('')
  const [selectedSlot, setSelectedSlot] = useState('')
  const [guestName, setGuestName] = useState('')
  const [guestPhone, setGuestPhone] = useState('')
  const [guestEmail, setGuestEmail] = useState('')
  const [notes, setNotes] = useState('')

  // Next 14 days
  const days = Array.from({ length: 14 }, (_, i) => {
    const d = addDays(new Date(), i + 1)
    return { value: format(d, 'yyyy-MM-dd'), label: format(d, 'd MMM, EEE', { locale: ru }) }
  })

  const { data: slots, isLoading: slotsLoading } = useQuery({
    queryKey: ['slots', DEMO_MASTER_ID, service.id, selectedDate],
    queryFn: () => bookingsApi.getSlots(DEMO_MASTER_ID, service.id, selectedDate),
    enabled: !!selectedDate,
  })

  const mutation = useMutation({
    mutationFn: () =>
      bookingsApi.create({
        companyId: company.id,
        serviceId: service.id,
        masterId: DEMO_MASTER_ID,
        date: selectedDate,
        startTime: selectedSlot,
        notes,
        guestName: isAuthenticated() ? undefined : guestName,
        guestPhone: isAuthenticated() ? undefined : guestPhone,
        guestEmail: isAuthenticated() ? undefined : guestEmail,
      }),
    onSuccess: () => setStep('done'),
  })

  return (
    <div className="fixed inset-0 bg-black/40 backdrop-blur-sm z-50 flex items-center justify-center p-4" onClick={onClose}>
      <div
        className="bg-white rounded-3xl shadow-2xl w-full max-w-md max-h-[90vh] overflow-y-auto"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="p-6 border-b border-gray-100">
          <div className="flex items-center justify-between">
            <div>
              <h2 className="font-bold text-gray-900 text-lg">Запись на услугу</h2>
              <p className="text-sm text-gray-500 mt-0.5">{service.name} · {service.durationMinutes} мин · {service.price.toLocaleString('ru-RU')} ₽</p>
            </div>
            <button onClick={onClose} className="text-gray-400 hover:text-gray-600 text-xl">✕</button>
          </div>

          {/* Steps */}
          <div className="flex gap-1 mt-4">
            {['date', 'slot', 'info'].map((s, i) => (
              <div key={s} className={`h-1 flex-1 rounded-full transition-colors ${
                ['date', 'slot', 'info'].indexOf(step) >= i ? 'bg-primary-500' : 'bg-gray-100'
              }`} />
            ))}
          </div>
        </div>

        <div className="p-6">
          {/* Step: Date */}
          {step === 'date' && (
            <div>
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

          {/* Step: Slot */}
          {step === 'slot' && (
            <div>
              <button onClick={() => setStep('date')} className="text-sm text-gray-500 hover:text-gray-700 mb-4 flex items-center gap-1">
                ← Изменить дату
              </button>
              <h3 className="font-semibold text-gray-700 mb-4">Выберите время</h3>
              {slotsLoading ? (
                <div className="grid grid-cols-3 gap-2">
                  {Array.from({ length: 9 }).map((_, i) => <div key={i} className="h-10 bg-gray-100 rounded-xl animate-pulse" />)}
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

          {/* Step: Info */}
          {step === 'info' && (
            <div className="flex flex-col gap-4">
              <button onClick={() => setStep('slot')} className="text-sm text-gray-500 hover:text-gray-700 flex items-center gap-1">
                ← Изменить время
              </button>

              <div className="bg-orange-50 rounded-2xl p-4 text-sm text-gray-700">
                <div className="font-medium">{service.name}</div>
                <div className="text-gray-500 mt-1">
                  {days.find(d => d.value === selectedDate)?.label} · {selectedSlot.slice(0, 5)}
                </div>
              </div>

              {!isAuthenticated() && (
                <>
                  <Input label="Ваше имя *" placeholder="Иван Иванов" value={guestName} onChange={e => setGuestName(e.target.value)} />
                  <Input label="Телефон *" type="tel" placeholder="+7 999 000 00 00" value={guestPhone} onChange={e => setGuestPhone(e.target.value)} />
                  <Input label="Email" type="email" placeholder="your@email.com" value={guestEmail} onChange={e => setGuestEmail(e.target.value)} />
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

              {!isAuthenticated() && (
                <p className="text-xs text-gray-400">
                  Продолжая, вы подтверждаете, что не являетесь роботом (защита reCAPTCHA)
                </p>
              )}

              <Button
                size="lg"
                loading={mutation.isPending}
                onClick={() => mutation.mutate()}
                disabled={!isAuthenticated() && (!guestName || !guestPhone)}
              >
                Подтвердить запись
              </Button>

              {mutation.isError && (
                <p className="text-sm text-red-500 text-center">Произошла ошибка. Попробуйте снова.</p>
              )}
            </div>
          )}

          {/* Done */}
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
