import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { DevicesPage } from './DevicesPage'

vi.mock('../../components/staffMax/StaffMaxCard', () => ({ StaffMaxCard: () => null }))
const useWebPush = vi.fn()
vi.mock('@/hooks/useWebPush', () => ({ useWebPush: (...a: unknown[]) => useWebPush(...a) }))

const base = { reason: null, isLoading: false, isSubscribedOnThisDevice: false, devices: [], isEnabling: false, isDisabling: false, actionError: null, enableOnThisDevice: vi.fn(), disableOnThisDevice: vi.fn(), disableDevice: vi.fn() }
const renderPage = () => render(<MemoryRouter><DevicesPage /></MemoryRouter>)

beforeEach(() => {
  useWebPush.mockReset().mockReturnValue({ ...base })
  base.enableOnThisDevice.mockReset()
  base.disableOnThisDevice.mockReset()
  base.disableDevice.mockReset()
})

describe('DevicesPage', () => {
  it('uses the Orders site and never drops the browser subscription (staff and buyer share it)', () => {
    renderPage()
    expect(useWebPush).toHaveBeenCalledWith({ site: 'Orders', keepBrowserSubscription: true })
  })

  it('turns push on for this device only on a press', async () => {
    const user = userEvent.setup()
    renderPage()
    expect(base.enableOnThisDevice).not.toHaveBeenCalled()
    await user.click(screen.getByRole('switch'))
    expect(base.enableOnThisDevice).toHaveBeenCalledTimes(1)
  })

  it('lists devices and disables any one of them, the current one marked', async () => {
    useWebPush.mockReturnValue({ ...base, isSubscribedOnThisDevice: true, devices: [
      { id: 'd1', deviceLabel: 'Chrome на Windows', createdAtUtc: '2026-09-01T10:00:00Z', lastSuccessAtUtc: null, isCurrent: true },
      { id: 'd2', deviceLabel: 'Safari на iOS', createdAtUtc: '2026-09-02T10:00:00Z', lastSuccessAtUtc: '2026-09-03T10:00:00Z', isCurrent: false },
    ] })
    const user = userEvent.setup()
    renderPage()
    expect(screen.getByText(/Chrome на Windows/)).toHaveTextContent('это устройство')
    await user.click(screen.getByRole('button', { name: 'Отключить уведомления на устройстве «Safari на iOS»' }))
    expect(base.disableDevice).toHaveBeenCalledWith('d2')
  })

  it('has an empty state for the device list', () => {
    renderPage()
    expect(screen.getByTestId('no-devices')).toBeInTheDocument()
  })

  it('explains the platform switch-off with the fixed wording and offers no toggle', () => {
    useWebPush.mockReturnValue({ ...base, reason: 'platform-disabled' })
    renderPage()
    expect(screen.getByTestId('push-unavailable')).toHaveTextContent('Уведомления на устройство пока не включены на платформе')
    expect(screen.queryByRole('switch')).toBeNull()
  })

  it('gives step-by-step Home Screen instructions on an iPhone outside the installed app', () => {
    useWebPush.mockReturnValue({ ...base, reason: 'ios-safari-not-installed' })
    renderPage()
    expect(screen.getByRole('list', { name: /Как добавить приложение на экран «Домой»/ })).toBeInTheDocument()
  })

  it('shows the action error', () => {
    useWebPush.mockReturnValue({ ...base, actionError: 'Не удалось включить уведомления' })
    renderPage()
    expect(screen.getByRole('alert')).toHaveTextContent('Не удалось включить уведомления')
  })
})
