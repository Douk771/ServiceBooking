import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { CatalogHomePage } from './CatalogHomePage'

const list = vi.fn()
vi.mock('../api/goodsCatalog', () => ({ goodsCatalogApi: { list: (...a: unknown[]) => list(...a) } }))
vi.mock('@/api/cities', () => ({ citiesApi: { search: () => Promise.resolve([]) } }))

const shop = (over: Record<string, unknown> = {}) => ({
  slug: 's', path: '/s', name: 'Шаурма', address: 'Ленина, 5', cityName: 'Барнаул',
  openState: { isOpen: true, text: 'Открыто до 21:00' }, acceptance: 'AcceptingNow', acceptanceText: 'Принимает заказы', logoUrl: null, ...over,
})

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter><CatalogHomePage /></MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('CatalogHomePage ShopRow logo (V29-32)', () => {
  beforeEach(() => {
    localStorage.clear()
    list.mockReset()
  })

  it('image with logoUrl, letter without, letter after image error; aria-label unchanged', async () => {
    list.mockResolvedValue({ items: [shop({ logoUrl: '/uploads/a.jpg' }), shop({ slug: 'b', path: '/b', name: 'Бублики' })], page: 1, pageSize: 20, totalCount: 2 })
    renderPage()
    const img = await screen.findByTestId('company-logo-img')
    expect(img).toHaveAttribute('src', '/uploads/a.jpg')
    expect(screen.getByTestId('company-logo-initial')).toHaveTextContent('Б')
    expect(screen.getByRole('link', { name: 'Шаурма, Открыто до 21:00, Принимает заказы' })).toBeInTheDocument()
    fireEvent.error(img)
    expect(screen.queryByTestId('company-logo-img')).toBeNull()
    expect(screen.getAllByTestId('company-logo-initial')).toHaveLength(2)
  })
})
