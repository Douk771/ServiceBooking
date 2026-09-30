import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { DevicesAndNotificationsSection } from './DevicesAndNotificationsSection'

const useWebPush = vi.fn()
vi.mock('../../hooks/useWebPush', () => ({
  useWebPush: (...a: unknown[]) => useWebPush(...a),
  describeDevice: () => 'Chrome на Android',
}))

const salon = { companyId: 'c1', companyName: 'Салон', staffPushEnabled: true, kind: 'Services' as const }
const shop = { companyId: 'c2', companyName: 'Шаурма', staffPushEnabled: true, kind: 'Orders' as const }
const fns = { enableOnThisDevice: vi.fn(), disableOnThisDevice: vi.fn(), disableDevice: vi.fn() }
const base = {
  reason: null, isLoading: false, isSubscribedOnThisDevice: false, devices: [], isEnabling: false, isDisabling: false,
  actionError: null, companies: [salon], hasServices: true, hasOrders: false, isStaff: true, siteUrls: undefined,
  devicesError: false, ...fns,
}
const device = (over: Record<string, unknown>) => ({
  id: 'd1', deviceLabel: 'Chrome на Windows', createdAtUtc: '2026-09-01T10:00:00Z', lastSuccessAtUtc: null, isCurrent: false, site: 'Services', ...over,
})

function renderSection(props: Partial<React.ComponentProps<typeof DevicesAndNotificationsSection>> = {}, path = '/profile') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <DevicesAndNotificationsSection site="Services" appName="Запись" {...props} />
    </MemoryRouter>,
  )
}

beforeEach(() => {
  useWebPush.mockReset().mockReturnValue({ ...base })
  Object.values(fns).forEach((f) => f.mockReset())
})

