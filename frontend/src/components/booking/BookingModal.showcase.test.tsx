import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { format } from 'date-fns'
import { ru } from 'date-fns/locale'
import { BookingModal } from './BookingModal'
import { useAuthStore } from '../../store/authStore'
import { SHOWCASE_FALLBACK_TEXTS } from '../../utils/showcaseTexts'
import type { Company, Service } from '../../types'
import type { MasterPublicDto } from '../../api/companies'

// Cycle 28 (API_CONTRACT_CYCLE28.md §591–§593, §600): the notice before confirm, the closed-showcase 409 (JSON) and
// the notice on the success screen. Component-level: the API modules are mocked, nothing touches the network.

const getMasters = vi.fn()
const getAvailability = vi.fn()
const getSlots = vi.fn()
const createBooking = vi.fn()
const getText = vi.fn()

vi.mock('../../api/companies', () => ({
  companiesApi: { getMy: vi.fn(), getMemberOf: vi.fn(), getMasters: (...a: unknown[]) => getMasters(...a) },
}))
vi.mock('../../api/services', () => ({ servicesApi: { getByCompany: vi.fn().mockResolvedValue([]) } }))
vi.mock('../../api/bookings', async () => {
  const actual = await vi.importActual<typeof import('../../api/bookings')>('../../api/bookings')
  return {
    ...actual,
    bookingsApi: {
      getAvailability: (...a: unknown[]) => getAvailability(...a),
      getSlots: (...a: unknown[]) => getSlots(...a),
      create: (...a: unknown[]) => createBooking(...a),
    },
  }
})
vi.mock('../../api/legal', () => ({ legalApi: { getText: (...a: unknown[]) => getText(...a) } }))
vi.mock('./SmartCaptcha', () => ({ smartCaptchaEnabled: false, SmartCaptcha: () => null }))

const NOTICE = SHOWCASE_FALLBACK_TEXTS.ShowcaseNotice
const service: Service = { id: 'svc1', companyId: 'co1', name: 'Стрижка', durationMinutes: 30, price: 1500 }
const master: MasterPublicDto = { userId: 'm1', firstName: 'Иван', lastName: 'Петров', providesServices: true }

function makeCompany(over: Partial<Company> = {}): Company {
  return { id: 'co1', name: 'Салон «Пример»', slug: 'primer-salon', allowSelfBooking: true, ...over }
}

// One available day a few days ahead of the pinned "now" (mid-month, so it never crosses a month boundary).
const FIXED_NOW = new Date('2026-09-15T09:00:00')
const DAY = '2026-09-18'

function renderModal(company: Company) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <BookingModal service={service} company={company} onClose={() => {}} allowMultipleServices={false} />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

async function reachInfoStep(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByLabelText(format(new Date(`${DAY}T00:00:00`), 'd MMMM', { locale: ru })))
  await user.click(await screen.findByRole('button', { name: '10:00' }))
}

async function fillGuestAndConfirm(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Ваше имя *'), 'Иван Иванов')
  await user.type(screen.getByLabelText('Телефон *'), '+79991234567')
  await user.click(screen.getByRole('button', { name: 'Подтвердить запись' }))
}

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true, toFake: ['Date'] })
  vi.setSystemTime(FIXED_NOW)
  getMasters.mockReset().mockResolvedValue([master])
  getAvailability.mockReset().mockResolvedValue({
    from: '2026-09-01',
    to: '2026-09-30',
    totalDurationMinutes: 30,
    stepMinutes: 30,
    horizonDays: 90,
    horizonLastDate: '2099-01-01',
    staffMode: false,
    days: [{ date: DAY, status: 'Available', lastFreeSlotStart: '18:00:00', scheduleState: null }],
  })
  getSlots.mockReset().mockResolvedValue([{ start: '10:00:00', end: '10:30:00' }])
  createBooking.mockReset().mockResolvedValue({ id: 'b1', companyIsShowcase: true })
  // Every uiText is "not published": 404 → the screens must fall back to §600.
  getText.mockReset().mockRejectedValue({ response: { status: 404 } })
  useAuthStore.setState({ user: null, token: null })
})

afterEach(() => {
  vi.useRealTimers()
})

