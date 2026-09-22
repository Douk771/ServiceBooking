import { useEffect, useState } from 'react'
import { useQuery, useMutation } from '@tanstack/react-query'
import { format } from 'date-fns'
import { ru } from 'date-fns/locale'
import { bookingsApi } from '../../api/bookings'
import { companiesApi } from '../../api/companies'
import { servicesApi } from '../../api/services'
import { useAuthStore } from '../../store/authStore'
import { Button } from '../ui/Button'
import { Input } from '../ui/Input'
import { PhoneInput } from '../ui/PhoneInput'
import { Icon } from '../ui/Icon'
import { Avatar } from '../ui/Avatar'
import { Link } from 'react-router-dom'
import { useOverlayDismiss } from '../../hooks/useOverlayDismiss'
import { getBookingErrorMessage } from '../../utils/bookingError'
import { isRussianPhone } from '../../utils/phone'
import { SmartCaptcha, smartCaptchaEnabled } from './SmartCaptcha'
import { BookingCalendar } from './BookingCalendar'
import type { Company, Service } from '../../types'

// US-67 (API_CONTRACT_CYCLE6.md §41.1/§43.1) — server rejects a visit of more than 5 services.
const MAX_SERVICES = 5

interface Props {
  service: Service
  company: Company
  onClose: () => void
  /**
   * US-67 (§43.3): the embed widget (`EmbedPage.tsx`) deliberately stays single-service — pass
   * `false` there. Everywhere else (the client-facing booking on `CompanyPage`) a visit can carry
   * up to `MAX_SERVICES` services, so this defaults to `true`.
   */
  allowMultipleServices?: boolean
}

type Step = 'services' | 'master' | 'date' | 'slot' | 'info' | 'done'

function BackLink({ onClick, children }: { onClick: () => void; children: React.ReactNode }) {
  return (
    <button onClick={onClick} className="flex items-center gap-1.5 text-[13px] text-ink-soft hover:text-ink mb-4">
      <Icon name="chevron-left" size={13} strokeWidth={1.8} />
      {children}
    </button>
  )
}

// US-64: label a date the way the flat list used to ("Сегодня" / "Завтра" / "24 сен, ср") — the
// calendar picks a specific date, but the rest of the flow (summary, confirmation) still wants a
// short human label for it.
function formatDateLabel(dateStr: string): string {
  const [y, m, d] = dateStr.split('-').map(Number)
  const date = new Date(y, m - 1, d)
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  const diffDays = Math.round((date.getTime() - today.getTime()) / 86400000)
  if (diffDays === 0) return 'Сегодня'
  if (diffDays === 1) return 'Завтра'
  return format(date, 'd MMM, EEE', { locale: ru })
}