describe('DevicesAndNotificationsSection (ARCHITECTURE_CYCLE33.md §33.7)', () => {
  it('passes site and keepBrowserSubscription to the hook', () => {
    renderSection({ site: 'Orders', appName: 'Заказы', keepBrowserSubscription: true })
    expect(useWebPush).toHaveBeenCalledWith({ site: 'Orders', keepBrowserSubscription: true })
  })

  it('renders nothing while the role is unknown', () => {
    useWebPush.mockReturnValue({ ...base, isStaff: undefined, isLoading: true, companies: [] })
    const { container } = renderSection()
    expect(container).toBeEmptyDOMElement()
  })

  it('renders nothing for a user who is not staff anywhere', () => {
    useWebPush.mockReturnValue({ ...base, isStaff: false, companies: [] })
    const { container } = renderSection()
    expect(container).toBeEmptyDOMElement()
  })

  it('shows the heading, the intro for a salon and the switch; enable only on a press', async () => {
    const user = userEvent.setup()
    renderSection()
    expect(screen.getByRole('heading', { name: 'Устройства и уведомления' })).toBeInTheDocument()
    expect(screen.getByText(/а не сообщения клиентам/)).toBeInTheDocument()
    expect(fns.enableOnThisDevice).not.toHaveBeenCalled()
    await user.click(screen.getByRole('switch', { name: 'Уведомлять меня о новых записях на этом устройстве' }))
    expect(fns.enableOnThisDevice).toHaveBeenCalledTimes(1)
  })

  it('disables this device when already on', async () => {
    useWebPush.mockReturnValue({ ...base, isSubscribedOnThisDevice: true })
    const user = userEvent.setup()
    renderSection()
    const sw = screen.getByRole('switch')
    expect(sw).toHaveAttribute('aria-checked', 'true')
    await user.click(sw)
    expect(fns.disableOnThisDevice).toHaveBeenCalledTimes(1)
  })

  it('shows a skeleton in the switch area while the list loads', () => {
    useWebPush.mockReturnValue({ ...base, isLoading: true })
    renderSection()
    expect(screen.getByTestId('devices-loading')).toBeInTheDocument()
    expect(screen.queryByRole('switch')).toBeNull()
  })

  it('unavailable reason: notice instead of the switch, device list still shown', () => {
    useWebPush.mockReturnValue({ ...base, reason: 'platform-disabled', devices: [device({})] })
    renderSection()
    expect(screen.getByTestId('push-unavailable')).toHaveAttribute('data-reason', 'platform-disabled')
    expect(screen.queryByRole('switch')).toBeNull()
    expect(screen.getByText(/Chrome на Windows/)).toBeInTheDocument()
  })

  it('iPhone steps carry the app name and the one-site hint only for both kinds', () => {
    useWebPush.mockReturnValue({ ...base, reason: 'ios-safari-not-installed', companies: [salon, shop], hasOrders: true })
    renderSection({ appName: 'Заказы', site: 'Orders' })
    expect(screen.getByRole('list', { name: /«Заказы»/ })).toBeInTheDocument()
    expect(screen.getByText(/ezbook\.ru или goods\.ezbook\.ru/)).toBeInTheDocument()
  })

  it('action error is shown verbatim', () => {
    useWebPush.mockReturnValue({ ...base, actionError: 'Не удалось включить уведомления' })
    renderSection()
    expect(screen.getByRole('alert')).toHaveTextContent('Не удалось включить уведомления')
  })

  it('empty device list and list error', () => {
    const { unmount } = renderSection()
    expect(screen.getByTestId('no-devices')).toBeInTheDocument()
    unmount()
    useWebPush.mockReturnValue({ ...base, devicesError: true })
    renderSection()
    expect(screen.getByRole('alert')).toHaveTextContent('Не удалось загрузить список устройств')
  })

  it('device rows: site label, current mark, removal of a device of the other site', async () => {
    useWebPush.mockReturnValue({
      ...base,
      isSubscribedOnThisDevice: true,
      devices: [
        device({ id: 'd1', isCurrent: true, site: 'Services' }),
        device({ id: 'd2', deviceLabel: 'Safari на iOS', site: 'Orders', lastSuccessAtUtc: '2026-09-03T10:00:00Z' }),
      ],
    })
    const user = userEvent.setup()
    renderSection()
    expect(screen.getByText(/Chrome на Windows/)).toHaveTextContent('это устройство')
    expect(screen.getByText(/через ezbook\.ru/)).toBeInTheDocument()
    expect(screen.getByText(/через goods\.ezbook\.ru/)).toHaveTextContent('Последняя доставка')
    await user.click(screen.getByRole('button', { name: 'Отключить уведомления на устройстве «Safari на iOS»' }))
    expect(fns.disableDevice).toHaveBeenCalledWith('d2')
  })

  it('ordersExtra only when the user has a shop; «one device is enough» only with both kinds', () => {
    const extra = <div data-testid="extra" />
    const { unmount } = renderSection({ ordersExtra: extra })
    expect(screen.queryByTestId('extra')).toBeNull()
    expect(screen.queryByText(/Достаточно включить на одном устройстве/)).toBeNull()
    unmount()
    useWebPush.mockReturnValue({ ...base, companies: [salon, shop], hasOrders: true })
    renderSection({ ordersExtra: extra })
    expect(screen.getByTestId('extra')).toBeInTheDocument()
    expect(screen.getByText(/Достаточно включить на одном устройстве/)).toBeInTheDocument()
    expect(screen.getByRole('switch', { name: /записях и заказах/ })).toBeInTheDocument()
  })

  it('duplicate hint: same label on the other site, not enabled here', () => {
    useWebPush.mockReturnValue({ ...base, devices: [device({ deviceLabel: 'Chrome на Android', site: 'Orders' })] })
    renderSection()
    expect(screen.getByTestId('duplicate-hint')).toHaveTextContent(/Похоже.*уже включены через goods\.ezbook\.ru.*не нужно/)
  })

  it('duplicate hint: enabled on both → warns about double delivery', () => {
    useWebPush.mockReturnValue({
      ...base,
      isSubscribedOnThisDevice: true,
      devices: [
        device({ id: 'a', deviceLabel: 'Chrome на Android', site: 'Services', isCurrent: true }),
        device({ id: 'b', deviceLabel: 'Chrome на Android', site: 'Orders' }),
      ],
    })
    renderSection()
    expect(screen.getByTestId('duplicate-hint')).toHaveTextContent(/дважды/)
  })

  it('no duplicate hint when labels differ', () => {
    useWebPush.mockReturnValue({ ...base, devices: [device({ site: 'Orders' })] })
    renderSection()
    expect(screen.queryByTestId('duplicate-hint')).toBeNull()
  })

  it('#devices focuses the heading and scrolls once', () => {
    const scroll = vi.fn()
    Element.prototype.scrollIntoView = scroll
    renderSection({}, '/profile#devices')
    expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'Устройства и уведомления' }))
    expect(scroll).toHaveBeenCalledTimes(1)
  })

  it('without #devices the focus is not moved', () => {
    renderSection({}, '/profile')
    expect(document.activeElement).toBe(document.body)
  })
})
