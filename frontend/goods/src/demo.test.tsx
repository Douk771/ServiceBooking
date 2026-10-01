import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { act, render, screen, waitFor } from '@testing-library/react'
import { AxiosError, type InternalAxiosRequestConfig } from 'axios'
import { api } from '@/api/client'
import { queryClient } from '@/queryClient'
import { useAuthStore } from '@/store/authStore'
import { useDemoStore } from '@/store/demoStore'
import { DEMO_RESTRICTED_FALLBACK } from '@/utils/demoHeaders'
import { demoOrdersStatus } from '@/components/demo/testUtils'
import { GoodsApp } from './GoodsApp'
import { getGoodsErrorMessage } from './utils/orderError'

// ARCHITECTURE_CYCLE35.md §35.13, §35.16.2 — the demo shell of goods («Заказы»): banner on every kind of page, the
// maintenance screen, the three shop role buttons, the demo refusal text. The whole GoodsApp is rendered in jsdom, but
// the goods pages are stubs and only the axios adapter is faked (the REAL interceptor chain and the REAL demoApi run);
// nothing here touches a network or a browser.

vi.mock('./pages/CatalogHomePage', () => ({ CatalogHomePage: () => <div data-testid="page">catalog</div> }))
vi.mock('./pages/StorefrontPage', () => ({ StorefrontPage: () => <div data-testid="page">storefront</div> }))
vi.mock('./pages/OrderPage', () => ({ OrderPage: () => <div data-testid="page">order</div> }))
vi.mock('./pages/MyOrdersPage', () => ({ MyOrdersPage: () => <div data-testid="page">my-orders</div> }))
vi.mock('./pages/GoodsProfilePage', () => ({ GoodsProfilePage: () => <div data-testid="page">profile</div> }))
vi.mock('./pages/NotFoundPage', () => ({ NotFoundPage: () => <div data-testid="page">not-found</div> }))
vi.mock('./pages/cabinet/CabinetHomePage', () => ({ CabinetHomePage: () => <div data-testid="page">cabinet</div> }))
vi.mock('./pages/cabinet/CreateShopPage', () => ({ CreateShopPage: () => <div data-testid="page">create</div> }))
vi.mock('./pages/cabinet/ShopLayout', async () => {
  const { Outlet } = await import('react-router-dom')
  return { ShopLayout: () => <Outlet /> }
})
vi.mock('./pages/cabinet/OrdersScreenPage', () => ({ OrdersScreenPage: () => <div data-testid="page">board</div> }))
vi.mock('./pages/cabinet/CatalogPage', () => ({ CatalogPage: () => null }))
vi.mock('./pages/cabinet/SettingsPage', () => ({ SettingsPage: () => null }))
vi.mock('./pages/cabinet/StaffPage', () => ({ StaffPage: () => null }))
vi.mock('./pages/cabinet/LinkPage', () => ({ LinkPage: () => null }))
vi.mock('./pages/cabinet/HoursPage', () => ({ HoursPage: () => null }))
vi.mock('./pages/cabinet/MenuPage', () => ({ MenuPage: () => null }))
vi.mock('./pages/cabinet/ShopNotificationsPage', () => ({ ShopNotificationsPage: () => null }))
vi.mock('./pages/cabinet/HistoryPage', () => ({ HistoryPage: () => null }))
vi.mock('./pages/cabinet/SummaryPage', () => ({ SummaryPage: () => null }))
vi.mock('./pages/cabinet/PickListPage', () => ({ PickListPage: () => null }))
vi.mock('./pages/cabinet/CustomerPage', () => ({ CustomerPage: () => null }))
vi.mock('./pages/cabinet/SubscriptionPage', () => ({ SubscriptionPage: () => null }))

type Reply = { status: number; data?: unknown; headers?: Record<string, string> }
let replies: Record<string, (config: InternalAxiosRequestConfig) => Reply>
let requests: { url: string; params?: Record<string, unknown> }[]
const realAdapter = api.defaults.adapter

const fakeAdapter = (config: InternalAxiosRequestConfig) => {
  const url = config.url ?? ''
  requests.push({ url, params: config.params as Record<string, unknown> | undefined })
  const reply = replies[url]?.(config) ?? { status: 404, data: '' }
  const response = {
    status: reply.status,
    statusText: '',
    data: reply.data ?? '',
    headers: reply.headers ?? {},
    config,
  }
  return reply.status >= 200 && reply.status < 300
    ? Promise.resolve(response)
    : Promise.reject(new AxiosError('fail', String(reply.status), config, undefined, response))
}

const robots = () => document.head.querySelector('meta[name="robots"]')

function openAt(path: string) {
  window.history.pushState({}, '', path)
  return render(<GoodsApp />)
}

beforeEach(() => {
  requests = []
  replies = { '/demo/status': () => ({ status: 200, data: demoOrdersStatus() }) }
  api.defaults.adapter = fakeAdapter
  queryClient.clear()
  useAuthStore.setState({ user: null, token: null })
  useDemoStore.setState({ resetting: false })
})
afterEach(() => {
  api.defaults.adapter = realAdapter
  window.history.pushState({}, '', '/')
})

