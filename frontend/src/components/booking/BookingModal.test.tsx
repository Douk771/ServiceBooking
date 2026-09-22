import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { format } from 'date-fns'
import { BookingModal } from './BookingModal'
import { DAY_FULL_LABEL } from './BookingCalendar'
import { useAuthStore } from '../../store/authStore'
import type { Company, Service } from '../../types'
import type { MasterPublicDto } from '../../api/companies'
import type { AvailabilityResponse } from '../../api/bookings'

const getMasters = vi.fn()
const getAvailability = vi.fn()
const getSlots = vi.fn()
const createBooking = vi.fn()
const getByCompany = vi.fn()
const getText = vi.fn()

vi.mock('../../api/companies', () => ({
  companiesApi: {
    getMasters: (...args: unknown[]) => getMasters(...args),
  },
}))

vi.mock('../../api/services', () => ({
  servicesApi: {
    getByCompany: (...args: unknown[]) => getByCompany(...args),
  },
}))

vi.mock('../../api/bookings', async () => {
  const actual = await vi.importActual<typeof import('../../api/bookings')>('../../api/bookings')
  return {
    ...actual,
    bookingsApi: {
      getAvailability: (...args: unknown[]) => getAvailability(...args),
      getSlots: (...args: unknown[]) => getSlots(...args),
      create: (...args: unknown[]) => createBooking(...args),
    },
  }
})

vi.mock('../../api/legal', () => ({
  legalApi: { getText: (...args: unknown[]) => getText(...args) },
}))

vi.mock('./SmartCaptcha', () => ({ smartCaptchaEnabled: false, SmartCaptcha: () => null }))

const BOOKING_NOTICE_HTML = `<p>Meta.</p>
<h2>Короткая строка (видна всегда)</h2>
<p>Записываясь, вы передаёте своё имя и номер телефона компании.</p>
<h2>Полный текст (раскрывается по ссылке «Подробнее»)</h2>
<p>Полное описание того, кто и зачем обрабатывает данные.</p>`

const GUARDIAN_HTML = `<p>Meta.</p>
<h2>Текст в форме записи</h2>
<p>Я записываю другого человека</p>
<h2>Текст, который появляется после отметки</h2>
<p>Вы подтверждаете, что вправе действовать в интересах этого человека.</p>`

const company: Company = {
  id: 'co1',
  name: 'Барбершоп «Гвоздь»',
  slug: 'gvozd',
  allowSelfBooking: true,
}

const service: Service = {
  id: 'svc1',
  companyId: 'co1',
  name: 'Стрижка',
  durationMinutes: 30,
  price: 1500,
}

function master(overrides: Partial<MasterPublicDto> = {}): MasterPublicDto {
  return { userId: 'm1', firstName: 'Иван', lastName: 'Петров', ...overrides }
}

function monthKey(offsetMonths = 0) {
  const d = new Date()
  d.setDate(1)
  d.setMonth(d.getMonth() + offsetMonths)
  return format(d, 'yyyy-MM')
}

// The suite pins "now" (see beforeEach below) to a fixed mid-month instant, so results never
// depend on the wall-clock day/time the test run happens to start at — see review finding re:
// futureDateInCurrentMonth() previously colliding with "today" (and the calendar's same-day
// "already passed" disabling) near month-end. With "now" fixed mid-month, today + 3 days always
// lands safely within the same month, in the future, and never equals "today".
const FIXED_NOW = new Date('2026-09-15T09:00:00')

function futureDateInCurrentMonth(): string {
  const today = new Date()
  const lastDayOfMonth = new Date(today.getFullYear(), today.getMonth() + 1, 0).getDate()
  const day = Math.min(today.getDate() + 3, lastDayOfMonth)
  return format(new Date(today.getFullYear(), today.getMonth(), day), 'yyyy-MM-dd')
}

function availabilityFor(dates: Record<string, AvailabilityResponse['days'][number]['status']>): AvailabilityResponse {
  const days = Object.entries(dates).map(([date, status]) => ({
    date,
    status,
    lastFreeSlotStart: status === 'Available' ? '18:00:00' : null,
  }))
  return {
    from: `${monthKey()}-01`,
    to: `${monthKey()}-28`,
    totalDurationMinutes: 30,
    stepMinutes: 30,
    horizonDays: 90,
    horizonLastDate: '2099-01-01',
    days,
  }
}

function renderModal(allowMultipleServices = false) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <BookingModal
          service={service}
          company={company}
          onClose={() => {}}
          allowMultipleServices={allowMultipleServices}
        />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

