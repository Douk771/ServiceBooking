import { useState, useEffect } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { format, addDays, isTomorrow } from 'date-fns'
import { ru } from 'date-fns/locale'
import { bookingsApi } from '../../api/bookings'
import { companiesApi } from '../../api/companies'
import { servicesApi } from '../../api/services'
import { Button } from '../ui/Button'
import { Input } from '../ui/Input'
import { PhoneInput } from '../ui/PhoneInput'
import { Icon } from '../ui/Icon'
import { isRussianPhone } from '../../utils/phone'
import { Avatar } from '../ui/Avatar'
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

function BackLink({ onClick, children }: { onClick: () => void; children: React.ReactNode }) {
  return (
    <button onClick={onClick} className="flex items-center gap-1.5 text-[13px] text-ink-soft hover:text-ink mb-4">
      <Icon name="chevron-left" size={13} strokeWidth={1.8} />
      {children}
    </button>
  )
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
      const [owned, member] = await Promise.all([companiesApi.getMy(), companiesApi.getMemberOf()])
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
    queryKey: ['slots', selectedCompany?.id, selectedMasterId, selectedService?.id, todayStr, 'manual'],
    queryFn: () => bookingsApi.getSlots(selectedCompany!.id, selectedMasterId, selectedService!.id, todayStr, true),
    enabled: !!selectedCompany && !!selectedMasterId && !!selectedService,
    staleTime: 0,
  })
  const hasAvailableSlotToday = slotsToday.some((s) => timeToMinutes(s.start) > nowMinutes)

  const { data: rawSlots, isLoading: slotsLoading } = useQuery({
    queryKey: ['slots', selectedCompany?.id, selectedMasterId, selectedService?.id, selectedDate, 'manual'],
    queryFn: () => bookingsApi.getSlots(selectedCompany!.id, selectedMasterId, selectedService!.id, selectedDate, true),
    enabled: !!selectedCompany && !!selectedMasterId && !!selectedService && !!selectedDate,
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
      return {
        value: format(d, 'yyyy-MM-dd'),
        label: isTomorrow(d) ? 'Завтра' : format(d, 'd MMM, EEE', { locale: ru }),
      }
    }),
  ]

  const progressSteps: Step[] =
    companies?.length === 1
      ? ['service', 'master', 'datetime', 'client']
      : ['company', 'service', 'master', 'datetime', 'client']
  const currentIdx = progressSteps.indexOf(step)

  const formattedDate = days.find((d) => d.value === selectedDate)?.label ?? ''

  const dismiss = useOverlayDismiss(onClose)

  return (
    <div className="fixed inset-0 bg-ink/45 backdrop-blur-sm z-50 flex items-center justify-center p-5" {...dismiss}>
      <div className="bg-cream rounded-[26px] shadow-modal w-full max-w-[440px] max-h-[88vh] overflow-y-auto">
        {/* Header */}
        <div className="p-6 pb-[22px] border-b border-line">
          <div className="flex items-center justify-between">
            <div>
              <h2 className="font-serif text-[19px] font-medium text-ink mb-0.5">Записать клиента</h2>
              {selectedService && (
                <p className="text-[13px] text-ink-soft">
                  {selectedService.name} · {selectedService.durationMinutes} мин ·{' '}
                  {selectedService.price.toLocaleString('ru-RU')} ₽
                </p>
              )}
            </div>
            <button onClick={onClose} className="text-muted hover:text-ink shrink-0">
              <Icon name="x" size={18} strokeWidth={1.8} />
            </button>
          </div>

          {step !== 'done' && (
            <div className="flex gap-1.5 mt-4">
              {progressSteps.map((s, i) => (
                <div
                  key={s}
                  className={`h-1 flex-1 rounded-full transition-colors ${currentIdx >= i ? 'bg-ink' : 'bg-line'}`}
                />
              ))}
            </div>
          )}
        </div>

        <div className="p-6 pt-[22px]">
          {/* ── Company ── */}
          {step === 'company' && (
            <div>
              <h3 className="text-[14.5px] font-semibold text-[#4A4038] mb-4">Выберите компанию</h3>
              {companiesLoading ? (
                <div className="flex flex-col gap-2.5">
                  {[1, 2].map((i) => (
                    <div key={i} className="h-14 bg-cream-deep rounded-2xl animate-pulse" />
                  ))}
                </div>
              ) : companies && companies.length > 0 ? (
                <div className="flex flex-col gap-2.5">
                  {companies.map((c) => (
                    <button
                      key={c.id}
                      onClick={() => {
                        setSelectedCompany(c)
                        setStep('service')
                      }}
                      className="flex items-center gap-3 p-3.5 rounded-2xl border border-line bg-white hover:border-line-strong transition-all text-left"
                    >
                      <div className="w-10 h-10 rounded-xl bg-cream-deep flex items-center justify-center text-gold-dark font-bold text-sm shrink-0">
                        {c.name[0]}
                      </div>
                      <div>
                        <p className="font-semibold text-sm text-ink">{c.name}</p>
                        {c.address && <p className="text-xs text-muted">{c.address}</p>}
                      </div>
                      <Icon
                        name="chevron-right"
                        size={16}
                        strokeWidth={1.8}
                        className="ml-auto text-line-strong shrink-0"
                      />
                    </button>
                  ))}
                </div>
              ) : (
                <p className="text-center text-muted py-8">Вы не состоите ни в одной компании</p>
              )}
            </div>
          )}

          {/* ── Service ── */}
          {step === 'service' && (
            <div>
              {companies && companies.length > 1 && (
                <BackLink onClick={() => setStep('company')}>{selectedCompany?.name}</BackLink>
              )}
              <h3 className="text-[14.5px] font-semibold text-[#4A4038] mb-4">Выберите услугу</h3>
              {servicesLoading ? (
                <div className="flex flex-col gap-2.5">
                  {[1, 2, 3].map((i) => (
                    <div key={i} className="h-14 bg-cream-deep rounded-2xl animate-pulse" />
                  ))}
                </div>
              ) : services && services.length > 0 ? (
                <div className="flex flex-col gap-2.5">
                  {services.map((s) => (
                    <button
                      key={s.id}
                      onClick={() => {
                        setSelectedService(s)
                        setStep('master')
                      }}
                      className="flex items-center justify-between p-3.5 rounded-2xl border border-line bg-white hover:border-line-strong transition-all text-left"
                    >
                      <div>
                        <p className="font-semibold text-sm text-ink">{s.name}</p>
                        <p className="text-xs text-muted mt-0.5">
                          {s.durationMinutes} мин · {s.price.toLocaleString('ru-RU')} ₽
                        </p>
                      </div>
                      <Icon name="chevron-right" size={16} strokeWidth={1.8} className="text-line-strong shrink-0" />
                    </button>
                  ))}
                </div>
              ) : (
                <p className="text-center text-muted py-8">Нет доступных услуг</p>
              )}
            </div>
          )}

          {/* ── Master ── */}
          {step === 'master' && (
            <div>
              <BackLink onClick={() => setStep('service')}>{selectedService?.name}</BackLink>
              <h3 className="text-[14.5px] font-semibold text-[#4A4038] mb-4">Выберите мастера</h3>
              {mastersLoading ? (
                <div className="flex flex-col gap-2.5">
                  {[1, 2].map((i) => (
                    <div key={i} className="h-16 bg-cream-deep rounded-2xl animate-pulse" />
                  ))}
                </div>
              ) : masters && masters.length > 0 ? (
                <div className="flex flex-col gap-2.5">
                  {masters.map((m) => (
                    <button
                      key={m.userId}
                      onClick={() => {
                        setSelectedMasterId(m.userId)
                        setStep('datetime')
                      }}
                      className="flex items-center gap-3.5 p-3.5 rounded-2xl border border-line bg-white hover:border-line-strong transition-all text-left"
                    >
                      <Avatar avatarUrl={m.avatarUrl} firstName={m.firstName} lastName={m.lastName} size={40} />
                      <div>
                        <p className="font-semibold text-sm text-ink">
                          {m.firstName} {m.lastName}
                        </p>
                        {m.bio && <p className="text-xs text-muted mt-0.5">{m.bio}</p>}
                      </div>
                      <Icon
                        name="chevron-right"
                        size={16}
                        strokeWidth={1.8}
                        className="ml-auto text-line-strong shrink-0"
                      />
                    </button>
                  ))}
                </div>
              ) : (
                <p className="text-center text-muted py-8">Нет доступных мастеров для этой услуги</p>
              )}
            </div>
          )}

          {/* ── DateTime ── */}
          {step === 'datetime' && (
            <div>
              <BackLink onClick={() => setStep('master')}>
                {selectedMaster ? `${selectedMaster.firstName} ${selectedMaster.lastName}` : 'Мастер'}
              </BackLink>

              {/* Date grid */}
              <h3 className="text-[14.5px] font-semibold text-[#4A4038] mb-3">Дата</h3>
              <div className="grid grid-cols-2 gap-2 mb-5">
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

              {/* Time grid — shown only after date selected */}
              {selectedDate && (
                <>
                  <h3 className="text-[14.5px] font-semibold text-[#4A4038] mb-3">Время</h3>
                  {slotsLoading ? (
                    <div className="grid grid-cols-4 gap-2">
                      {Array.from({ length: 8 }).map((_, i) => (
                        <div key={i} className="h-9 bg-cream-deep rounded-xl animate-pulse" />
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
                  ) : (
                    <p className="text-center text-muted py-4 text-sm">Нет доступных слотов на этот день</p>
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
            <div className="flex flex-col gap-3.5">
              <BackLink onClick={() => setStep('datetime')}>Изменить дату / время</BackLink>

              <div className="bg-cream-deep rounded-2xl p-4 text-[13.5px] text-ink">
                <div className="font-semibold">{selectedService?.name}</div>
                {selectedMaster && (
                  <div className="text-ink-soft mt-0.5">
                    {selectedMaster.firstName} {selectedMaster.lastName}
                  </div>
                )}
                <div className="text-ink-soft mt-0.5">
                  {formattedDate} · {selectedTime}
                </div>
              </div>

              <h3 className="text-[14.5px] font-semibold text-[#4A4038]">Данные клиента</h3>

              <Input
                label="Имя клиента *"
                placeholder="Иван Иванов"
                value={clientName}
                onChange={(e) => setClientName(e.target.value)}
              />
              <PhoneInput
                label="Телефон *"
                value={clientPhone}
                onChange={setClientPhone}
                error={
                  clientPhone && !isRussianPhone(clientPhone)
                    ? 'Пока принимаем только российские номера, в формате +7 (900) 000-00-00'
                    : undefined
                }
              />
              <Input
                label="Email (необязательно)"
                type="email"
                placeholder="client@email.com"
                value={clientEmail}
                onChange={(e) => setClientEmail(e.target.value)}
              />

              <div className="flex flex-col gap-1.5">
                <label className="text-[13px] font-medium text-[#4A4038]">Комментарий</label>
                <textarea
                  className="rounded-xl border border-line px-4 py-3 text-sm outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep resize-none bg-white text-ink"
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
                disabled={!clientName || !isRussianPhone(clientPhone)}
                className="w-full"
              >
                Записать клиента
              </Button>

              {mutation.isError && (
                <p className="text-sm text-danger text-center">{getBookingErrorMessage(mutation.error)}</p>
              )}
            </div>
          )}

          {/* ── Done ── */}
          {step === 'done' && (
            <div className="text-center py-5">
              <div className="w-14 h-14 rounded-full bg-success-bg flex items-center justify-center mx-auto mb-[18px]">
                <Icon name="check" size={26} strokeWidth={1.8} className="text-success" />
              </div>
              <h3 className="font-serif text-xl font-medium text-ink mb-2">Клиент записан!</h3>
              <p className="text-sm text-ink-soft mb-6">
                {clientName} · {formattedDate} в {selectedTime}
              </p>
              <Button onClick={onClose} variant="secondary" size="lg">
                Закрыть
              </Button>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
