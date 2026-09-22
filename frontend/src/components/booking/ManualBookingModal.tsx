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

// US-67 (API_CONTRACT_CYCLE6.md §41.1/§43.1) — server rejects a visit of more than 5 services.
const MAX_SERVICES = 5

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
  // US-67: staff picks the same way the client does — one or more services, up to MAX_SERVICES.
  const [selectedServices, setSelectedServices] = useState<Service[]>([])
  const [servicesLimitMessage, setServicesLimitMessage] = useState('')
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

  // Primary service drives master filtering (§40.1 only takes one `serviceId`) and is required to
  // equal `serviceIds[0]` on create (§43.1).
  const primaryService = selectedServices[0] ?? null
  const extraServiceIds = selectedServices.slice(1).map((s) => s.id)
  const totalDurationMinutes = selectedServices.reduce((sum, s) => sum + s.durationMinutes, 0)
  const totalPrice = selectedServices.reduce((sum, s) => sum + s.price, 0)

  const { data: masters, isLoading: mastersLoading } = useQuery({
    queryKey: ['company-masters', selectedCompany?.id, primaryService?.id],
    queryFn: () => companiesApi.getMasters(selectedCompany!.id, primaryService!.id),
    enabled: !!selectedCompany && !!primaryService,
  })

  // US-64: same rule as the client-facing BookingModal — exactly one active master means there's
  // nothing to pick, so auto-select and skip the step ("сделать единообразно").
  // Keyed on `step` — masters can finish loading while the operator is still on the services step,
  // and a one-shot "masters just arrived" effect would then miss the skip entirely.
  useEffect(() => {
    if (step === 'master' && masters && masters.length === 1 && !selectedMasterId) {
      setSelectedMasterId(masters[0].userId)
      setStep('datetime')
    }
  }, [step, masters, selectedMasterId])

  const now = new Date()
  const todayStr = format(now, 'yyyy-MM-dd')
  const nowMinutes = now.getHours() * 60 + now.getMinutes()

  // Today is only offered as a booking date if the master still has at least one slot today
  // that both respects working hours/breaks and hasn't passed yet.
  const { data: slotsToday = [] } = useQuery({
    queryKey: ['slots', selectedCompany?.id, selectedMasterId, primaryService?.id, extraServiceIds, todayStr, 'manual'],
    queryFn: () =>
      bookingsApi.getSlots(selectedCompany!.id, selectedMasterId, primaryService!.id, extraServiceIds, todayStr, true),
    enabled: !!selectedCompany && !!selectedMasterId && !!primaryService,
    staleTime: 0,
  })
  const hasAvailableSlotToday = slotsToday.some((s) => timeToMinutes(s.start) > nowMinutes)

  const {
    data: rawSlots,
    isLoading: slotsLoading,
    error: slotsError,
  } = useQuery({
    queryKey: [
      'slots',
      selectedCompany?.id,
      selectedMasterId,
      primaryService?.id,
      extraServiceIds,
      selectedDate,
      'manual',
    ],
    queryFn: () =>
      bookingsApi.getSlots(selectedCompany!.id, selectedMasterId, primaryService!.id, extraServiceIds, selectedDate, true),
    enabled: !!selectedCompany && !!selectedMasterId && !!primaryService && !!selectedDate,
    staleTime: 0,
    retry: false,
  })
  const slots = (rawSlots ?? []).filter((s) => selectedDate !== todayStr || timeToMinutes(s.start) > nowMinutes)

  const mutation = useMutation({
    mutationFn: () =>
      bookingsApi.create({
        companyId: selectedCompany!.id,
        serviceId: primaryService!.id,
        serviceIds: selectedServices.map((s) => s.id),
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

  const toggleService = (s: Service) => {
    setServicesLimitMessage('')
    setSelectedServices((prev) => {
      const exists = prev.some((p) => p.id === s.id)
      if (exists) return prev.filter((p) => p.id !== s.id)
      if (prev.length >= MAX_SERVICES) {
        setServicesLimitMessage(`За один визит можно выбрать не больше ${MAX_SERVICES} услуг`)
        return prev
      }
      return [...prev, s]
    })
  }

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

  // US-64: mirrors BookingModal — the "choose a master" step is only counted when there's an
  // actual choice (2+ active masters); with 0 or 1 it never renders.
  const showMasterStep = !!masters && masters.length > 1
  const baseSteps: Step[] = showMasterStep
    ? ['service', 'master', 'datetime', 'client']
    : ['service', 'datetime', 'client']
  const progressSteps: Step[] = companies?.length === 1 ? baseSteps : ['company', ...baseSteps]
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
              {selectedServices.length > 0 && (
                <p className="text-[13px] text-ink-soft">
                  {selectedServices.map((s) => s.name).join(', ')} · {totalDurationMinutes} мин ·{' '}
                  {totalPrice.toLocaleString('ru-RU')} ₽
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

          {/* ── Service (US-67: staff picks 1..5 services, same as the client) ── */}
          {step === 'service' && (
            <div>
              {companies && companies.length > 1 && (
                <BackLink onClick={() => setStep('company')}>{selectedCompany?.name}</BackLink>
              )}
              <h3 className="text-[14.5px] font-semibold text-[#4A4038] mb-4">Выберите услуги</h3>
              {servicesLoading ? (
                <div className="flex flex-col gap-2.5">
                  {[1, 2, 3].map((i) => (
                    <div key={i} className="h-14 bg-cream-deep rounded-2xl animate-pulse" />
                  ))}
                </div>
              ) : services && services.length > 0 ? (
                <>
                  <div className="flex flex-col gap-2.5 mb-4">
                    {services.map((s) => {
                      const checked = selectedServices.some((p) => p.id === s.id)
                      return (
                        <button
                          key={s.id}
                          onClick={() => toggleService(s)}
                          aria-pressed={checked}
                          className={`flex items-center justify-between p-3.5 rounded-2xl border transition-all text-left ${
                            checked ? 'border-ink bg-cream-deep' : 'border-line bg-white hover:border-line-strong'
                          }`}
                        >
                          <div>
                            <p className="font-semibold text-sm text-ink">{s.name}</p>
                            <p className="text-xs text-muted mt-0.5">
                              {s.durationMinutes} мин · {s.price.toLocaleString('ru-RU')} ₽
                            </p>
                          </div>
                          <Icon
                            name={checked ? 'check' : 'plus'}
                            size={16}
                            strokeWidth={1.8}
                            className={checked ? 'text-ink shrink-0' : 'text-line-strong shrink-0'}
                          />
                        </button>
                      )
                    })}
                  </div>

                  {servicesLimitMessage && (
                    <p className="text-sm text-danger text-center mb-3">{servicesLimitMessage}</p>
                  )}

                  {selectedServices.length > 0 && (
                    <div className="flex items-center justify-between text-[13.5px] font-semibold text-ink bg-cream-deep rounded-xl px-3.5 py-2.5 mb-4">
                      <span>Итого</span>
                      <span>
                        {totalDurationMinutes} мин · {totalPrice.toLocaleString('ru-RU')} ₽
                      </span>
                    </div>
                  )}

                  <Button
                    className="w-full"
                    disabled={selectedServices.length === 0}
                    onClick={() => setStep('master')}
                  >
                    Продолжить
                  </Button>
                </>
              ) : (
                <p className="text-center text-muted py-8">Нет доступных услуг</p>
              )}
            </div>
          )}

          {/* ── Master ── */}
          {step === 'master' && (
            <div>
              <BackLink onClick={() => setStep('service')}>Изменить услуги</BackLink>
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
              <BackLink onClick={() => setStep(showMasterStep ? 'master' : 'service')}>
                {showMasterStep && selectedMaster
                  ? `${selectedMaster.firstName} ${selectedMaster.lastName}`
                  : showMasterStep
                    ? 'Мастер'
                    : 'Изменить услуги'}
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
                  ) : slotsError ? (
                    // US-67 (§41.1): an added service the master doesn't do surfaces as an explicit
                    // 400 — show it instead of a plain "no slots" that would hide the real reason.
                    <p className="text-center text-danger py-4 text-sm">{getBookingErrorMessage(slotsError)}</p>
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
                <div className="font-semibold">{selectedServices.map((s) => s.name).join(', ')}</div>
                <div className="text-ink-soft mt-0.5">
                  {totalDurationMinutes} мин · {totalPrice.toLocaleString('ru-RU')} ₽
                </div>
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
