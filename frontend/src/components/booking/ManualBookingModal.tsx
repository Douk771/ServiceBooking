import { useState, useEffect } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { format, addDays, isTomorrow } from 'date-fns'
import { ru } from 'date-fns/locale'
import { bookingsApi } from '../../api/bookings'
import { companiesApi } from '../../api/companies'
import { servicesApi } from '../../api/services'
import { Button } from '../ui/Button'
import { Input } from '../ui/Input'
import { useOverlayDismiss } from '../../hooks/useOverlayDismiss'
import { getBookingErrorMessage } from '../../utils/bookingError'
import type { Company, Service } from '../../types'

interface Props {
  onClose: () => void
}

type Step = 'company' | 'service' | 'master' | 'datetime' | 'client' | 'done'

function timeToMinutes(t: string): number {
  const [h, m] = t.slice(0, 5).split(':').map(Number)
  return h * 60 + m
}

export function ManualBookingModal({ onClose }: Props) {
  const qc = useQueryClient()
  const [step, setStep] = useState<Step>('company')
  const [selectedCompany, setSelectedCompany] = useState<Company | null>(null)
  const [selectedService, setSelectedService] = useState<Service | null>(null)
  const [selectedMasterId, setSelectedMasterId] = useState('')
  const [selectedDate, setSelectedDate] = useState('')
  const [selectedTime, setSelectedTime] = useState('')
  const [clientName, setClientName] = useState('')
  const [clientPhone, setClientPhone] = useState('')
  const [clientEmail, setClientEmail] = useState('')
  const [notes, setNotes] = useState('')

  const { data: companies, isLoading: companiesLoading } = useQuery({
    queryKey: ['my-companies-all'],
    queryFn: async () => {
      const [owned, member] = await Promise.all([
        companiesApi.getMy(),
        companiesApi.getMemberOf(),
      ])
      const all = [...owned, ...member]
      return Array.from(new Map(all.map((c) => [c.id, c])).values())
    },
  })

  // Auto-select company if only one
  useEffect(() => {
    if (companies?.length === 1 && !selectedCompany) {
      setSelectedCompany(companies[0])
      setStep('service')
    }
  }, [companies, selectedCompany])

  const { data: services, isLoading: servicesLoading } = useQuery({
    queryKey: ['services', selectedCompany?.id],
    queryFn: () => servicesApi.getByCompany(selectedCompany!.id),
    enabled: !!selectedCompany,
  })

  const { data: masters, isLoading: mastersLoading } = useQuery({
    queryKey: ['company-masters', selectedCompany?.id, selectedService?.id],
    queryFn: () => companiesApi.getMasters(selectedCompany!.id, selectedService!.id),
    enabled: !!selectedCompany && !!selectedService,
  })

  const now = new Date()
  const todayStr = format(now, 'yyyy-MM-dd')
  const nowMinutes = now.getHours() * 60 + now.getMinutes()

  // Today is only offered as a booking date if the master still has at least one slot today
  // that both respects working hours/breaks and hasn't passed yet.
  const { data: slotsToday = [] } = useQuery({
    queryKey: ['slots', selectedMasterId, selectedService?.id, todayStr],
    queryFn: () => bookingsApi.getSlots(selectedMasterId, selectedService!.id, todayStr),
    enabled: !!selectedMasterId && !!selectedService,
    staleTime: 0,
  })
  const hasAvailableSlotToday = slotsToday.some((s) => timeToMinutes(s.start) > nowMinutes)

  const { data: rawSlots, isLoading: slotsLoading } = useQuery({
    queryKey: ['slots', selectedMasterId, selectedService?.id, selectedDate],
    queryFn: () => bookingsApi.getSlots(selectedMasterId, selectedService!.id, selectedDate),
    enabled: !!selectedMasterId && !!selectedService && !!selectedDate,
    staleTime: 0,
  })
  const slots = (rawSlots ?? []).filter((s) => selectedDate !== todayStr || timeToMinutes(s.start) > nowMinutes)

  const mutation = useMutation({
    mutationFn: () =>
      bookingsApi.create({
        companyId: selectedCompany!.id,
        serviceId: selectedService!.id,
        masterId: selectedMasterId,
        date: selectedDate,
        startTime: selectedTime,
        notes,
        guestName: clientName,
        guestPhone: clientPhone,
        guestEmail: clientEmail || undefined,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['master-bookings'] })
      setStep('done')
    },
  })

  const selectedMaster = masters?.find((m) => m.userId === selectedMasterId)
  const days = [
    ...(hasAvailableSlotToday ? [{ value: todayStr, label: 'Сегодня' }] : []),
    ...Array.from({ length: 14 }, (_, i) => {
      const d = addDays(now, i + 1)
      return { value: format(d, 'yyyy-MM-dd'), label: isTomorrow(d) ? 'Завтра' : format(d, 'd MMM, EEE', { locale: ru }) }
    }),
  ]

  const progressSteps: Step[] = companies?.length === 1
    ? ['service', 'master', 'datetime', 'client']
    : ['company', 'service', 'master', 'datetime', 'client']
  const currentIdx = progressSteps.indexOf(step)

  const formattedDate = days.find((d) => d.value === selectedDate)?.label ?? ''

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
              <h2 className="font-bold text-gray-900 text-lg">Записать клиента</h2>
              {selectedService && (
                <p className="text-sm text-gray-500 mt-0.5">
                  {selectedService.name} · {selectedService.durationMinutes} мин · {selectedService.price.toLocaleString('ru-RU')} ₽
                </p>
              )}
            </div>
            <button onClick={onClose} className="text-gray-400 hover:text-gray-600 text-xl">✕</button>
          </div>

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

          {/* ── Company ── */}
          {step === 'company' && (
            <div>
              <h3 className="font-semibold text-gray-700 mb-4">Выберите компанию</h3>
              {companiesLoading ? (
                <div className="flex flex-col gap-3">
                  {[1, 2].map((i) => <div key={i} className="h-14 bg-gray-100 rounded-2xl animate-pulse" />)}
                </div>
              ) : companies && companies.length > 0 ? (
                <div className="flex flex-col gap-3">
                  {companies.map((c) => (
                    <button
                      key={c.id}
                      onClick={() => { setSelectedCompany(c); setStep('service') }}
                      className="flex items-center gap-3 p-4 rounded-2xl border border-gray-200 hover:border-primary-400 hover:bg-primary-50 transition-all text-left"
                    >
                      <div className="w-10 h-10 rounded-xl bg-primary-100 flex items-center justify-center text-primary-700 font-bold text-sm shrink-0">
                        {c.name[0]}
                      </div>
                      <div>
                        <p className="font-semibold text-gray-900">{c.name}</p>
                        {c.address && <p className="text-xs text-gray-400">{c.address}</p>}
                      </div>
                      <span className="ml-auto text-gray-300 text-lg">›</span>
                    </button>
                  ))}
                </div>
              ) : (
                <p className="text-center text-gray-400 py-8">Вы не состоите ни в одной компании</p>
              )}
            </div>
          )}

          {/* ── Service ── */}
          {step === 'service' && (
            <div>
              {companies && companies.length > 1 && (
                <button onClick={() => setStep('company')} className="text-sm text-gray-500 hover:text-gray-700 mb-4 flex items-center gap-1">
                  ← {selectedCompany?.name}
                </button>
              )}
              <h3 className="font-semibold text-gray-700 mb-4">Выберите услугу</h3>
              {servicesLoading ? (
                <div className="flex flex-col gap-3">
                  {[1, 2, 3].map((i) => <div key={i} className="h-14 bg-gray-100 rounded-2xl animate-pulse" />)}
                </div>
              ) : services && services.length > 0 ? (
                <div className="flex flex-col gap-3">
                  {services.map((s) => (
                    <button
                      key={s.id}
                      onClick={() => { setSelectedService(s); setStep('master') }}
                      className="flex items-center justify-between p-4 rounded-2xl border border-gray-200 hover:border-primary-400 hover:bg-primary-50 transition-all text-left"
                    >
                      <div>
                        <p className="font-semibold text-gray-900">{s.name}</p>
                        <p className="text-xs text-gray-400 mt-0.5">{s.durationMinutes} мин · {s.price.toLocaleString('ru-RU')} ₽</p>
                      </div>
                      <span className="text-gray-300 text-lg">›</span>
                    </button>
                  ))}
                </div>
              ) : (
                <p className="text-center text-gray-400 py-8">Нет доступных услуг</p>
              )}
            </div>
          )}

          {/* ── Master ── */}
          {step === 'master' && (
            <div>
              <button onClick={() => setStep('service')} className="text-sm text-gray-500 hover:text-gray-700 mb-4 flex items-center gap-1">
                ← {selectedService?.name}
              </button>
              <h3 className="font-semibold text-gray-700 mb-4">Выберите мастера</h3>
              {mastersLoading ? (
                <div className="flex flex-col gap-3">
                  {[1, 2].map((i) => <div key={i} className="h-16 bg-gray-100 rounded-2xl animate-pulse" />)}
                </div>
              ) : masters && masters.length > 0 ? (
                <div className="flex flex-col gap-3">
                  {masters.map((m) => (
                    <button
                      key={m.userId}
                      onClick={() => { setSelectedMasterId(m.userId); setStep('datetime') }}
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
                <p className="text-center text-gray-400 py-8">Нет доступных мастеров для этой услуги</p>
              )}
            </div>
          )}

          {/* ── DateTime ── */}
          {step === 'datetime' && (
            <div>
              <button onClick={() => setStep('master')} className="text-sm text-gray-500 hover:text-gray-700 mb-4 flex items-center gap-1">
                ← {selectedMaster ? `${selectedMaster.firstName} ${selectedMaster.lastName}` : 'Мастер'}
              </button>

              {/* Date grid */}
              <h3 className="font-semibold text-gray-700 mb-3">Дата</h3>
              <div className="grid grid-cols-2 gap-2 mb-5">
                {days.map((d) => (
                  <button
                    key={d.value}
                    onClick={() => { setSelectedDate(d.value); setSelectedTime('') }}
                    className={`px-4 py-3 rounded-xl border text-sm transition-all text-left ${
                      selectedDate === d.value
                        ? 'bg-primary-500 text-white border-primary-500'
                        : 'border-gray-200 hover:border-primary-400 hover:bg-primary-50'
                    }`}
                  >
                    {d.label}
                  </button>
                ))}
              </div>

              {/* Time grid — shown only after date selected */}
              {selectedDate && (
                <>
                  <h3 className="font-semibold text-gray-700 mb-3">Время</h3>
                  {slotsLoading ? (
                    <div className="grid grid-cols-4 gap-2">
                      {Array.from({ length: 8 }).map((_, i) => (
                        <div key={i} className="h-9 bg-gray-100 rounded-xl animate-pulse" />
                      ))}
                    </div>
                  ) : slots.length > 0 ? (
                    <div className="grid grid-cols-4 gap-2">
                      {slots.map((s) => {
                        const time = s.start.slice(0, 5)
                        const isSelected = selectedTime === time
                        return (
                          <button
                            key={time}
                            onClick={() => setSelectedTime(isSelected ? '' : time)}
                            className={`py-2 rounded-xl text-sm font-medium border transition-all ${
                              isSelected
                                ? 'bg-primary-500 text-white border-primary-500'
                                : 'border-gray-200 hover:border-primary-400 hover:bg-primary-50'
                            }`}
                          >
                            {time}
                          </button>
                        )
                      })}
                    </div>
                  ) : (
                    <p className="text-center text-gray-400 py-4 text-sm">Нет доступных слотов на этот день</p>
                  )}
                </>
              )}

              <Button
                className="mt-5 w-full"
                disabled={!selectedDate || !selectedTime}
                onClick={() => setStep('client')}
              >
                Продолжить
              </Button>
            </div>
          )}

          {/* ── Client info ── */}
          {step === 'client' && (
            <div className="flex flex-col gap-4">
              <button onClick={() => setStep('datetime')} className="text-sm text-gray-500 hover:text-gray-700 flex items-center gap-1">
                ← Изменить дату / время
              </button>

              <div className="bg-orange-50 rounded-2xl p-4 text-sm text-gray-700">
                <div className="font-semibold">{selectedService?.name}</div>
                {selectedMaster && (
                  <div className="text-gray-500 mt-0.5">
                    {selectedMaster.firstName} {selectedMaster.lastName}
                  </div>
                )}
                <div className="text-gray-500 mt-1">
                  {formattedDate} · {selectedTime}
                </div>
              </div>

              <h3 className="font-semibold text-gray-700">Данные клиента</h3>

              <Input
                label="Имя клиента *"
                placeholder="Иван Иванов"
                value={clientName}
                onChange={(e) => setClientName(e.target.value)}
              />
              <Input
                label="Телефон *"
                type="tel"
                placeholder="+7 999 000 00 00"
                value={clientPhone}
                onChange={(e) => setClientPhone(e.target.value)}
              />
              <Input
                label="Email (необязательно)"
                type="email"
                placeholder="client@email.com"
                value={clientEmail}
                onChange={(e) => setClientEmail(e.target.value)}
              />

              <div className="flex flex-col gap-1">
                <label className="text-sm font-medium text-gray-700">Комментарий</label>
                <textarea
                  className="rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400 focus:ring-2 focus:ring-primary-100 resize-none"
                  rows={3}
                  placeholder="Пожелания или заметки..."
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                />
              </div>

              <Button
                size="lg"
                loading={mutation.isPending}
                onClick={() => mutation.mutate()}
                disabled={!clientName || !clientPhone}
              >
                Записать клиента
              </Button>

              {mutation.isError && (
                <p className="text-sm text-red-500 text-center">{getBookingErrorMessage(mutation.error)}</p>
              )}
            </div>
          )}

          {/* ── Done ── */}
          {step === 'done' && (
            <div className="text-center py-6">
              <div className="text-5xl mb-4">✅</div>
              <h3 className="text-xl font-bold text-gray-900">Клиент записан!</h3>
              <p className="text-gray-500 mt-2">
                {clientName} · {formattedDate} в {selectedTime}
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
