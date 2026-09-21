import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BookingModal } from './BookingModal'
import { useAuthStore } from '../../store/authStore'
import type { Company, Service } from '../../types'

const getMasters = vi.fn()
const getSlots = vi.fn()
const create = vi.fn()
const getText = vi.fn()

vi.mock('../../api/companies', () => ({
  companiesApi: { getMasters: (...args: unknown[]) => getMasters(...args) },
}))
vi.mock('../../api/bookings', () => ({
  bookingsApi: {
    getSlots: (...args: unknown[]) => getSlots(...args),
    create: (...args: unknown[]) => create(...args),
  },
}))
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
  id: 'c1',
  name: 'Салон Роз',
  slug: 'rozy',
  allowSelfBooking: true,
}

const service: Service = {
  id: 's1',
  companyId: 'c1',
  name: 'Стрижка',
  durationMinutes: 60,
  price: 1500,
}

function renderModal() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <BookingModal service={service} company={company} onClose={() => {}} />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

async function reachInfoStep(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: /Мастер Мастеров/ }))
  await user.click(await screen.findByRole('button', { name: 'Завтра' }))
  await user.click(await screen.findByRole('button', { name: '10:00' }))
}

beforeEach(() => {
  getMasters.mockReset()
  getSlots.mockReset()
  create.mockReset()
  getText.mockReset()
  useAuthStore.setState({ user: null, token: null })

  getMasters.mockResolvedValue([{ userId: 'm1', firstName: 'Мастер', lastName: 'Мастеров' }])
  getSlots.mockResolvedValue([{ start: '10:00:00', end: '11:00:00' }])
  getText.mockImplementation((key: string) => {
    if (key === 'BookingNotice') return Promise.resolve({ key, version: '2026-09-21', isDraft: true, contentHtml: BOOKING_NOTICE_HTML })
    if (key === 'GuardianConfirmation')
      return Promise.resolve({ key, version: '2026-09-21', isDraft: true, contentHtml: GUARDIAN_HTML })
    return Promise.reject(new Error('unknown key'))
  })
})

describe('BookingModal — cycle 5 additions', () => {
  it('does not send bookedForOther/guardianConfirmation when the box is left unchecked (today\'s behaviour)', async () => {
    const user = userEvent.setup()
    create.mockResolvedValueOnce({ id: 'b1' })
    renderModal()

    await reachInfoStep(user)
    await user.type(screen.getByLabelText('Ваше имя *'), 'Иван Иванов')
    await user.type(screen.getByLabelText('Телефон *'), '+79991234567')
    await user.click(screen.getByRole('button', { name: 'Подтвердить запись' }))

    await waitFor(() => expect(create).toHaveBeenCalled())
    const payload = create.mock.calls[0][0]
    expect(payload.bookedForOther).toBeUndefined()
    expect(payload.guardianConfirmation).toBeUndefined()
  })

  it('checking "записываю другого человека" reveals the guardian text and requires it before submit', async () => {
    const user = userEvent.setup()
    renderModal()

    await reachInfoStep(user)
    await user.type(screen.getByLabelText('Ваше имя *'), 'Иван Иванов')
    await user.type(screen.getByLabelText('Телефон *'), '+79991234567')

    await user.click(screen.getByLabelText('Я записываю другого человека'))

    expect(await screen.findByText(/вправе действовать в интересах этого человека/)).toBeInTheDocument()
  })

  it('submitting with the box checked sends bookedForOther and the guardian text version', async () => {
    const user = userEvent.setup()
    create.mockResolvedValueOnce({ id: 'b1' })
    renderModal()

    await reachInfoStep(user)
    await user.type(screen.getByLabelText('Ваше имя *'), 'Иван Иванов')
    await user.type(screen.getByLabelText('Телефон *'), '+79991234567')
    await user.click(screen.getByLabelText('Я записываю другого человека'))
    await screen.findByText(/вправе действовать в интересах этого человека/)
    await user.click(screen.getByRole('button', { name: 'Подтвердить запись' }))

    await waitFor(() =>
      expect(create).toHaveBeenCalledWith(
        expect.objectContaining({
          bookedForOther: true,
          guardianConfirmation: { textVersion: '2026-09-21', confirmed: true },
        }),
      ),
    )
  })

  it('renders the short booking-notice line always, with the full text tucked behind a "Подробнее" disclosure', async () => {
    const user = userEvent.setup()
    renderModal()

    await reachInfoStep(user)

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
    renderModal()

    await reachInfoStep(user)

    expect(await screen.findByText(/Оставляя номер телефона, вы получите сервисные сообщения/)).toBeInTheDocument()
  })
})
