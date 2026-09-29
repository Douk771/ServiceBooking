import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { OrderPushCard } from './OrderPushCard'
import { orderPushStorageKey } from '../../utils/goodsPush'

const subscribe = vi.fn()
const unsubscribe = vi.fn()
vi.mock('../../api/orderPush', () => ({ orderPushApi: { subscribe: (...a: unknown[]) => subscribe(...a), unsubscribe: (...a: unknown[]) => unsubscribe(...a) } }))

const KEY = 'BEl62iUYgUivxIkv69yViEuiBIa-Ib9-SkvMeAtA3LFgDzkrxZJjSgSnfckjBJuBkr3qBUYIHBQFLXYp5Nksh8U'
const rawKey = (n: number) => new Uint8Array(Array.from({ length: 8 }, (_, i) => i + n)).buffer

interface FakeSub { endpoint: string; getKey: (n: string) => ArrayBuffer; unsubscribe: ReturnType<typeof vi.fn> }
let existing: FakeSub | null
let requestPermission: ReturnType<typeof vi.fn>
let pmSubscribe: ReturnType<typeof vi.fn>

function installBrowser({ permission = 'default', ua = 'Mozilla/5.0 (Windows NT 10.0) Chrome/120.0' }: { permission?: string; ua?: string } = {}) {
  existing = null
  pmSubscribe = vi.fn(async () => (existing = { endpoint: 'https://push.example/abc', getKey: (n) => rawKey(n === 'auth' ? 50 : 1), unsubscribe: vi.fn() }))
  const registration = { update: vi.fn(), pushManager: { getSubscription: vi.fn(async () => existing), subscribe: pmSubscribe } }
  Object.defineProperty(navigator, 'serviceWorker', { configurable: true, value: { register: vi.fn(async () => registration), getRegistration: vi.fn(async () => registration) } })
  Object.defineProperty(navigator, 'userAgent', { configurable: true, value: ua })
  ;(window as unknown as { PushManager: unknown }).PushManager = function PushManager() {}
  requestPermission = vi.fn(async () => 'granted')
  ;(globalThis as unknown as { Notification: unknown }).Notification = Object.assign(function Notification() {}, { permission, requestPermission })
  Object.defineProperty(window, 'isSecureContext', { configurable: true, value: true })
}

beforeEach(() => {
  window.localStorage.clear()
  subscribe.mockReset().mockResolvedValue({ subscribed: true, subscriptionCount: 1 })
  unsubscribe.mockReset().mockResolvedValue(undefined)
  installBrowser()
})
afterEach(() => {
  delete (window as unknown as { PushManager?: unknown }).PushManager
  delete (globalThis as unknown as { Notification?: unknown }).Notification
  Reflect.deleteProperty(navigator, 'serviceWorker')
})

const info = (over: Record<string, unknown> = {}) => ({ available: true, publicKey: KEY, unavailableText: null, ...over })

describe('OrderPushCard', () => {
  it('renders nothing when the shop turned it off or the order is finished (available: false, no text)', () => {
    const { container } = render(<OrderPushCard token="t" info={info({ available: false })} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('prints the server text when the platform has push switched off', () => {
    render(<OrderPushCard token="t" info={info({ available: false, unavailableText: 'Уведомления в браузере пока не включены на платформе' })} />)
    expect(screen.getByTestId('order-push-unavailable')).toHaveTextContent('Уведомления в браузере пока не включены на платформе')
    expect(screen.queryByRole('button')).toBeNull()
  })

  it('never asks the browser for permission on mount — only when the button is pressed', async () => {
    render(<OrderPushCard token="t" info={info()} />)
    expect(await screen.findByRole('button', { name: 'Уведомлять о статусе в этом браузере' })).toBeInTheDocument()
    expect(requestPermission).not.toHaveBeenCalled()
  })

  it('subscribes on press: permission, subscription with the VAPID key, POST with endpoint and keys, remembered per order', async () => {
    const user = userEvent.setup()
    render(<OrderPushCard token="tok" info={info()} />)
    await user.click(await screen.findByRole('button', { name: 'Уведомлять о статусе в этом браузере' }))
    expect(await screen.findByText('Уведомления в этом браузере включены')).toBeInTheDocument()
    expect(requestPermission).toHaveBeenCalledTimes(1)
    expect(subscribe).toHaveBeenCalledWith('tok', expect.objectContaining({ endpoint: 'https://push.example/abc', keys: { p256dh: expect.any(String), auth: expect.any(String) } }))
    expect(window.localStorage.getItem(orderPushStorageKey('tok'))).toBe('https://push.example/abc')
  })

  it('does nothing further when the permission is refused', async () => {
    requestPermission.mockResolvedValue('denied')
    const user = userEvent.setup()
    render(<OrderPushCard token="t" info={info()} />)
    await user.click(await screen.findByRole('button', { name: 'Уведомлять о статусе в этом браузере' }))
    await waitFor(() => expect(requestPermission).toHaveBeenCalled())
    expect(subscribe).not.toHaveBeenCalled()
  })

  it('«Отключить» removes the server row but NEVER unsubscribes the browser (staff share that subscription)', async () => {
    const sub: FakeSub = { endpoint: 'https://push.example/abc', getKey: () => rawKey(1), unsubscribe: vi.fn() }
    existing = sub
    window.localStorage.setItem(orderPushStorageKey('tok'), sub.endpoint)
    const user = userEvent.setup()
    render(<OrderPushCard token="tok" info={info()} />)
    await user.click(await screen.findByRole('button', { name: 'Отключить' }))
    await waitFor(() => expect(unsubscribe).toHaveBeenCalledWith('tok', 'https://push.example/abc'))
    expect(sub.unsubscribe).not.toHaveBeenCalled()
    expect(await screen.findByRole('button', { name: 'Уведомлять о статусе в этом браузере' })).toBeInTheDocument()
    expect(window.localStorage.getItem(orderPushStorageKey('tok'))).toBeNull()
  })

  it('shows the server error text (409) when subscribing is refused', async () => {
    subscribe.mockRejectedValue({ response: { status: 409, data: 'Заказ уже завершён — уведомления по нему не приходят' } })
    const user = userEvent.setup()
    render(<OrderPushCard token="t" info={info()} />)
    await user.click(await screen.findByRole('button', { name: 'Уведомлять о статусе в этом браузере' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Заказ уже завершён — уведомления по нему не приходят')
  })

  it('on an iPhone outside the Home Screen app explains why and advises the messenger instead of offering a dead button', () => {
    installBrowser({ ua: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) AppleWebKit/605.1.15 Version/17.4 Mobile/15E148 Safari/604.1' })
    delete (window as unknown as { PushManager?: unknown }).PushManager
    render(<OrderPushCard token="t" info={info()} />)
    expect(screen.getByTestId('order-push-reason')).toHaveAttribute('data-reason', 'ios-safari-not-installed')
    expect(screen.getByTestId('order-push-reason')).toHaveTextContent('выберите при оформлении сообщения в MAX/WhatsApp')
    expect(screen.queryByRole('button')).toBeNull()
  })

  it('explains a denied permission and does not offer the button', () => {
    installBrowser({ permission: 'denied' })
    render(<OrderPushCard token="t" info={info()} />)
    expect(screen.getByTestId('order-push-reason')).toHaveAttribute('data-reason', 'permission-denied')
  })
})