// US-65/API_CONTRACT_CYCLE5.md tests below reach the "info" step via a single mocked master (so the
// US-64 master-step auto-skip kicks in) and a single available day in the current month, picked
// through the real BookingCalendar rather than the flat date list the old UI used.
async function reachInfoStep(user: ReturnType<typeof userEvent.setup>, dateStr: string) {
  const dateLabel = format(new Date(`${dateStr}T00:00:00`), 'd MMMM', { locale: (await import('date-fns/locale')).ru })
  await user.click(await screen.findByLabelText(dateLabel))
  await user.click(await screen.findByRole('button', { name: '10:00' }))
}

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true, toFake: ['Date'] })
  vi.setSystemTime(FIXED_NOW)

  getMasters.mockReset()
  getAvailability.mockReset().mockResolvedValue(availabilityFor({}))
  getSlots.mockReset().mockResolvedValue([])
  createBooking.mockReset().mockResolvedValue({})
  getByCompany.mockReset().mockResolvedValue([])
  getText.mockReset()
  useAuthStore.setState({ user: null, token: null })

  getMasters.mockResolvedValue([master()])
  getText.mockImplementation((key: string) => {
    if (key === 'BookingNotice')
      return Promise.resolve({ key, version: '2026-09-21', isDraft: true, contentHtml: BOOKING_NOTICE_HTML })
    if (key === 'GuardianConfirmation')
      return Promise.resolve({ key, version: '2026-09-21', isDraft: true, contentHtml: GUARDIAN_HTML })
    return Promise.reject(new Error('unknown key'))
  })
})

afterEach(() => {
  vi.useRealTimers()
})

describe('BookingModal — US-64 single/zero master', () => {
  it('skips the master step and shows a 3-step progress bar when exactly one master provides the service', async () => {
    getMasters.mockResolvedValueOnce([master()])
    renderModal()

    // The master-choosing step never renders...
    await waitFor(() => expect(screen.queryByText('Выберите мастера')).not.toBeInTheDocument())
    // ...and the date step is shown right away instead.
    expect(await screen.findByText('Выберите дату')).toBeInTheDocument()

    // Progress bar renders exactly 3 segments (date, slot, info) — not the usual 4.
    const segments = document.querySelectorAll('.flex.gap-1\\.5.mt-4 > div')
    expect(segments.length).toBe(3)
  })

  it("shows the sole master's name in the confirmation summary — a client must not book \"into the void\"", async () => {
    getMasters.mockResolvedValueOnce([master({ firstName: 'Анна', lastName: 'Смирнова' })])
    // A future (not "today") date, so the modal's same-day "already passed" slot filter can't
    // interfere with this test regardless of what time of day the suite happens to run.
    const dateStr = futureDateInCurrentMonth()
    getAvailability.mockResolvedValue(availabilityFor({ [dateStr]: 'Available' }))
    getSlots.mockResolvedValue([{ start: '10:00:00', end: '10:30:00' }])

    renderModal()
    await screen.findByText('Выберите дату')

    const dateLabel = format(new Date(`${dateStr}T00:00:00`), 'd MMMM', { locale: (await import('date-fns/locale')).ru })
    const dayButton = (await screen.findByLabelText(dateLabel)) as HTMLButtonElement
    dayButton.click()

    const slotButton = await screen.findByRole('button', { name: '10:00' })
    slotButton.click()

    expect(await screen.findByText('Анна Смирнова')).toBeInTheDocument()
  })

  it('shows an understandable "cannot book now" message when zero active masters provide the service', async () => {
    getMasters.mockResolvedValueOnce([])
    renderModal()

    expect(await screen.findByText('Сейчас записаться нельзя')).toBeInTheDocument()
    expect(screen.queryByText('Выберите дату')).not.toBeInTheDocument()
  })
})

