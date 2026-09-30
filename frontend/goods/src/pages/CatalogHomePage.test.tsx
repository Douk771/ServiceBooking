import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CatalogHomePage } from './CatalogHomePage'
import { goodsCatalogApi } from '../api/goodsCatalog'

vi.mock('../api/goodsCatalog', () => ({ goodsCatalogApi: { list: vi.fn() } }))
vi.mock('@/api/cities', () => ({ citiesApi: { search: () => Promise.resolve([]) } }))

const list = vi.mocked(goodsCatalogApi.list)

const shop = (over: Record<string, unknown> = {}) => ({
  slug: 's', path: '/s', name: 'Шаурма', address: 'Ленина, 5', cityName: 'Барнаул',
  openState: { isOpen: true, text: 'Открыто до 21:00' }, acceptance: 'AcceptingNow', acceptanceText: 'Принимает заказы', logoUrl: null, ...over,
})

const setup = (entry = '/') => {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[entry]}>
        <CatalogHomePage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}
const follows = (a: Element, b: Element) => !!(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING)

describe('CatalogHomePage ShopRow logo (V29-32)', () => {
  beforeEach(() => {
    localStorage.clear()
    list.mockReset()
  })

  it('image with logoUrl, letter without, letter after image error; aria-label unchanged', async () => {
    list.mockResolvedValue({ items: [shop({ logoUrl: '/uploads/a.jpg' }), shop({ slug: 'b', path: '/b', name: 'Бублики' })], page: 1, pageSize: 20, totalCount: 2 } as never)
    setup()
    const img = await screen.findByTestId('company-logo-img')
    expect(img).toHaveAttribute('src', '/uploads/a.jpg')
    expect(screen.getByTestId('company-logo-initial')).toHaveTextContent('Б')
    expect(screen.getByRole('link', { name: 'Шаурма, Открыто до 21:00, Принимает заказы' })).toBeInTheDocument()
    fireEvent.error(img)
    expect(screen.queryByTestId('company-logo-img')).toBeNull()
    expect(screen.getAllByTestId('company-logo-initial')).toHaveLength(2)
  })
})

describe('CatalogHomePage blocks', () => {
  beforeEach(() => {
    list.mockReset()
  })

  it('T30-18 order: shops section, buyers, business; how-to link before the search form', async () => {
    list.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, city: null, emptyText: 'Пусто' } as never)
    const { container } = setup('/city/5?openNow=1')
    await waitFor(() => expect(screen.getByText('Пусто')).toBeTruthy())
    const shops = container.querySelector('main section[aria-label="Магазины"]') as HTMLElement
    expect(shops.id).toBe('shop-list')
    expect(shops.tabIndex).toBe(-1)
    const buyers = screen.getByRole('region', { name: 'Соберите заказ с телефона и заберите, когда он готов' })
    const biz = screen.getByRole('region', { name: 'Магазин и кафе принимают заказы без звонков и переписок' })
    expect(follows(shops, buyers)).toBe(true)
    expect(follows(buyers, biz)).toBe(true)
    const link = screen.getByRole('link', { name: 'Как сделать заказ' })
    expect(link.getAttribute('href')).toBe('#buyers-title')
    expect(follows(link, screen.getByRole('search'))).toBe(true)
  })

  it('T30-19 blocks stay when the catalog fails', async () => {
    list.mockRejectedValue(Object.assign(new Error('500'), { response: { status: 500, data: '' } }))
    setup('/city/5?openNow=1')
    await screen.findByRole('button', { name: 'Повторить' })
    expect(screen.getByRole('region', { name: 'Соберите заказ с телефона и заберите, когда он готов' })).toBeTruthy()
    expect(screen.getByRole('region', { name: 'Магазин и кафе принимают заказы без звонков и переписок' })).toBeTruthy()
  })
})
