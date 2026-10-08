import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ServicePage } from './ServicePage'
import { httpError } from '../test/fixtures'
import type { PublicServiceDto } from '../types'

const api = vi.hoisted(() => ({ page: vi.fn(), availability: vi.fn(), starts: vi.fn(), quote: vi.fn(), createOrder: vi.fn() }))
vi.mock('../api/publicServices', () => ({ publicServicesApi: api }))
vi.mock('../api/legalTexts', () => ({ stayLegalTextsApi: { get: () => Promise.reject(httpError(404, '')) } }))

const service = (over: Partial<PublicServiceDto> = {}): PublicServiceDto => ({
  id: 's1',
  slug: 'banya',
  name: 'Баня на дровах',
  description: 'Правила посещения: приходите без алкоголя.',
  photos: [],
  minHours: 2,
  maxHours: 6,
  stepMinutes: 60,
  priceTable: [{ label: 'Пт 18:00 — 02:00 (ночь на сб)', priceRub: 2000 }],
  items: [{ id: 'i1', name: 'Веник', priceRub: 300, maxPerSession: 3 }],
  standalone: { ordering: true, prepayPercent: 30, cancellationPolicy: 'PreparationCosts', cancellationBoundaryHours: 12, cancellationSummary: 'Расходы на подготовку: отмена не позднее чем за 12 ч до начала — вся предоплата', holdMinutes: 30 },
  company: { slug: 'kedr-park', name: 'Кедр Парк', phone: '+79001112233', url: '/kedr-park' },
  acceptingBookings: true,
  available: true,
  today: '2027-01-14',
  timeZoneId: 'Asia/Novokuznetsk',
  ...over,
})

function renderPage() {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter initialEntries={['/kedr-park/uslugi/banya']}>
        <Routes>
          <Route path="/:slug/uslugi/:serviceSlug" element={<ServicePage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  Object.values(api).forEach((f) => f.mockReset())
  api.availability.mockResolvedValue({ serviceId: 's1', today: '2027-01-14', days: [] })
})

describe('ServicePage', () => {
  it('shows the description, the price table in the guest wording, the cancellation rule and the order form', async () => {
    api.page.mockResolvedValue(service())
    renderPage()
    expect(await screen.findByRole('heading', { name: 'Баня на дровах' })).toBeInTheDocument()
    expect(screen.getByText('Правила посещения: приходите без алкоголя.')).toBeInTheDocument()
    expect(screen.getByText('Пт 18:00 — 02:00 (ночь на сб)')).toBeInTheDocument()
    expect(screen.getByText(/Расходы на подготовку: отмена не позднее/)).toBeInTheDocument()
    expect(await screen.findByRole('region', { name: 'Заказ услуги' })).toBeInTheDocument()
  })

  it('has no tourist tax and never the word «бизнес-день» (Т39-15, ЮР39-8)', async () => {
    api.page.mockResolvedValue(service())
    renderPage()
    await screen.findByRole('heading', { name: 'Баня на дровах' })
    const text = document.body.textContent ?? ''
    expect(text).not.toMatch(/туристическ/i)
    expect(text.toLowerCase()).not.toContain('бизнес')
  })

  it('without orders on their own it says the service can be added to a house booking, and shows no form', async () => {
    api.page.mockResolvedValue(service({ standalone: { ordering: false, holdMinutes: 30, notOrderingText: 'Можно добавить к брони дома' } }))
    renderPage()
    expect(await screen.findByTestId('not-ordering')).toHaveTextContent('Можно добавить к брони дома')
    expect(screen.queryByText('Выберите время')).not.toBeInTheDocument()
  })

  it('shows «Бронирование временно недоступно» instead of the form when the gate is closed', async () => {
    api.page.mockResolvedValue(service({ acceptingBookings: false, notAcceptingText: 'Бронирование временно недоступно' }))
    renderPage()
    expect(await screen.findByText('Бронирование временно недоступно')).toBeInTheDocument()
    expect(screen.queryByText('Выберите время')).not.toBeInTheDocument()
  })

  it('a service that became unavailable says so; an unknown one is «not found»', async () => {
    api.page.mockResolvedValueOnce(service({ available: false, notAvailableText: 'Услуга недоступна для бронирования' }))
    renderPage()
    expect(await screen.findByText('Услуга недоступна для бронирования')).toBeInTheDocument()
  })

  it('an unknown service is «not found»', async () => {
    api.page.mockRejectedValue(httpError(404, ''))
    renderPage()
    expect(await screen.findByText('Услуга не найдена')).toBeInTheDocument()
  })
})