describe('BookingModal — US-65 calendar day states', () => {
  it('does not allow clicking a day off', async () => {
    getMasters.mockResolvedValueOnce([master(), master({ userId: 'm2' })])
    const dateStr = futureDateInCurrentMonth()
    getAvailability.mockResolvedValue(availabilityFor({ [dateStr]: 'DayOff' }))
    renderModal()

    const masterButtons = await screen.findAllByRole('button', { name: /Иван Петров/i })
    masterButtons[0].click()

    await waitFor(() => expect(screen.getByText('Выберите дату')).toBeInTheDocument())
    const dayOffCell = await screen.findByText('выходной')
    const button = dayOffCell.closest('button') as HTMLButtonElement
    expect(button).toBeDisabled()
    expect(button.getAttribute('aria-disabled')).toBe('true')
  })

  it('marks a fully booked working day as not clickable and labels it', async () => {
    getMasters.mockResolvedValueOnce([master(), master({ userId: 'm2' })])
    const dateStr = futureDateInCurrentMonth()
    getAvailability.mockResolvedValue(availabilityFor({ [dateStr]: 'FullyBooked' }))
    renderModal()

    const masterButtons = await screen.findAllByRole('button', { name: /Иван Петров/i })
    masterButtons[0].click()

    await waitFor(() => expect(screen.getByText('Выберите дату')).toBeInTheDocument())
    // The legend repeats the same DAY_FULL_LABEL text, so match on the calendar day button's
    // accessible name (which embeds the label) rather than on text content alone.
    const button = await screen.findByRole('button', { name: new RegExp(DAY_FULL_LABEL) })
    expect(button).toBeDisabled()
    expect(button.getAttribute('aria-disabled')).toBe('true')
  })
})

// ── US-67: several services in one visit ─────────────────────────────────────

const secondService: Service = {
  id: 'svc2',
  companyId: 'co1',
  name: 'Окрашивание',
  durationMinutes: 60,
  price: 2500,
}
const thirdService: Service = {
  id: 'svc3',
  companyId: 'co1',
  name: 'Укладка',
  durationMinutes: 20,
  price: 900,
}

describe('BookingModal — US-67 multiple services per visit', () => {
  it('shows the summed duration and price once a second and third service are added', async () => {
    getByCompany.mockResolvedValue([service, secondService, thirdService])
    getMasters.mockResolvedValue([master()])
    renderModal(true)

    await screen.findByText('Услуги за визит')
    ;(await screen.findByText('Окрашивание')).click()
    ;(await screen.findByText('Укладка')).click()

    // 30 + 60 + 20 = 110 minutes, 1500 + 2500 + 900 = 4900 ₽
    expect(await screen.findByText('110 мин · 4 900 ₽')).toBeInTheDocument()
  })

  it('refuses to add a 6th service with an explanatory message', async () => {
    const extraServices = Array.from({ length: 5 }, (_, i) => ({
      id: `extra-${i}`,
      companyId: 'co1',
      name: `Доп. услуга ${i}`,
      durationMinutes: 10,
      price: 100,
    }))
    getByCompany.mockResolvedValue([service, ...extraServices])
    getMasters.mockResolvedValue([master()])
    renderModal(true)

    await screen.findByText('Услуги за визит')
    for (const s of extraServices) {
      const el = await screen.findByText(s.name)
      el.click()
    }

    expect(await screen.findByText('За один визит можно выбрать не больше 5 услуг')).toBeInTheDocument()
  })

  it('shows the server\'s "master does not perform this service" text instead of an empty slot list', async () => {
    getByCompany.mockResolvedValue([service, secondService])
    getMasters.mockResolvedValue([master()])
    const dateStr = futureDateInCurrentMonth()
    getAvailability.mockResolvedValue(availabilityFor({ [dateStr]: 'Available' }))
    getSlots.mockRejectedValue({
      response: { status: 400, data: 'Мастер не оказывает услугу: Окрашивание' },
      isAxiosError: true,
    })
    renderModal(true)

    await screen.findByText('Услуги за визит')
    ;(await screen.findByText('Окрашивание')).click()
    ;(await screen.findByText('Продолжить')).click()

    await screen.findByText('Выберите дату')
    const dateLabel = format(new Date(`${dateStr}T00:00:00`), 'd MMMM', { locale: (await import('date-fns/locale')).ru })
    const dayButton = (await screen.findByLabelText(dateLabel)) as HTMLButtonElement
    dayButton.click()

    expect(await screen.findByText('Мастер не оказывает услугу: Окрашивание')).toBeInTheDocument()
  })
})

// ── cycle 5: booking notice text, guardian/"записываю другого человека" confirmation ─────────────

