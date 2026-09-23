import { useEffect, useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
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
import { useLegalText } from '../../hooks/useLegalText'
import { getBookingErrorMessage } from '../../utils/bookingError'
import { isRussianPhone } from '../../utils/phone'
import { findSection, splitLegalSections } from '../../utils/legalSections'
import { SmartCaptcha, smartCaptchaEnabled } from './SmartCaptcha'
import { BookingCalendar } from './BookingCalendar'
import type { Company, Service } from '../../types'

// US-67 (API_CONTRACT_CYCLE6.md §41.1/§43.1) — server rejects a visit of more than 5 services.
const MAX_SERVICES = 5

/**
 * ARCHITECTURE_CYCLE10.md §108 — the ONE booking modal, replacing the old BookingModal +
 * ManualBookingModal pair. Three entry points:
 *   - `company` + `service` set        → CompanyPage.tsx (client-facing)
 *   - same, `allowMultipleServices=false` → EmbedPage.tsx (widget, deliberately single-service)
 *   - neither set                       → MyBookingsPage.tsx "Записать клиента" (staff picks company)
 *
 * `staffMode` is NEVER taken from props, `authStore`, or which entry point opened the modal — it is
 * read exclusively from the server's `availability.staffMode` (§108.2/§131), reported up from
 * `BookingCalendar` via `onStaffModeChange`. Until the calendar has answered, the modal behaves as
 * the plain client flow.
 */
interface Props {
  company?: Company
  service?: Service
  onClose: () => void
  /**
   * US-67 (§43.3): the embed widget (`EmbedPage.tsx`) deliberately stays single-service — pass
   * `false` there. Everywhere else a visit can carry up to `MAX_SERVICES` services (defaults `true`).
   */
  allowMultipleServices?: boolean
}

type Step = 'company' | 'services' | 'master' | 'date' | 'slot' | 'info' | 'done'

function BackLink({ onClick, children }: { onClick: () => void; children: React.ReactNode }) {
  return (
    <button onClick={onClick} className="flex items-center gap-1.5 text-[13px] text-ink-soft hover:text-ink mb-4">
      <Icon name="chevron-left" size={13} strokeWidth={1.8} />
      {children}
    </button>
  )
}

// US-64: label a date the way the old flat list used to ("Сегодня" / "Завтра" / "24 сен, ср").
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

export function BookingModal({ company, service, onClose, allowMultipleServices = true }: Props) {
  const qc = useQueryClient()
  const { isAuthenticated } = useAuthStore()

  const [selectedCompany, setSelectedCompany] = useState<Company | null>(company ?? null)
  const effectiveCompany = company ?? selectedCompany

  // §108.3 — services picking has two shapes: a fixed, non-removable primary + "add more" (client
  // arrived from a service card), or a from-scratch multi-select (staff picking with nothing
  // preselected). `service` being set is what tells them apart.
  const [extraServices, setExtraServices] = useState<Service[]>([]) // used when `service` is fixed
  const [pickedServices, setPickedServices] = useState<Service[]>([]) // used when picking from scratch
  const [servicesLimitMessage, setServicesLimitMessage] = useState('')

  const [step, setStep] = useState<Step>(company ? (allowMultipleServices ? 'services' : 'master') : 'company')
  const [selectedMasterId, setSelectedMasterId] = useState('')
  const [selectedDate, setSelectedDate] = useState('')
  const [selectedSlot, setSelectedSlot] = useState('')
  const [staffMode, setStaffMode] = useState(false)
  // Q7 (`SPEC_CYCLE6_BOOKING_FIXES.md` §0.1) — "show the whole day", staff-only, §108.3/FE-4.
  const [showExtendedHours, setShowExtendedHours] = useState(false)

  // Review finding §1 — "recording a client" vs "booking for myself" is an EXPLICIT intent, never
  // derived from `staffMode` (which only reports whether the server granted this caller schedule
  // freedom, §131). The default is set by the entry point: no `company` prop means the caller came
  // from "Мои записи → Записать клиента" (MyBookingsPage), so the intent defaults to recording a
  // client; a `company` prop means the public company page or the embed widget, so the intent
  // defaults to booking for oneself, exactly like the pre-cycle-10 flow. Staff who opened the
  // public page of a company they work at can still flip this explicitly (see the toggle on the
  // info step below) — but the default never assumes it for them.
  const [bookForClient, setBookForClient] = useState(!company)

  const [guestName, setGuestName] = useState('')
  const [guestPhone, setGuestPhone] = useState('')
  const [guestEmail, setGuestEmail] = useState('')
  const [notes, setNotes] = useState('')
  const [captchaToken, setCaptchaToken] = useState('')
  const [bookedForOther, setBookedForOther] = useState(false)

  // API_CONTRACT_CYCLE5.md §46.3 — informational ст. 18 notice; §41.2 — guardian-confirmation text.
  // §108.3 — neither applies to a staff booking (GuardianConfirmation is a self-booking concept).
  const { data: bookingNotice } = useLegalText('BookingNotice')
  const { data: guardianText } = useLegalText('GuardianConfirmation')
  const bookingNoticeSections = bookingNotice ? splitLegalSections(bookingNotice.contentHtml) : []
  const bookingNoticeShort = findSection(bookingNoticeSections, 'Короткая строка')
  const bookingNoticeFull = findSection(bookingNoticeSections, 'Полный текст')
  const guardianSections = guardianText ? splitLegalSections(guardianText.contentHtml) : []
  const guardianRevealText = findSection(guardianSections, 'Текст, который появляется после отметки')

  // ── Company step (only rendered when `company` prop is absent) ──────────────────────────────
  const { data: companies, isLoading: companiesLoading } = useQuery({
    queryKey: ['my-companies-all'],
    queryFn: async () => {
      const [owned, member] = await Promise.all([companiesApi.getMy(), companiesApi.getMemberOf()])
      const all = [...owned, ...member]
      return Array.from(new Map(all.map((c) => [c.id, c])).values())
    },
    enabled: !company,
  })

  // Auto-select when there's exactly one company to pick from (mirrors the old ManualBookingModal).
  useEffect(() => {
    if (!company && companies?.length === 1 && !selectedCompany) {
      setSelectedCompany(companies[0])
      setStep(allowMultipleServices ? 'services' : 'master')
    }
  }, [company, companies, selectedCompany, allowMultipleServices])

  // ── Services step ────────────────────────────────────────────────────────────────────────────
  // Fixed-primary mode (`service` prop set): `service` + whatever's been added.
  const allServicesFixed = service ? [service, ...extraServices] : []
  const allServices = service ? allServicesFixed : pickedServices
  const totalDurationMinutes = allServices.reduce((sum, s) => sum + s.durationMinutes, 0)
  const totalPrice = allServices.reduce((sum, s) => sum + s.price, 0)
  const primaryService = service ?? pickedServices[0] ?? null
  const extraServiceIds = allowMultipleServices
    ? service
      ? extraServices.map((s) => s.id)
      : pickedServices.slice(1).map((s) => s.id)
    : undefined

  const { data: companyServices, isLoading: companyServicesLoading } = useQuery({
    queryKey: ['services', effectiveCompany?.id],
    queryFn: () => servicesApi.getByCompany(effectiveCompany!.id),
    enabled: allowMultipleServices && !!effectiveCompany,
  })
  const addableServices = (companyServices ?? []).filter((s) => !allServicesFixed.some((picked) => picked.id === s.id))

  const addService = (s: Service) => {
    if (allServicesFixed.length >= MAX_SERVICES) {
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
  const togglePickedService = (s: Service) => {
    setServicesLimitMessage('')
    setPickedServices((prev) => {
      const exists = prev.some((p) => p.id === s.id)
      if (exists) return prev.filter((p) => p.id !== s.id)
      if (prev.length >= MAX_SERVICES) {
        setServicesLimitMessage(`За один визит можно выбрать не больше ${MAX_SERVICES} услуг`)
        return prev
      }
      return [...prev, s]
    })
  }

  // ── Masters step ─────────────────────────────────────────────────────────────────────────────
  // includeHidden is always REQUESTED (§103.5/§124) — the server only honors it for staff of this
  // company or SuperAdmin, and silently ignores it for everyone else. `providesServices: false`
  // therefore only ever shows up when it was actually honored, and is what we label "hidden" by.
  const { data: masters, isLoading: mastersLoading } = useQuery({
    queryKey: ['company-masters', effectiveCompany?.id, primaryService?.id],
    queryFn: () => companiesApi.getMasters(effectiveCompany!.id, primaryService!.id, true),
    enabled: !!effectiveCompany && !!primaryService,
  })

  // US-64: exactly one active master → auto-select and skip the step. Keyed on `step` so it fires
  // even if `masters` finished loading while the user was still on an earlier step.
  useEffect(() => {
    if (step === 'master' && masters && masters.length === 1 && !selectedMasterId) {
      setSelectedMasterId(masters[0].userId)
      setStep('date')
    }
  }, [step, masters, selectedMasterId])

  const selectedMaster = masters?.find((m) => m.userId === selectedMasterId)
  const showMasterStep = !!masters && masters.length > 1
  const noMastersAvailable = !!masters && masters.length === 0

  // ── Date / slot ──────────────────────────────────────────────────────────────────────────────
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
    queryKey: [
      'slots',
      effectiveCompany?.id,
      selectedMasterId,
      primaryService?.id,
      extraServiceIds,
      selectedDate,
      showExtendedHours,
    ],
    queryFn: () =>
      bookingsApi.getSlots(
        effectiveCompany!.id,
        selectedMasterId,
        primaryService!.id,
        extraServiceIds,
        selectedDate,
        true, // manual: a REQUEST for staff mode — the server decides based on real membership (§121.2)
        showExtendedHours,
      ),
    enabled: !!effectiveCompany && !!selectedMasterId && !!primaryService && !!selectedDate,
    staleTime: 0,
    retry: false,
  })
  const slots = rawSlots?.filter((s) => selectedDate !== todayStr || timeToMinutes(s.start) > nowMinutes)

  // ── Submit ───────────────────────────────────────────────────────────────────────────────────
  const mutation = useMutation({
    mutationFn: () =>
      bookingsApi.create({
        companyId: effectiveCompany!.id,
        serviceId: primaryService!.id,
        serviceIds: allowMultipleServices ? allServices.map((s) => s.id) : undefined,
        masterId: selectedMasterId,
        date: selectedDate,
        startTime: selectedSlot,
        notes,
        // Review finding §1 — `bookForClient` (explicit intent), NOT `staffMode`, decides whether
        // this request carries guest fields. A self-booking authenticated caller — staff or not —
        // must send a body byte-for-byte identical to the pre-cycle-10 client flow.
        guestName: bookForClient || !isAuthenticated() ? guestName : undefined,
        guestPhone: bookForClient || !isAuthenticated() ? guestPhone : undefined,
        guestEmail: bookForClient || !isAuthenticated() ? guestEmail || undefined : undefined,
        captchaToken: !bookForClient && !isAuthenticated() ? captchaToken || undefined : undefined,
        // GuardianConfirmation is a self-booking concept only; never sent when recording a client.
        bookedForOther: !bookForClient && bookedForOther ? true : undefined,
        guardianConfirmation:
          !bookForClient && bookedForOther && guardianText
            ? { textVersion: guardianText.version, confirmed: true }
            : undefined,
      }),
    onSuccess: () => {
      // §108.4 item 14 — must survive the merge: "Мои записи" (staff list) needs to see a booking
      // made through this modal immediately. A no-op when the query doesn't exist (client flow).
      qc.invalidateQueries({ queryKey: ['master-bookings'] })
      setStep('done')
    },
  })

  // ── Steps / progress ─────────────────────────────────────────────────────────────────────────
  const baseSteps: Step[] = showMasterStep ? ['master', 'date', 'slot', 'info'] : ['date', 'slot', 'info']
  const withServices: Step[] = allowMultipleServices ? ['services', ...baseSteps] : baseSteps
  // Review finding — when there's exactly one company to pick from, the 'company' step is
  // auto-skipped (see the effect above) and `selectedCompany` is set without ever visiting it; the
  // progress bar must not count a step nobody sees. Only prepend 'company' when it will actually be
  // rendered: no `company` prop AND more than one company to choose from (or still loading, since
  // we don't yet know if it'll be skipped).
  const companyStepRendered = !company && (companiesLoading || (companies?.length ?? 0) !== 1)
  const progressSteps: Step[] = companyStepRendered ? ['company', ...withServices] : withServices
  const currentIdx = progressSteps.indexOf(step)

  const formattedSelectedDate = selectedDate && formatDateLabel(selectedDate)

  const dismiss = useOverlayDismiss(onClose)

  const servicesContinueDisabled = service ? false : pickedServices.length === 0

  return (
    <div className="fixed inset-0 bg-ink/45 backdrop-blur-sm z-50 flex items-center justify-center p-5" {...dismiss}>
      <div className="bg-cream rounded-[26px] shadow-modal w-full max-w-[440px] max-h-[88vh] overflow-y-auto">
        {/* Header */}
        <div className="p-6 pb-[22px] border-b border-line">
          <div className="flex items-center justify-between">
            <div>
              <h2 className="font-serif text-[19px] font-medium text-ink mb-0.5">
                {bookForClient ? 'Записать клиента' : 'Запись на услугу'}
              </h2>
              {allServices.length > 0 && (
                <p className="text-[13px] text-ink-soft">
                  {allServices.map((s) => s.name).join(', ')} · {totalDurationMinutes} мин ·{' '}
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
          {/* ── Step: Company (only when `company` prop is absent) ── */}
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
                        setStep(allowMultipleServices ? 'services' : 'master')
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

          {/* ── Step: Services (US-67) ── */}
          {step === 'services' && (
            <div>
              {!company && companies && companies.length > 1 && (
                <BackLink onClick={() => setStep('company')}>{effectiveCompany?.name}</BackLink>
              )}
              <h3 className="text-[14.5px] font-semibold text-[#4A4038] mb-4">
                {service ? 'Услуги за визит' : 'Выберите услуги'}
              </h3>

              {service ? (
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
              ) : companyServicesLoading ? (
                <div className="flex flex-col gap-2.5 mb-4">
                  {[1, 2, 3].map((i) => (
                    <div key={i} className="h-14 bg-cream-deep rounded-2xl animate-pulse" />
                  ))}
                </div>
              ) : companyServices && companyServices.length > 0 ? (
                <div className="flex flex-col gap-2.5 mb-4">
                  {companyServices.map((s) => {
                    const checked = pickedServices.some((p) => p.id === s.id)
                    return (
                      <button
                        key={s.id}
                        onClick={() => togglePickedService(s)}
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
              ) : (
                <p className="text-center text-muted py-8">Нет доступных услуг</p>
              )}

              {allServices.length > 0 && (
                <div className="flex items-center justify-between text-[13.5px] font-semibold text-ink bg-cream-deep rounded-xl px-3.5 py-2.5 mb-4">
                  <span>Итого</span>
                  <span>
                    {totalDurationMinutes} мин · {totalPrice.toLocaleString('ru-RU')} ₽
                  </span>
                </div>
              )}

              {servicesLimitMessage && (
                <p className="text-sm text-danger text-center mb-3">{servicesLimitMessage}</p>
              )}

              {service && addableServices.length > 0 && (
                <>
                  <h4 className="text-[13px] font-semibold text-ink-soft mb-2">Добавить услугу</h4>
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
                </>
              )}

              <Button
                className="w-full mt-3"
                disabled={servicesContinueDisabled}
                onClick={() => setStep('master')}
              >
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
                          onClick={() => {
                            setSelectedMasterId(m.userId)
                            setStep('date')
                          }}
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
                            {/* §103.5 — only ever true when includeHidden was actually honored (staff). */}
                            {!m.providesServices && (
                              <p className="text-[11px] text-gold-dark mt-0.5">Не виден клиентам</p>
                            )}
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
                companyId={effectiveCompany!.id}
                masterId={selectedMasterId}
                serviceId={primaryService!.id}
                extraServiceIds={extraServiceIds}
                selectedDate={selectedDate}
                onSelectDate={(date) => {
                  setSelectedDate(date)
                  setStep('slot')
                }}
                manual
                extendedHours={showExtendedHours}
                onStaffModeChange={setStaffMode}
              />
            </div>
          )}

          {/* ── Step: Slot ── */}
          {step === 'slot' && (
            <div>
              <BackLink onClick={() => setStep('date')}>Изменить дату</BackLink>
              <div className="flex items-center justify-between mb-4">
                <h3 className="text-[14.5px] font-semibold text-[#4A4038]">Выберите время</h3>
                {/* §108.3/FE-4 — staff-only, drives extendedHours (§121.1). */}
                {staffMode && (
                  <button
                    type="button"
                    onClick={() => setShowExtendedHours((v) => !v)}
                    className="text-[12.5px] font-medium text-gold-dark hover:underline"
                  >
                    {showExtendedHours ? 'Скрыть остальные часы' : 'Показать остальные часы'}
                  </button>
                )}
              </div>
              {staffMode && showExtendedHours && (
                <p className="text-[12px] text-ink-soft -mt-2 mb-3">
                  Мастер в это время не работает — запись вне графика.
                </p>
              )}
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
                // the master doesn't do — show the server's explicit reason instead of a bare empty state.
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

              {/* Review finding §1 — staff who opened the PUBLIC page/widget of a company they work
                  at may still want to record a walk-in client instead of booking for themselves;
                  give them an explicit switch, defaulting to "себя" (§108/staff-self regression). A
                  caller with no `company` prop came from "Записать клиента" specifically — there is
                  no "себя" alternative to offer there. */}
              {company && staffMode && (
                <div className="flex rounded-xl bg-cream-deep p-1 -mt-1" role="group" aria-label="Кого записываем">
                  <button
                    type="button"
                    aria-pressed={!bookForClient}
                    onClick={() => setBookForClient(false)}
                    className={`flex-1 rounded-lg py-2 text-[12.5px] font-medium transition-colors ${
                      !bookForClient ? 'bg-white text-ink shadow-sm' : 'text-ink-soft'
                    }`}
                  >
                    Записываюсь сам
                  </button>
                  <button
                    type="button"
                    aria-pressed={bookForClient}
                    onClick={() => setBookForClient(true)}
                    className={`flex-1 rounded-lg py-2 text-[12.5px] font-medium transition-colors ${
                      bookForClient ? 'bg-white text-ink shadow-sm' : 'text-ink-soft'
                    }`}
                  >
                    Записать клиента
                  </button>
                </div>
              )}

              <div className="bg-cream-deep rounded-2xl p-4 text-[13.5px] text-ink">
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
                  {formattedSelectedDate} · {selectedSlot.slice(0, 5)}
                </div>
              </div>

              {/* §108.3 — staff always fills the client's contact fields; otherwise only a guest
                  (not-yet-authenticated visitor) does. */}
              {(bookForClient || !isAuthenticated()) && (
                <>
                  <Input
                    label={bookForClient ? 'Имя клиента *' : 'Ваше имя *'}
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
                    label={bookForClient ? 'Email (необязательно)' : 'Email'}
                    type="email"
                    placeholder={bookForClient ? 'client@email.com' : 'your@email.com'}
                    value={guestEmail}
                    onChange={(e) => setGuestEmail(e.target.value)}
                  />
                </>
              )}

              <div className="flex flex-col gap-1.5">
                <label className="text-[13px] font-medium text-[#4A4038]">
                  {bookForClient ? 'Комментарий' : 'Комментарий (необязательно)'}
                </label>
                <textarea
                  className="rounded-xl border border-line px-4 py-3 text-sm outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep resize-none bg-white text-ink"
                  rows={3}
                  placeholder="Пожелания или вопросы..."
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                />
              </div>

              {!bookForClient && !isAuthenticated() && smartCaptchaEnabled && (
                <div className="flex flex-col gap-1">
                  <SmartCaptcha onToken={setCaptchaToken} />
                  <p className="text-xs text-muted">Подтвердите, что вы не робот (Yandex SmartCaptcha)</p>
                </div>
              )}

              {/* GuardianConfirmation is a self-booking concept; not shown when recording a client. */}
              {!bookForClient && (
                <>
                  <label className="flex items-start gap-2.5 cursor-pointer">
                    <input
                      type="checkbox"
                      className="w-4 h-4 mt-0.5 rounded accent-gold"
                      checked={bookedForOther}
                      onChange={(e) => setBookedForOther(e.target.checked)}
                    />
                    <span className="text-[13px] text-ink-soft leading-snug">Я записываю другого человека</span>
                  </label>
                  {bookedForOther && guardianRevealText && (
                    <div
                      className="legal-content -mt-2 rounded-xl bg-cream-deep px-3.5 py-2.5 text-xs text-ink-soft leading-[1.6] [&_p]:mb-1.5 last:[&_p]:mb-0 [&_a]:text-gold [&_a]:hover:text-gold-dark"
                      dangerouslySetInnerHTML={{ __html: guardianRevealText.html }}
                    />
                  )}
                </>
              )}

              <Button
                size="lg"
                loading={mutation.isPending}
                onClick={() => mutation.mutate()}
                disabled={
                  ((bookForClient || !isAuthenticated()) && (!guestName || !isRussianPhone(guestPhone))) ||
                  (!bookForClient && !isAuthenticated() && smartCaptchaEnabled && !captchaToken) ||
                  (!bookForClient && bookedForOther && !guardianText)
                }
                className="w-full"
              >
                {bookForClient ? 'Записать клиента' : 'Подтвердить запись'}
              </Button>

              {!bookForClient && !isAuthenticated() && (
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

              {/* API_CONTRACT_CYCLE5.md §46.3 — ст. 18 notice; doesn't apply when recording a client. */}
              {!bookForClient && (
                <div className="text-center text-xs text-muted -mt-1.5">
                  {bookingNoticeShort || bookingNoticeFull ? (
                    <>
                      <div
                        className="legal-content [&_a]:text-gold [&_a]:hover:text-gold-dark [&_p]:mb-0"
                        dangerouslySetInnerHTML={{ __html: (bookingNoticeShort ?? bookingNoticeFull)!.html }}
                      />
                      {bookingNoticeFull && bookingNoticeShort && (
                        <details className="mt-1">
                          <summary className="cursor-pointer text-gold hover:text-gold-dark inline">Подробнее</summary>
                          <div
                            className="legal-content text-left mt-2 [&_p]:mb-2 [&_a]:text-gold [&_a]:hover:text-gold-dark"
                            dangerouslySetInnerHTML={{ __html: bookingNoticeFull.html }}
                          />
                        </details>
                      )}
                    </>
                  ) : (
                    <p>
                      Оставляя номер телефона, вы получите сервисные сообщения о записи в WhatsApp от салона. Подробнее
                      — в{' '}
                      <Link to="/privacy" target="_blank" className="text-gold hover:text-gold-dark">
                        политике обработки персональных данных
                      </Link>
                      .
                    </p>
                  )}
                </div>
              )}

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
              <h3 className="font-serif text-xl font-medium text-ink mb-2">
                {bookForClient ? 'Клиент записан!' : 'Запись подтверждена!'}
              </h3>
              <p className="text-sm text-ink-soft mb-6">
                {bookForClient && guestName
                  ? `${guestName} · ${formattedSelectedDate} в ${selectedSlot.slice(0, 5)}`
                  : `Ждём вас ${formattedSelectedDate} в ${selectedSlot.slice(0, 5)}`}
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
