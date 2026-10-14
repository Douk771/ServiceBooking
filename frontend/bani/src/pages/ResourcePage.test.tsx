import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen, fireEvent } from '@testing-library/react'
import { ResourcePage } from './ResourcePage'
import { renderInVertical, testVertical } from '../test/baniTestVertical'
import { GUEST_STORAGE_KEY } from '../utils/guestStorage'
import type { PublicServiceDto } from '@/types/slots'

const publicServices = vi.hoisted(() => ({
  page: vi.fn(),
  availability: vi.fn(),
  starts: vi.fn(),
  quote: vi.fn(),
  createOrder: vi.fn(),
}))

const service = (over: Partial<PublicServiceDto> = {}): PublicServiceDto =>
  ({
    id: 'svc-1',
    slug: 'banya',
    name: 'Баня по-чёрному',
    description: null,
    photos: [],
    minHours: 2,
    maxHours: 4,
    bufferMinutes: null,
    priceTable: [{ label: 'Будни', priceRub: 2000 }],
    items: [],
    acceptingBookings: true,
    available: true,
    today: '2027-01-14',
    timeZoneId: 'Asia/Novokuznetsk',
    capacity: 6,
    cityName: 'Шерегеш',
    localTimeNote: 'Время местное, Шерегеш',
    company: { name: 'Щербаны', url: '/sherbany', phone: null },
    provider: null,
    standalone: { ordering: true, cancellationSummary: 'Отмена за 12 ч' },
    ...over,
  }) as unknown as PublicServiceDto

function show(s = service()) {
  publicServices.page.mockResolvedValue(s)
  publicServices.availability.mockResolvedValue({ serviceId: 'svc-1', today: '2027-01-14', days: [] })
  return renderInVertical(testVertical({ publicServices }), '/sherbany/banya', '/:slug/:resourceSlug', <ResourcePage />)
}

beforeEach(() => {
  Object.values(publicServices).forEach((f) => f.mockReset())
  sessionStorage.clear()
})

describe('ResourcePage (bani)', () => {
  it('reads the page by the company and resource slugs of the address', async () => {
    show()
    await screen.findByRole('heading', { level: 1, name: 'Баня по-чёрному' })
    expect(publicServices.page).toHaveBeenCalledWith('sherbany', 'banya')
  })

  it('shows the capacity, the local time note, the guests field and the notice under the button', async () => {
    show()
    expect(await screen.findByTestId('service-capacity')).toHaveTextContent('до 6 человек')
    expect(screen.getByTestId('local-time-note')).toHaveTextContent('Время местное, Шерегеш')
    expect(screen.getByLabelText('Сколько человек придёт, включая детей')).toHaveValue(null)
    expect(await screen.findByText('Уведомление о данных')).toBeInTheDocument()
  })

  it('does not tick the messenger box (Т42-09)', async () => {
    show()
    await screen.findByTestId('service-capacity')
    expect(screen.getByRole('checkbox')).not.toBeChecked()
  })

  it('refills an anonymous guest from the tab memory, but never the tick or the guests count', async () => {
    sessionStorage.setItem(GUEST_STORAGE_KEY, JSON.stringify({ name: 'Анна', phone: '+79001112233' }))
    show()
    await screen.findByTestId('service-capacity')
    expect(screen.getByLabelText('Имя')).toHaveValue('Анна')
    expect(screen.getByRole('checkbox')).not.toBeChecked()
    expect(screen.getByLabelText('Сколько человек придёт, включая детей')).toHaveValue(null)
  })

  it('has no guests field for a resource without capacity', async () => {
    show(service({ capacity: null }))
    await screen.findByRole('heading', { level: 1 })
    expect(screen.queryByLabelText('Сколько человек придёт, включая детей')).toBeNull()
  })

  it('says «бронь» in the form', async () => {
    show()
    await screen.findByTestId('service-capacity')
    expect(screen.getByLabelText('Бронь сеанса')).toBeInTheDocument()
    expect(screen.getByText('На этот номер придёт ссылка на бронь. Проверьте, что номер указан верно.')).toBeInTheDocument()
  })

  it('shows a not-found screen for an unknown resource', async () => {
    publicServices.page.mockRejectedValue(Object.assign(new Error('nf'), { response: { status: 404, data: '' } }))
    renderInVertical(testVertical({ publicServices }), '/sherbany/none', '/:slug/:resourceSlug', <ResourcePage />)
    expect(await screen.findByText('Услуга не найдена')).toBeInTheDocument()
  })

  it('shows an error with a retry when the page does not load', async () => {
    publicServices.page.mockRejectedValue(Object.assign(new Error('x'), { response: { status: 500, data: '' } }))
    renderInVertical(testVertical({ publicServices }), '/sherbany/banya', '/:slug/:resourceSlug', <ResourcePage />)
    // the page retries once by itself (1 s) before it shows the error
    expect(await screen.findByRole('alert', {}, { timeout: 4000 })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Повторить' }))
    expect(publicServices.page.mock.calls.length).toBeGreaterThanOrEqual(3)
  }, 10000)
})
