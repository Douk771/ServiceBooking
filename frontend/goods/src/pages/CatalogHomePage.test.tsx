import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CatalogHomePage } from './CatalogHomePage'
import { goodsCatalogApi } from '../api/goodsCatalog'

vi.mock('../api/goodsCatalog', () => ({ goodsCatalogApi: { list: vi.fn() } }))

const list = vi.mocked(goodsCatalogApi.list)
const setup = () => {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/city/5?openNow=1']}>
        <CatalogHomePage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}
const follows = (a: Element, b: Element) => !!(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING)

describe('CatalogHomePage blocks', () => {
  beforeEach(() => {
    list.mockReset()
  })

  it('T30-18 order: shops section, buyers, business; how-to link before the search form', async () => {
    list.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, city: null, emptyText: 'Пусто' } as never)
    const { container } = setup()
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
    setup()
    await screen.findByRole('button', { name: 'Повторить' })
    expect(screen.getByRole('region', { name: 'Соберите заказ с телефона и заберите, когда он готов' })).toBeTruthy()
    expect(screen.getByRole('region', { name: 'Магазин и кафе принимают заказы без звонков и переписок' })).toBeTruthy()
  })
})