describe('BookingModal — showcase company (cycle 28)', () => {
  it('open showcase: the notice stands before the confirm button (fallback text while the uiText is unpublished)', async () => {
    const user = userEvent.setup()
    renderModal(makeCompany({ isShowcase: true, showcaseBookingOpen: true }))
    await reachInfoStep(user)

    const notice = await screen.findByTestId('showcase-notice')
    expect(notice).toHaveTextContent(NOTICE)
    const confirm = screen.getByRole('button', { name: 'Подтвердить запись' })
    // DOM order: notice first, confirm button after it.
    expect(notice.compareDocumentPosition(confirm) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('an ordinary company has no notice and never asks for the showcase texts', async () => {
    const user = userEvent.setup()
    renderModal(makeCompany({ isShowcase: false }))
    await reachInfoStep(user)

    await screen.findByRole('button', { name: 'Подтвердить запись' })
    expect(screen.queryByTestId('showcase-notice')).not.toBeInTheDocument()
    expect(getText).not.toHaveBeenCalledWith('ShowcaseNotice')
    expect(getText).not.toHaveBeenCalledWith('ShowcaseBookingClosed')
  })

  it('closed showcase: the UI stays open and silent before confirm (the refusal comes from the server)', async () => {
    const user = userEvent.setup()
    renderModal(makeCompany({ isShowcase: true, showcaseBookingOpen: false }))
    await reachInfoStep(user)
    await user.type(await screen.findByLabelText('Ваше имя *'), 'Иван Иванов')
    await user.type(screen.getByLabelText('Телефон *'), '+79991234567')

    expect(screen.getByRole('button', { name: 'Подтвердить запись' })).toBeEnabled()
    expect(screen.queryByTestId('showcase-notice')).not.toBeInTheDocument()
  })

  it('409 JSON ShowcaseBookingClosed → the server message, not «время занято»', async () => {
    const user = userEvent.setup()
    createBooking.mockRejectedValue({
      response: { status: 409, data: { code: 'ShowcaseBookingClosed', message: 'Запись к этому примеру не принимается.' } },
    })
    renderModal(makeCompany({ isShowcase: true, showcaseBookingOpen: false }))
    await reachInfoStep(user)
    await fillGuestAndConfirm(user)

    expect(await screen.findByRole('alert')).toHaveTextContent('Запись к этому примеру не принимается.')
    expect(screen.queryByText(/время уже занято/)).not.toBeInTheDocument()
  })

  it('409 JSON ShowcaseBookingClosed → the published uiText wins over the server message', async () => {
    const user = userEvent.setup()
    getText.mockImplementation((key: string) =>
      key === 'ShowcaseBookingClosed'
        ? Promise.resolve({ key, version: '1', isDraft: false, contentHtml: '<p>Текст из legal.json.</p>' })
        : Promise.reject({ response: { status: 404 } }),
    )
    createBooking.mockRejectedValue({
      response: { status: 409, data: { code: 'ShowcaseBookingClosed', message: 'Запасной текст сервера.' } },
    })
    renderModal(makeCompany({ isShowcase: true, showcaseBookingOpen: false }))
    await reachInfoStep(user)
    await waitFor(() => expect(getText).toHaveBeenCalledWith('ShowcaseBookingClosed'))
    await fillGuestAndConfirm(user)

    expect(await screen.findByRole('alert')).toHaveTextContent('Текст из legal.json.')
  })

  it('the usual text/plain 409 of an ordinary company still reads «время уже занято»', async () => {
    const user = userEvent.setup()
    createBooking.mockRejectedValue({ response: { status: 409, data: 'Slot is already booked' } })
    renderModal(makeCompany({ isShowcase: false }))
    await reachInfoStep(user)
    await fillGuestAndConfirm(user)

    expect(await screen.findByRole('alert')).toHaveTextContent('Это время уже занято. Выберите другой слот.')
  })

  it('success screen of a showcase booking repeats the notice (BookingDto.companyIsShowcase)', async () => {
    const user = userEvent.setup()
    renderModal(makeCompany({ isShowcase: true, showcaseBookingOpen: true }))
    await reachInfoStep(user)
    await fillGuestAndConfirm(user)

    expect(await screen.findByText('Запись подтверждена!')).toBeInTheDocument()
    expect(screen.getByTestId('showcase-notice')).toHaveTextContent(NOTICE)
  })

  it('success screen of an ordinary booking has no notice', async () => {
    const user = userEvent.setup()
    createBooking.mockResolvedValue({ id: 'b1', companyIsShowcase: false })
    renderModal(makeCompany())
    await reachInfoStep(user)
    await fillGuestAndConfirm(user)

    expect(await screen.findByText('Запись подтверждена!')).toBeInTheDocument()
    expect(screen.queryByTestId('showcase-notice')).not.toBeInTheDocument()
  })
})