describe('goods demo shell: banner (§35.13)', () => {
  it.each(['/', '/kofeinya', '/o/abc123token', '/login'])('%s: the demo banner is on the page', async (path) => {
    openAt(path)
    expect(await screen.findByTestId('demo-banner')).toBeInTheDocument()
    expect(screen.getByRole('note')).toBeInTheDocument()
  })

  it('/cabinet/:shopId/orders (signed in): the banner is above the board', async () => {
    useAuthStore.setState({
      user: { id: 'u1', phone: '7900', email: null, firstName: 'Демо', lastName: 'Роль', roles: ['CompanyOwner'] },
      token: 'demo-token',
    })
    openAt('/cabinet/11111111-1111-1111-1111-111111111111/orders')
    expect(await screen.findByTestId('demo-banner')).toBeInTheDocument()
    expect(screen.getByText('board')).toBeInTheDocument()
  })

  it('the banner is in the flow above the navbar, not fixed', async () => {
    openAt('/')
    const banner = await screen.findByTestId('demo-banner')
    expect(banner.className).not.toMatch(/\b(fixed|sticky|absolute)\b/)
    const nav = screen.getByRole('navigation')
    expect(banner.compareDocumentPosition(nav) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('noindex meta is set in the demo', async () => {
    openAt('/')
    await waitFor(() => expect(robots()).toHaveAttribute('content', 'noindex, nofollow'))
  })

  it('outside the demo (404 on the status): no banner, no noindex, the app renders as before', async () => {
    replies['/demo/status'] = () => ({ status: 404 })
    openAt('/')
    expect(await screen.findByText('catalog')).toBeInTheDocument()
    await waitFor(() => expect(requests.some((r) => r.url === '/demo/status')).toBe(true))
    expect(screen.queryByTestId('demo-banner')).toBeNull()
    expect(robots()).toBeNull()
    expect(screen.queryByTestId('demo-maintenance')).toBeNull()
  })
})

describe('goods demo shell: status request and login buttons (§35.21–§35.22)', () => {
  it('asks the status for the orders product', async () => {
    openAt('/')
    await screen.findByTestId('demo-banner')
    const status = requests.filter((r) => r.url === '/demo/status')
    expect(status.length).toBeGreaterThan(0)
    expect(status.every((r) => r.params?.product === 'orders')).toBe(true)
  })

  it('/login: exactly the three shop role buttons, no salon ones', async () => {
    openAt('/login')
    expect(await screen.findByRole('button', { name: 'Войти как владелец магазина' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Войти как сотрудник магазина' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Войти как покупатель' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /салона|мастер|клиент/i })).toBeNull()
  })

  it('/login outside the demo: no role buttons at all', async () => {
    replies['/demo/status'] = () => ({ status: 404 })
    openAt('/login')
    await waitFor(() => expect(requests.some((r) => r.url === '/demo/status')).toBe(true))
    await Promise.resolve()
    expect(screen.queryByTestId('demo-role-buttons')).toBeNull()
  })
})

describe('goods demo shell: maintenance screen (§35.24)', () => {
  it('status.resetting = true: the whole app is replaced by «Демо обновляется»', async () => {
    replies['/demo/status'] = () => ({ status: 200, data: demoOrdersStatus({ resetting: true }) })
    openAt('/')
    expect(await screen.findByTestId('demo-maintenance')).toHaveTextContent('Демо обновляется')
    expect(screen.queryByText('catalog')).toBeNull()
    expect(screen.queryByTestId('demo-banner')).toBeNull()
  })

  it('an API call answered 503 + X-Demo-Resetting swaps the app for the screen', async () => {
    replies['/orders/my'] = () => {
      // the stand really is resetting now: the status the screen polls says so too (no reload)
      replies['/demo/status'] = () => ({ status: 200, data: demoOrdersStatus({ resetting: true }) })
      return {
        status: 503,
        data: 'Демо обновляется, зайдите через минуту',
        headers: { 'x-demo-resetting': '1', 'retry-after': '60' },
      }
    }
    openAt('/')
    await screen.findByTestId('demo-banner')
    await act(async () => {
      await api.get('/orders/my').catch(() => undefined)
    })
    expect(await screen.findByTestId('demo-maintenance')).toBeInTheDocument()
    expect(screen.queryByText('catalog')).toBeNull()
  })

  it('an ordinary 503 (no X-Demo-Resetting) does not show the screen', async () => {
    replies['/orders/my'] = () => ({ status: 503, data: 'Сервис недоступен' })
    openAt('/')
    await screen.findByTestId('demo-banner')
    await act(async () => {
      await api.get('/orders/my').catch(() => undefined)
    })
    expect(screen.queryByTestId('demo-maintenance')).toBeNull()
    expect(screen.getByText('catalog')).toBeInTheDocument()
  })
})

describe('getGoodsErrorMessage: demo refusal (§35.23)', () => {
  const refusal = (status: number, data: unknown, headers: Record<string, string>) => ({
    response: { status, data, headers },
  })

  it('403 + X-Demo-Restricted: the body is the message', () => {
    const e = refusal(403, 'В демо-версии это действие недоступно.', { 'x-demo-restricted': '1' })
    expect(getGoodsErrorMessage(e)).toBe('В демо-версии это действие недоступно.')
  })

  it('403 + X-Demo-Restricted with an empty body: the fallback text', () => {
    expect(getGoodsErrorMessage(refusal(403, '', { 'x-demo-restricted': '1' }))).toBe(DEMO_RESTRICTED_FALLBACK)
  })

  it('403 without the header keeps the old wording (even with a body)', () => {
    expect(getGoodsErrorMessage(refusal(403, '', {}))).toBe('Недостаточно прав для этого действия.')
    expect(getGoodsErrorMessage(refusal(403, 'что-то', {}))).toBe('Недостаточно прав для этого действия.')
  })

  it('the header on a non-403 is not a demo refusal', () => {
    expect(getGoodsErrorMessage(refusal(400, 'Укажите имя', { 'x-demo-restricted': '1' }))).toBe('Укажите имя')
  })
})
