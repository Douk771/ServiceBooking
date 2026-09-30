import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { EmbedPage } from './EmbedPage'

vi.mock('../components/booking/BookingModal', () => ({ BookingModal: () => <div data-testid="booking-modal" /> }))
const getBySlug = vi.fn()
const getByCompany = vi.fn()
vi.mock('../api/companies', () => ({ companiesApi: { getBySlug: (...a: unknown[]) => getBySlug(...a) } }))
vi.mock('../api/services', () => ({ servicesApi: { getByCompany: (...a: unknown[]) => getByCompany(...a) } }))

beforeEach(() => {
  getBySlug.mockReset()
  getByCompany.mockReset().mockResolvedValue([])
})

function renderEmbed() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/embed/shop']}>
        <Routes>
          <Route path="/embed/:slug" element={<EmbedPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('EmbedPage — cycle 23 (§389.3-2)', () => {
  it('shows «нет онлайн-записи» for a shop and never asks for services', async () => {
    getBySlug.mockResolvedValue({ id: 'c1', name: 'Шаурма', slug: 'shop', allowSelfBooking: true, kind: 'Orders' })
    renderEmbed()
    expect(await screen.findByText('У этой компании нет онлайн-записи')).toBeInTheDocument()
    expect(getByCompany).not.toHaveBeenCalled()
  })
})

describe('EmbedPage — cycle 29 (US-29-07)', () => {
  const svc = { id: 's1', name: 'Стрижка', durationMinutes: 30, price: 1000 }
  const salon = (over: Record<string, unknown> = {}) => ({ id: 'c1', name: 'Салон', slug: 'shop', allowSelfBooking: true, kind: 'Salon', photos: [], ...over })
  const photo = (i: number) => ({ id: `p${i}`, url: `/u/${i}.jpg`, thumbnailUrl: `/u/t${i}.jpg`, width: 800, height: 600, position: i, isCover: i === 0 })

  it('V29-20: gallery and two map links with target=_blank', async () => {
    getBySlug.mockResolvedValue(salon({ photos: [photo(0), photo(1)], yandexMapsUrl: 'https://yandex.ru/maps/1', twoGisUrl: 'https://2gis.ru/1' }))
    renderEmbed()
    expect(await screen.findByRole('group', { name: 'Фотографии Салон' })).toBeInTheDocument()
    const y = screen.getByRole('link', { name: /Открыть в Яндекс Картах/ })
    const g = screen.getByRole('link', { name: /Открыть в 2ГИС/ })
    expect(y).toHaveAttribute('target', '_blank')
    expect(g).toHaveAttribute('target', '_blank')
  })

  it('V29-21: no gallery, links or wrapper without photos and links', async () => {
    getBySlug.mockResolvedValue(salon())
    const { container } = renderEmbed()
    await screen.findByText('Салон')
    expect(screen.queryByRole('group', { name: /Фотографии/ })).toBeNull()
    expect(screen.queryByRole('link', { name: /Открыть в/ })).toBeNull()
    expect(container.querySelector('.mb-4')).toBeNull()
  })

  it('V29-22: booking button still opens BookingModal', async () => {
    getBySlug.mockResolvedValue(salon())
    getByCompany.mockResolvedValue([svc])
    renderEmbed()
    fireEvent.click(await screen.findByRole('button', { name: 'Записаться' }))
    expect(screen.getByTestId('booking-modal')).toBeInTheDocument()
  })
})
