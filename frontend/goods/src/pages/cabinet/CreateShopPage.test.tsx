import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { useAuthStore } from '@/store/authStore'
import { CreateShopPage } from './CreateShopPage'

const create = vi.fn()
const slugCheck = vi.fn()
const getManifest = vi.fn()
const searchCities = vi.fn()

vi.mock('../../api/shops', () => ({ shopsApi: { create: (...a: unknown[]) => create(...a), slugCheck: (...a: unknown[]) => slugCheck(...a) } }))
vi.mock('@/api/legal', () => ({ legalApi: { getManifest: (...a: unknown[]) => getManifest(...a), getText: () => new Promise(() => {}) } }))
vi.mock('@/api/cities', () => ({ citiesApi: { search: (...a: unknown[]) => searchCities(...a) } }))
vi.mock('@/api/companyAddress', () => ({ companyAddressApi: { confirmNotice: vi.fn() } }))

const BARNAUL = { id: 5, name: 'Барнаул', region: 'Алтайский край', timeZoneId: 'Asia/Barnaul', utcOffsetMinutes: 420, label: 'Барнаул, Алтайский край' }

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/cabinet/new']}>
        <Routes>
          <Route path="/cabinet/new" element={<CreateShopPage />} />
          <Route path="/cabinet/:shopId/catalog" element={<div>Каталог магазина</div>} />
          <Route path="/cabinet" element={<div>Мои магазины</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

/** The submit button stays disabled until the agreement is ticked AND the legal manifest has loaded. */
async function submitWhenReady(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('checkbox'))
  const btn = screen.getByRole('button', { name: 'Создать магазин' })
  await waitFor(() => expect(btn).toBeEnabled())
  await user.click(btn)
}

async function fillRequired(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Название *'), 'Шаурма на Ленина')
  await user.click(screen.getByRole('combobox', { name: 'Город *' }))
  await user.click(await screen.findByRole('option', { name: /Барнаул/ }))
  // the suggested address arrives after a short debounce; the field is required
  await waitFor(() => expect(screen.getByLabelText('Адрес магазина на goods *')).toHaveValue('shaurma-na-lenina'))
}

beforeEach(() => {
  create.mockReset()
  slugCheck.mockReset().mockImplementation((p: { slug?: string; name?: string }) =>
    Promise.resolve(p.slug ? { slug: p.slug, available: true } : { slug: 'shaurma-na-lenina', available: true }),
  )
  getManifest.mockReset().mockResolvedValue({ documents: [{ type: 'TermsOwner', version: 'v7', title: 'x' }], uiTexts: [] })
  searchCities.mockReset().mockResolvedValue([BARNAUL])
  useAuthStore.setState({ user: { id: 'u', phone: '79001234567', firstName: 'И', lastName: 'П', roles: ['Client'] }, token: 'old-token' })
})

describe('CreateShopPage', () => {
  it('suggests an address from the name, requires the owner agreement, and stores the NEW token from the response', async () => {
    create.mockResolvedValue({ shop: { id: 'shop-1' }, token: 'new-token' })
    const user = userEvent.setup()
    renderPage()
    await fillRequired(user)

    const submit = screen.getByRole('button', { name: 'Создать магазин' })
    expect(submit).toBeDisabled() // agreement not accepted yet
    await user.click(screen.getByRole('checkbox'))
    await waitFor(() => expect(submit).toBeEnabled())
    await user.click(submit)

    await screen.findByText('Каталог магазина')
    expect(create).toHaveBeenCalledWith(expect.objectContaining({ name: 'Шаурма на Ленина', slug: 'shaurma-na-lenina', cityId: 5, ownerTerms: { version: 'v7' } }))
    expect(useAuthStore.getState().token).toBe('new-token')
  })

  it('does not send anything without a city', async () => {
    const user = userEvent.setup()
    renderPage()
    await user.type(screen.getByLabelText('Название *'), 'Шаурма')
    await waitFor(() => expect(screen.getByLabelText('Адрес магазина на goods *')).toHaveValue('shaurma-na-lenina'))
    await submitWhenReady(user)
    expect(await screen.findByText('Укажите город магазина')).toBeInTheDocument()
    expect(create).not.toHaveBeenCalled()
  })

  it('shows the server reason when the address is taken', async () => {
    create.mockRejectedValue({ response: { status: 409, data: { code: 'SlugTaken', message: 'Адрес уже занят — выберите другой' } } })
    const user = userEvent.setup()
    renderPage()
    await fillRequired(user)
    await submitWhenReady(user)
    expect(await screen.findByText('Адрес уже занят — выберите другой')).toBeInTheDocument()
    expect(useAuthStore.getState().token).toBe('old-token')
  })

  it('explains the account company limit (402)', async () => {
    create.mockRejectedValue({ response: { status: 402, data: 'Достигнут лимит компаний по тарифу' } })
    const user = userEvent.setup()
    renderPage()
    await fillRequired(user)
    await submitWhenReady(user)
    expect(await screen.findByText('Достигнут лимит компаний по тарифу')).toBeInTheDocument()
  })

  it('flags an occupied address as the buyer types', async () => {
    slugCheck.mockImplementation((p: { slug?: string }) => Promise.resolve(p.slug ? { slug: p.slug, available: false, reason: 'Адрес уже занят — выберите другой', reasonCode: 'SlugTaken' } : { slug: 'x', available: true }))
    const user = userEvent.setup()
    renderPage()
    await user.type(screen.getByLabelText('Адрес магазина на goods *'), 'taken-shop')
    expect(await screen.findByRole('status')).toHaveTextContent('Адрес уже занят — выберите другой')
  })

  it('normalizes what is typed into the address field', async () => {
    const user = userEvent.setup()
    renderPage()
    const field = screen.getByLabelText('Адрес магазина на goods *')
    await user.type(field, 'My Shop!')
    expect(field).toHaveValue('my-shop-')
  })
})
