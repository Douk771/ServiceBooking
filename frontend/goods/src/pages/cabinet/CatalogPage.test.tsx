import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom'
import { CatalogPage } from './CatalogPage'
import type { CategoryDto, ProductDto, ShopManageDto } from '../../types'

const categories = vi.fn()
const products = vi.fn()
const setSoldOut = vi.fn()
const deleteCategory = vi.fn()
vi.mock('../../api/catalog', () => ({
  catalogApi: {
    categories: (...a: unknown[]) => categories(...a),
    products: (...a: unknown[]) => products(...a),
    setSoldOut: (...a: unknown[]) => setSoldOut(...a),
    deleteCategory: (...a: unknown[]) => deleteCategory(...a),
    setStock: vi.fn(),
  },
}))

const shop = (over: Partial<ShopManageDto['settings']> = {}) =>
  ({ id: 's1', name: 'Шаурма', settings: { customerMode: 'Anyone', acceptanceMode: 'Manual', allowCustomerCancel: true, trackStock: false, ...over } }) as unknown as ShopManageDto

const cat = (over: Partial<CategoryDto> = {}): CategoryDto => ({ id: 'c1', name: 'Горячее', position: 1, isHidden: false, productCount: 1, ...over })
const prod = (over: Partial<ProductDto> = {}): ProductDto =>
  ({ id: 'p1', categoryId: 'c1', name: 'Шаурма классическая', unit: 'Piece', price: 250, position: 1, isPublished: true, isSoldOut: false, foodInfo: {}, stock: { onHand: null, reserved: 0, free: null }, availableToCustomers: true, ...over }) as ProductDto

function renderPage(isOwner: boolean, settings: Partial<ShopManageDto['settings']> = {}) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/cabinet/s1/catalog']}>
        <Routes>
          <Route element={<Outlet context={{ shop: shop(settings), isOwner }} />}>
            <Route path="/cabinet/:shopId/catalog" element={<CatalogPage />} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  categories.mockReset().mockResolvedValue([cat(), cat({ id: 'c2', name: 'Напитки', position: 2, productCount: 0 })])
  products.mockReset().mockResolvedValue([prod(), prod({ id: 'p2', categoryId: null, name: 'Пирожок', position: 1 })])
  setSoldOut.mockReset()
  deleteCategory.mockReset()
})

describe('CatalogPage', () => {
  it('owner gets every editing control', async () => {
    renderPage(true)
    await screen.findByText('Шаурма классическая')
    expect(screen.getByRole('button', { name: 'Категория' })).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Товар' }).length).toBeGreaterThan(0)
    expect(screen.getByRole('button', { name: 'Изменить товар Шаурма классическая' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Удалить категорию Горячее' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Поднять товар Шаурма классическая' })).toBeInTheDocument()
  })

  it('staff (Master) gets no editing control — only «Закончилось» (and the stock when tracked)', async () => {
    renderPage(false)
    await screen.findByText('Шаурма классическая')
    expect(screen.queryByRole('button', { name: 'Категория' })).toBeNull()
    expect(screen.queryAllByRole('button', { name: 'Товар' })).toHaveLength(0)
    expect(screen.queryByRole('button', { name: /Изменить товар/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /Удалить/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /Поднять|Опустить/ })).toBeNull()
    expect(screen.getAllByRole('button', { name: 'Закончилось' }).length).toBeGreaterThan(0)
    expect(screen.queryByText('Изменить остаток')).toBeNull() // stock is not tracked in this shop
  })

  it('shows the stock editor when the shop tracks stock', async () => {
    products.mockResolvedValue([prod({ stock: { onHand: 5, reserved: 2, free: 3 } })])
    renderPage(false, { trackStock: true })
    expect(await screen.findByText('Изменить остаток')).toBeInTheDocument()
    expect(screen.getByText('5 шт')).toBeInTheDocument()
  })

  it('warns when the reserve is above the stock', async () => {
    products.mockResolvedValue([prod({ stock: { onHand: 1, reserved: 3, free: -2 } })])
    renderPage(false, { trackStock: true })
    expect(await screen.findByRole('alert')).toHaveTextContent('Резерв больше остатка на 2 шт')
  })

  it('toggles «закончилось» right away and updates the row', async () => {
    setSoldOut.mockResolvedValue(prod({ isSoldOut: true }))
    const user = userEvent.setup()
    renderPage(false)
    const row = (await screen.findByText('Шаурма классическая')).closest('li')!
    await user.click(within(row).getByRole('button', { name: 'Закончилось' }))
    expect(setSoldOut).toHaveBeenCalledWith('s1', 'p1', true)
    expect(await within(row).findByRole('button', { name: 'Вернуть в продажу' })).toBeInTheDocument()
  })

  it('shows the server explanation when a non-empty category cannot be deleted', async () => {
    deleteCategory.mockRejectedValue({ response: { status: 409, data: { code: 'CategoryNotEmpty', message: 'В категории есть товары — сначала перенесите или удалите их' } } })
    const user = userEvent.setup()
    renderPage(true)
    await user.click(await screen.findByRole('button', { name: 'Удалить категорию Горячее' }))
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Удалить' }))
    expect(await screen.findByText('В категории есть товары — сначала перенесите или удалите их')).toBeInTheDocument()
  })

  it('filters by search and hides categories without a match', async () => {
    const user = userEvent.setup()
    renderPage(true)
    await screen.findByText('Шаурма классическая')
    await user.type(screen.getByRole('searchbox', { name: 'Поиск товара' }), 'пирожок')
    expect(screen.getByText('Пирожок')).toBeInTheDocument()
    expect(screen.queryByText('Шаурма классическая')).toBeNull()
    expect(screen.queryByRole('heading', { name: 'Горячее' })).toBeNull()
  })

  it('has an empty state and an error state', async () => {
    categories.mockResolvedValue([])
    products.mockResolvedValue([])
    const first = renderPage(true)
    expect(await screen.findByText('Каталог пуст')).toBeInTheDocument()
    first.unmount()
    products.mockRejectedValue({ response: { status: 500, data: '' } })
    renderPage(true)
    expect(await screen.findByRole('button', { name: 'Повторить' })).toBeInTheDocument()
  })
})