describe('BookingModal — cycle 5 legal text additions', () => {
  it('does not send bookedForOther/guardianConfirmation when the box is left unchecked (today\'s behaviour)', async () => {
    const user = userEvent.setup()
    createBooking.mockResolvedValueOnce({ id: 'b1' })
    const dateStr = futureDateInCurrentMonth()
    getAvailability.mockResolvedValue(availabilityFor({ [dateStr]: 'Available' }))
    getSlots.mockResolvedValue([{ start: '10:00:00', end: '11:00:00' }])
    renderModal()

    await screen.findByText('Выберите дату')
    await reachInfoStep(user, dateStr)
    await user.type(screen.getByLabelText('Ваше имя *'), 'Иван Иванов')
    await user.type(screen.getByLabelText('Телефон *'), '+79991234567')
    await user.click(screen.getByRole('button', { name: 'Подтвердить запись' }))

    await waitFor(() => expect(createBooking).toHaveBeenCalled())
    const payload = createBooking.mock.calls[0][0]
    expect(payload.bookedForOther).toBeUndefined()
    expect(payload.guardianConfirmation).toBeUndefined()
  })

  it('checking "записываю другого человека" reveals the guardian text and requires it before submit', async () => {
    const user = userEvent.setup()
    const dateStr = futureDateInCurrentMonth()
    getAvailability.mockResolvedValue(availabilityFor({ [dateStr]: 'Available' }))
    getSlots.mockResolvedValue([{ start: '10:00:00', end: '11:00:00' }])
    renderModal()

    await screen.findByText('Выберите дату')
    await reachInfoStep(user, dateStr)
    await user.type(screen.getByLabelText('Ваше имя *'), 'Иван Иванов')
    await user.type(screen.getByLabelText('Телефон *'), '+79991234567')

    await user.click(screen.getByLabelText('Я записываю другого человека'))

    expect(await screen.findByText(/вправе действовать в интересах этого человека/)).toBeInTheDocument()
  })

  it('submitting with the box checked sends bookedForOther and the guardian text version', async () => {
    const user = userEvent.setup()
    createBooking.mockResolvedValueOnce({ id: 'b1' })
    const dateStr = futureDateInCurrentMonth()
    getAvailability.mockResolvedValue(availabilityFor({ [dateStr]: 'Available' }))
    getSlots.mockResolvedValue([{ start: '10:00:00', end: '11:00:00' }])
    renderModal()

    await screen.findByText('Выберите дату')
    await reachInfoStep(user, dateStr)
    await user.type(screen.getByLabelText('Ваше имя *'), 'Иван Иванов')
    await user.type(screen.getByLabelText('Телефон *'), '+79991234567')
    await user.click(screen.getByLabelText('Я записываю другого человека'))
    await screen.findByText(/вправе действовать в интересах этого человека/)
    await user.click(screen.getByRole('button', { name: 'Подтвердить запись' }))

    await waitFor(() =>
      expect(createBooking).toHaveBeenCalledWith(
        expect.objectContaining({
          bookedForOther: true,
          guardianConfirmation: { textVersion: '2026-09-21', confirmed: true },
        }),
      ),
    )
  })

  it('renders the short booking-notice line always, with the full text tucked behind a "Подробнее" disclosure', async () => {
    const user = userEvent.setup()
    const dateStr = futureDateInCurrentMonth()
    getAvailability.mockResolvedValue(availabilityFor({ [dateStr]: 'Available' }))
    getSlots.mockResolvedValue([{ start: '10:00:00', end: '11:00:00' }])
    renderModal()

    await screen.findByText('Выберите дату')
    await reachInfoStep(user, dateStr)

    expect(await screen.findByText(/Записываясь, вы передаёте своё имя/)).toBeInTheDocument()
    // jsdom doesn't hide closed <details> content from text queries the way a real browser does, so
    // the meaningful assertion here is the structural one: the full text lives inside the
    // <details>/<summary> disclosure, collapsed by default (no `open` attribute).
    const details = screen.getByText('Подробнее').closest('details')
    expect(details).not.toBeNull()
    expect(details).not.toHaveAttribute('open')
    expect(details).toHaveTextContent(/Полное описание того, кто и зачем/)

    await user.click(screen.getByText('Подробнее'))
    expect(details).toHaveAttribute('open')
  })

  it('falls back to a static notice if the legal text fails to load, instead of showing nothing', async () => {
    getText.mockRejectedValue(new Error('network'))
    const user = userEvent.setup()
    const dateStr = futureDateInCurrentMonth()
    getAvailability.mockResolvedValue(availabilityFor({ [dateStr]: 'Available' }))
    getSlots.mockResolvedValue([{ start: '10:00:00', end: '11:00:00' }])
    renderModal()

    await screen.findByText('Выберите дату')
    await reachInfoStep(user, dateStr)

    expect(await screen.findByText(/Оставляя номер телефона, вы получите сервисные сообщения/)).toBeInTheDocument()
  })
})
