import { useEffect, useState } from 'react'
import { useQuery, useMutation } from '@tanstack/react-query'
import { format } from 'date-fns'
import { ru } from 'date-fns/locale'
import { bookingsApi } from '../../api/bookings'
import { companiesApi } from '../../api/companies'
import { useAuthStore } from '../../store/authStore'
import { Button } from '../ui/Button'
import { Input } from '../ui/Input'
import { Icon } from '../ui/Icon'
import { Avatar } from '../ui/Avatar'
import { Link } from 'react-router-dom'
import { useOverlayDismiss } from '../../hooks/useOverlayDismiss'
import { getBookingErrorMessage } from '../../utils/bookingError'
import { SmartCaptcha, smartCaptchaEnabled } from './SmartCaptcha'
import { BookingCalendar } from './BookingCalendar'
import type { Company, Service } from '../../types'

interface Props {
  service: Service
  company: Company
  onClose: () => void
}

type Step = 'master' | 'date' | 'slot' | 'info' | 'done'

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

export function BookingModal({ service, company, onClose }: Props) {
  const { isAuthenticated } = useAuthStore()
  const [step, setStep] = useState<Step>('master')
  const [selectedMasterId, setSelectedMasterId] = useState('')
  const [selectedDate, setSelectedDate] = useState('')
  const [selectedSlot, setSelectedSlot] = useState('')
  const [guestName, setGuestName] = useState('')
  const [guestPhone, setGuestPhone] = useState('')
  const [guestEmail, setGuestEmail] = useState('')
  const [notes, setNotes] = useState('')
  const [captchaToken, setCaptchaToken] = useState('')

  // Load masters that can perform this service. US-62 (backend) already filters out staff who
  // toggled off "provides services" — this list is the post-filter, active count for US-64.
  const { data: masters, isLoading: mastersLoading } = useQuery({
    queryKey: ['company-masters', company.id, service.id],
    queryFn: () => companiesApi.getMasters(company.id, service.id),
  })

  // US-64: with exactly one active master there's nothing to pick — auto-select and skip the
  // step entirely. With zero, there's nobody to book with at all; the master step stays on screen
  // to show that message rather than a broken empty list further down the flow.
  useEffect(() => {
    if (masters && masters.length === 1 && !selectedMasterId) {
      setSelectedMasterId(masters[0].userId)
      setStep((s) => (s === 'master' ? 'date' : s))
    }
  }, [masters, selectedMasterId])

  const now = new Date()
  const todayStr = format(now, 'yyyy-MM-dd')
  const nowMinutes = now.getHours() * 60 + now.getMinutes()
  const timeToMinutes = (t: string) => {
    const [h, m] = t.slice(0, 5).split(':').map(Number)
    return h * 60 + m
  }

  const { data: rawSlots, isLoading: slotsLoading } = useQuery({
    queryKey: ['slots', company.id, selectedMasterId, service.id, selectedDate],
    queryFn: () => bookingsApi.getSlots(company.id, selectedMasterId, service.id, selectedDate),
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
        guestName: isAuthenticated() ? undefined : guestName,
        guestPhone: isAuthenticated() ? undefined : guestPhone,
        guestEmail: isAuthenticated() ? undefined : guestEmail,
        captchaToken: isAuthenticated() ? undefined : captchaToken || undefined,
      }),
    onSuccess: () => setStep('done'),
  })

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
  const progressSteps: Step[] = showMasterStep ? ['master', 'date', 'slot', 'info'] : ['date', 'slot', 'info']
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
                {service.name} · {service.durationMinutes} мин · {service.price.toLocaleString('ru-RU')} ₽
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
          {/* ── Step: Master ── */}
          {step === 'master' && (
            <div>
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
                <div className="font-semibold">{service.name}</div>
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
                  <Input
                    label="Телефон *"
                    type="tel"
                    placeholder="+7 999 000 00 00"
                    value={guestPhone}
                    onChange={(e) => setGuestPhone(e.target.value)}
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
                  (!isAuthenticated() && (!guestName || !guestPhone)) ||
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