export function BookingModal({ service, company, onClose, allowMultipleServices = true }: Props) {
  const { isAuthenticated } = useAuthStore()
  const [step, setStep] = useState<Step>(allowMultipleServices ? 'services' : 'master')
  const [extraServices, setExtraServices] = useState<Service[]>([])
  const [selectedMasterId, setSelectedMasterId] = useState('')
  const [selectedDate, setSelectedDate] = useState('')
  const [selectedSlot, setSelectedSlot] = useState('')
  const [guestName, setGuestName] = useState('')
  const [guestPhone, setGuestPhone] = useState('')
  const [guestEmail, setGuestEmail] = useState('')
  const [notes, setNotes] = useState('')
  const [captchaToken, setCaptchaToken] = useState('')

  // US-67: the full list of the visit's services — the one the client clicked "Записаться" on,
  // plus whatever they added on the services step. Duration/price shown to the client are always
  // the SUM across this list, never just the first service's.
  const allServices = [service, ...extraServices]
  const totalDurationMinutes = allServices.reduce((sum, s) => sum + s.durationMinutes, 0)
  const totalPrice = allServices.reduce((sum, s) => sum + s.price, 0)
  const extraServiceIds = allowMultipleServices ? extraServices.map((s) => s.id) : undefined

  // Full company service catalogue, for the "add another service" list. Not needed by the embed
  // widget, which never shows this step.
  const { data: companyServices, isLoading: companyServicesLoading } = useQuery({
    queryKey: ['services', company.id],
    queryFn: () => servicesApi.getByCompany(company.id),
    enabled: allowMultipleServices,
  })
  const addableServices = (companyServices ?? []).filter((s) => !allServices.some((picked) => picked.id === s.id))

  // Load masters that can perform this service. US-62 (backend) already filters out staff who
  // toggled off "provides services" — this list is the post-filter, active count for US-64.
  // Filtered by the primary service only (API_CONTRACT_CYCLE6.md §40.1 doesn't take a service list);
  // if an added service turns out to be one this master doesn't do, that surfaces later as a 400
  // on the slots/availability call ("Мастер не оказывает услугу: …"), not silently here.
  const { data: masters, isLoading: mastersLoading } = useQuery({
    queryKey: ['company-masters', company.id, service.id],
    queryFn: () => companiesApi.getMasters(company.id, service.id),
  })

  // US-64: with exactly one active master there's nothing to pick — auto-select and skip the
  // step entirely. With zero, there's nobody to book with at all; the master step stays on screen
  // to show that message rather than a broken empty list further down the flow.
  // Keyed on `step` (not just on `masters` loading) — with the services step now shown first
  // (allowMultipleServices), `masters` can finish loading well before the user ever reaches the
  // master step, and a one-shot "masters just arrived" effect would then miss the skip entirely.
  useEffect(() => {
    if (step === 'master' && masters && masters.length === 1 && !selectedMasterId) {
      setSelectedMasterId(masters[0].userId)
      setStep('date')
    }
  }, [step, masters, selectedMasterId])

  const now = new Date()
  const todayStr = format(now, 'yyyy-MM-dd')
  const nowMinutes = now.getHours() * 60 + now.getMinutes()
  const timeToMinutes = (t: string) => {
    const [h, m] = t.slice(0, 5).split(':').map(Number)
    return h * 60 + m
  }

  const {
    data: rawSlots,
    isLoading: slotsLoading,
    error: slotsError,
  } = useQuery({
    queryKey: ['slots', company.id, selectedMasterId, service.id, extraServiceIds, selectedDate],
    queryFn: () => bookingsApi.getSlots(company.id, selectedMasterId, service.id, extraServiceIds, selectedDate),
    enabled: !!selectedMasterId && !!selectedDate,
    staleTime: 0, // always fetch fresh — bookings made by others should be reflected immediately
    retry: false,
  })
  const slots = rawSlots?.filter((s) => selectedDate !== todayStr || timeToMinutes(s.start) > nowMinutes)

  const mutation = useMutation({
    mutationFn: () =>
      bookingsApi.create({
        companyId: company.id,
        serviceId: service.id,
        serviceIds: allowMultipleServices ? allServices.map((s) => s.id) : undefined,
        masterId: selectedMasterId,
        date: selectedDate,
        startTime: selectedSlot,
        notes,
        guestName: isAuthenticated() ? undefined : guestName,
        guestPhone: isAuthenticated() ? undefined : guestPhone,
        guestEmail: isAuthenticated() ? undefined : guestEmail,
        captchaToken: isAuthenticated() ? undefined : captchaToken || undefined,
      }),
    onSuccess: () => setStep('done'),
  })

  // US-67: adding a 6th service is rejected up front with a clear message rather than sent to the
  // server to bounce back as a 400.
  const [servicesLimitMessage, setServicesLimitMessage] = useState('')
  const addService = (s: Service) => {
    if (allServices.length >= MAX_SERVICES) {
      setServicesLimitMessage(`За один визит можно выбрать не больше ${MAX_SERVICES} услуг`)
      return
    }
    setServicesLimitMessage('')
    setExtraServices((prev) => [...prev, s])
  }
  const removeService = (id: string) => {
    setServicesLimitMessage('')
    setExtraServices((prev) => prev.filter((s) => s.id !== id))
  }

  const pickMaster = (id: string) => {
    setSelectedMasterId(id)
    setStep('date')
  }

  const selectedMaster = masters?.find((m) => m.userId === selectedMasterId)

  // US-64: the progress indicator must reflect the actual number of steps — with a single master
  // (or while the count isn't known yet) the "choose a master" step never renders, so it shouldn't
  // be counted either. Only show it when there's a real choice to make.
  const showMasterStep = !!masters && masters.length > 1
  const noMastersAvailable = !!masters && masters.length === 0
  const baseSteps: Step[] = showMasterStep ? ['master', 'date', 'slot', 'info'] : ['date', 'slot', 'info']
  const progressSteps: Step[] = allowMultipleServices ? ['services', ...baseSteps] : baseSteps
  const currentIdx = progressSteps.indexOf(step)

  const dismiss = useOverlayDismiss(onClose)

  return (
    <div className="fixed inset-0 bg-ink/45 backdrop-blur-sm z-50 flex items-center justify-center p-5" {...dismiss}>
      <div className="bg-cream rounded-[26px] shadow-modal w-full max-w-[440px] max-h-[88vh] overflow-y-auto">
        {/* Header */}
        <div className="p-6 pb-[22px] border-b border-line">
          <div className="flex items-center justify-between">
            <div>
              <h2 className="font-serif text-[19px] font-medium text-ink mb-0.5">Запись на услугу</h2>
              <p className="text-[13px] text-ink-soft">
                {allServices.map((s) => s.name).join(', ')} · {totalDurationMinutes} мин ·{' '}
                {totalPrice.toLocaleString('ru-RU')} ₽
              </p>
            </div>
            <button onClick={onClose} className="text-muted hover:text-ink shrink-0">
              <Icon name="x" size={18} strokeWidth={1.8} />
            </button>
          </div>

          {/* Progress bar */}
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
          {/* ── Step: Services (US-67) ── */}
          {step === 'services' && (
            <div>
              <h3 className="text-[14.5px] font-semibold text-[#4A4038] mb-4">Услуги за визит</h3>

              <div className="flex flex-col gap-2 mb-4">
                {allServices.map((s) => (
                  <div
                    key={s.id}
                    className="flex items-center justify-between gap-3 p-3 rounded-xl border border-line bg-white"
                  >
                    <div>
                      <p className="text-sm font-medium text-ink">{s.name}</p>
                      <p className="text-xs text-muted mt-0.5">
                        {s.durationMinutes} мин · {s.price.toLocaleString('ru-RU')} ₽
                      </p>
                    </div>
                    {s.id !== service.id && (
                      <button
                        type="button"
                        aria-label={`Убрать «${s.name}»`}
                        onClick={() => removeService(s.id)}
                        className="text-muted hover:text-danger shrink-0"
                      >
                        <Icon name="x" size={16} strokeWidth={1.8} />
                      </button>
                    )}
                  </div>
                ))}
              </div>

              <div className="flex items-center justify-between text-[13.5px] font-semibold text-ink bg-cream-deep rounded-xl px-3.5 py-2.5 mb-4">
                <span>Итого</span>
                <span>
                  {totalDurationMinutes} мин · {totalPrice.toLocaleString('ru-RU')} ₽
                </span>
              </div>

              {servicesLimitMessage && (
                <p className="text-sm text-danger text-center mb-3">{servicesLimitMessage}</p>
              )}

              {addableServices.length > 0 && (
                <>
                  <h4 className="text-[13px] font-semibold text-ink-soft mb-2">Добавить услугу</h4>
                  {companyServicesLoading ? (
                    <div className="flex flex-col gap-2">
                      {Array.from({ length: 2 }).map((_, i) => (
                        <div key={i} className="h-12 bg-cream-deep rounded-xl animate-pulse" />
                      ))}
                    </div>
                  ) : addableServices.length > 0 ? (
                    <div className="flex flex-col gap-2 mb-2">
                      {addableServices.map((s) => (
                        <button
                          key={s.id}
                          type="button"
                          onClick={() => addService(s)}
                          className="flex items-center justify-between gap-3 p-3 rounded-xl border border-line bg-white hover:border-line-strong transition-all text-left"
                        >
                          <div>
                            <p className="text-sm font-medium text-ink">{s.name}</p>
                            <p className="text-xs text-muted mt-0.5">
                              {s.durationMinutes} мин · {s.price.toLocaleString('ru-RU')} ₽
                            </p>
                          </div>
                          <Icon name="plus" size={16} strokeWidth={1.8} className="text-line-strong shrink-0" />
                        </button>
                      ))}
                    </div>
                  ) : null}
                </>
              )}

              <Button className="w-full mt-3" onClick={() => setStep('master')}>
                Продолжить
              </Button>
            </div>
          )}

          {/* ── Step: Master ── */}
          {step === 'master' && (
            <div>
              {allowMultipleServices && (
                <BackLink onClick={() => setStep('services')}>Изменить услуги</BackLink>
              )}
              {!mastersLoading && noMastersAvailable ? (
                <div className="text-center py-8">
                  <div className="w-12 h-12 rounded-full bg-cream-deep flex items-center justify-center mx-auto mb-3">
                    <Icon name="users" size={20} strokeWidth={1.8} className="text-muted" />
                  </div>
                  <p className="text-sm font-medium text-ink mb-1">Сейчас записаться нельзя</p>
                  <p className="text-[13px] text-ink-soft">
                    На эту услугу временно нет свободных специалистов. Загляните позже или свяжитесь с
                    салоном напрямую.
                  </p>
                </div>
              ) : (
                <>
                  <h3 className="text-[14.5px] font-semibold text-[#4A4038] mb-4">Выберите мастера</h3>
                  {mastersLoading ? (
                    <div className="flex flex-col gap-2.5">
                      {Array.from({ length: 2 }).map((_, i) => (
                        <div key={i} className="h-16 bg-cream-deep rounded-2xl animate-pulse" />
                      ))}
                    </div>
                  ) : (
                    <div className="flex flex-col gap-2.5">
                      {masters?.map((m) => (
                        <button
                          key={m.userId}
                          onClick={() => pickMaster(m.userId)}
                          className="flex items-center gap-3.5 p-3.5 rounded-2xl border border-line bg-white hover:border-line-strong transition-all text-left"
                        >
                          <Avatar
                            avatarUrl={m.avatarUrl}
                            firstName={m.firstName}
                            lastName={m.lastName}
                            size={40}
                            className="text-[13px]"
                          />
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
                  )}
                </>
              )}
            </div>
          )}

          {/* ── Step: Date ── */}
          {step === 'date' && (
            <div>
              {showMasterStep && (
                <BackLink onClick={() => setStep('master')}>
                  {selectedMaster ? `${selectedMaster.firstName} ${selectedMaster.lastName}` : 'Мастер'}
                </BackLink>
              )}
              <h3 className="text-[14.5px] font-semibold text-[#4A4038] mb-4">Выберите дату</h3>
              <BookingCalendar
                companyId={company.id}
                masterId={selectedMasterId}
                serviceId={service.id}
                extraServiceIds={extraServiceIds}
                selectedDate={selectedDate}
                onSelectDate={(date) => {
                  setSelectedDate(date)
                  setStep('slot')
                }}
              />
            </div>
          )}

          {/* ── Step: Slot ── */}
          {step === 'slot' && (
            <div>
              <BackLink onClick={() => setStep('date')}>Изменить дату</BackLink>
              <h3 className="text-[14.5px] font-semibold text-[#4A4038] mb-4">Выберите время</h3>
              {slotsLoading ? (
                <div className="grid grid-cols-3 gap-2">
                  {Array.from({ length: 9 }).map((_, i) => (
                    <div key={i} className="h-10 bg-cream-deep rounded-xl animate-pulse" />
                  ))}
                </div>
              ) : slots && slots.length > 0 ? (
                <div className="grid grid-cols-3 gap-2">
                  {slots.map((s) => (
                    <button
                      key={s.start}
                      onClick={() => {
                        setSelectedSlot(s.start)
                        setStep('info')
                      }}
                      className={`py-[11px] rounded-xl text-[13.5px] font-medium border transition-all ${
                        selectedSlot === s.start
                          ? 'bg-ink text-cream border-ink'
                          : 'border-line bg-white hover:border-line-strong'
                      }`}
                    >
                      {s.start.slice(0, 5)}
                    </button>
                  ))}
                </div>
              ) : slotsError ? (
                // US-67 (§41.1): a service added after the master was picked may turn out to be one
                // the master doesn't do — the server says so explicitly, so show that text instead
                // of a bare empty state that reads as "just no free time".
                <p className="text-center text-danger py-8">{getBookingErrorMessage(slotsError)}</p>
              ) : (
                <p className="text-center text-muted py-8">Нет доступных слотов на этот день</p>
              )}
            </div>
          )}

          {/* ── Step: Info ── */}
          {step === 'info' && (
            <div className="flex flex-col gap-3.5">
              <BackLink onClick={() => setStep('slot')}>Изменить время</BackLink>

              <div className="bg-cream-deep rounded-2xl p-4 text-[13.5px] text-ink">
                {/* US-67: the visit's every service, plus the summed duration/price — never just
                    the first service, so the client confirms what they're actually paying for. */}
                <div className="font-semibold">{allServices.map((s) => s.name).join(', ')}</div>
                <div className="text-ink-soft mt-0.5">
                  {totalDurationMinutes} мин · {totalPrice.toLocaleString('ru-RU')} ₽
                </div>
                {selectedMaster && (
                  <div className="text-ink-soft mt-0.5">
                    {selectedMaster.firstName} {selectedMaster.lastName}
                  </div>
                )}
                <div className="text-ink-soft mt-0.5">
                  {selectedDate && formatDateLabel(selectedDate)} · {selectedSlot.slice(0, 5)}
                </div>
              </div>

              {!isAuthenticated() && (
                <>
                  <Input
                    label="Ваше имя *"
                    placeholder="Иван Иванов"
                    value={guestName}
                    onChange={(e) => setGuestName(e.target.value)}
                  />
                  <PhoneInput
                    label="Телефон *"
                    value={guestPhone}
                    onChange={setGuestPhone}
                    error={
                      guestPhone && !isRussianPhone(guestPhone)
                        ? 'Пока принимаем только российские номера, в формате +7 (900) 000-00-00'
                        : undefined
                    }
                  />
                  <Input
                    label="Email"
                    type="email"
                    placeholder="your@email.com"
                    value={guestEmail}
                    onChange={(e) => setGuestEmail(e.target.value)}
                  />
                </>
              )}

              <div className="flex flex-col gap-1.5">
                <label className="text-[13px] font-medium text-[#4A4038]">Комментарий (необязательно)</label>
                <textarea
                  className="rounded-xl border border-line px-4 py-3 text-sm outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep resize-none bg-white text-ink"
                  rows={3}
                  placeholder="Пожелания или вопросы..."
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                />
              </div>

              {!isAuthenticated() && smartCaptchaEnabled && (
                <div className="flex flex-col gap-1">
                  <SmartCaptcha onToken={setCaptchaToken} />
                  <p className="text-xs text-muted">Подтвердите, что вы не робот (Yandex SmartCaptcha)</p>
                </div>
              )}

              <Button
                size="lg"
                loading={mutation.isPending}
                onClick={() => mutation.mutate()}
                disabled={
                  (!isAuthenticated() && (!guestName || !isRussianPhone(guestPhone))) ||
                  (!isAuthenticated() && smartCaptchaEnabled && !captchaToken)
                }
                className="w-full"
              >
                Подтвердить запись
              </Button>

              {!isAuthenticated() && (
                <p className="text-center text-xs text-muted -mt-1.5">
                  Нажимая «Подтвердить запись», вы соглашаетесь с{' '}
                  <Link to="/terms" target="_blank" className="text-gold hover:text-gold-dark">
                    пользовательским соглашением
                  </Link>{' '}
                  и{' '}
                  <Link to="/privacy" target="_blank" className="text-gold hover:text-gold-dark">
                    политикой обработки персональных данных
                  </Link>
                </p>
              )}
              {/* US-33 п. 1 — service messages about the booking need no separate opt-in checkbox, but
                  the client must be told they'll arrive in WhatsApp from the salon (guest path included). */}
              <p className="text-center text-xs text-muted -mt-1.5">
                Оставляя номер телефона, вы получите сервисные сообщения о записи в WhatsApp от салона. Подробнее — в{' '}
                <Link to="/privacy" target="_blank" className="text-gold hover:text-gold-dark">
                  политике обработки персональных данных
                </Link>
                .
              </p>

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
              <h3 className="font-serif text-xl font-medium text-ink mb-2">Запись подтверждена!</h3>
              <p className="text-sm text-ink-soft mb-6">
                Ждём вас {selectedDate && formatDateLabel(selectedDate)} в {selectedSlot.slice(0, 5)}
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
