import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { act, screen, waitFor } from '@testing-library/react'
import { DemoMaintenanceGate } from './DemoMaintenanceGate'
import { DemoMaintenanceScreen, DEMO_MAINTENANCE_TEXT } from './DemoMaintenanceScreen'
import { DEMO_STATUS_POLL_MS } from '../../hooks/useDemoStatus'
import { useDemoStore } from '../../store/demoStore'
import { demoStatus, renderWithProviders } from './testUtils'

const getStatus = vi.fn()
vi.mock('../../api/demo', () => ({ demoApi: { getStatus: (...a: unknown[]) => getStatus(...a) } }))

const robots = () => document.head.querySelector('meta[name="robots"]')
const App = () => (
  <DemoMaintenanceGate>
    <div data-testid="app">приложение</div>
  </DemoMaintenanceGate>
)

beforeEach(() => {
  getStatus.mockReset()
  useDemoStore.setState({ resetting: false })
})
afterEach(() => {
  vi.useRealTimers()
})

describe('DemoMaintenanceGate (API_CONTRACT_CYCLE28.md §597, §600a)', () => {
  it('production: renders the app as is and adds no robots meta', async () => {
    getStatus.mockResolvedValue(null)
    renderWithProviders(<App />)
    expect(screen.getByTestId('app')).toBeInTheDocument()
    await waitFor(() => expect(getStatus).toHaveBeenCalled())
    expect(robots()).toBeNull()
    expect(screen.queryByTestId('demo-maintenance')).toBeNull()
  })

  it('demo: the app renders and meta robots noindex is set, removed on unmount', async () => {
    getStatus.mockResolvedValue(demoStatus())
    const { unmount } = renderWithProviders(<App />)
    await waitFor(() => expect(robots()).toHaveAttribute('content', 'noindex, nofollow'))
    expect(screen.getByTestId('app')).toBeInTheDocument()
    unmount()
    expect(robots()).toBeNull()
  })

  it('page opened mid-reset (status.resetting): the maintenance message replaces the app', async () => {
    getStatus.mockResolvedValue(demoStatus({ resetting: true }))
    renderWithProviders(<App />)
    expect(await screen.findByTestId('demo-maintenance')).toHaveTextContent(DEMO_MAINTENANCE_TEXT)
    expect(screen.queryByTestId('app')).toBeNull()
  })

  it('an API call answered 503 + X-Demo-Resetting (store flag) swaps the app for the message', async () => {
    getStatus.mockResolvedValue(demoStatus())
    renderWithProviders(<App />)
    await waitFor(() => expect(robots()).not.toBeNull())
    act(() => useDemoStore.getState().setResetting(true))
    expect(await screen.findByTestId('demo-maintenance')).toBeInTheDocument()
    expect(screen.queryByTestId('app')).toBeNull()
  })
})

describe('DemoMaintenanceScreen polling (§600a)', () => {
  it('asks the status right away; while it still says resetting it does not reload, then re-asks every 15 s', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const onReload = vi.fn()
    getStatus.mockResolvedValue(demoStatus({ resetting: true }))
    renderWithProviders(<DemoMaintenanceScreen onReload={onReload} />)
    await waitFor(() => expect(getStatus).toHaveBeenCalledTimes(1))
    expect(onReload).not.toHaveBeenCalled()

    await act(async () => {
      await vi.advanceTimersByTimeAsync(DEMO_STATUS_POLL_MS + 100)
    })
    expect(getStatus.mock.calls.length).toBeGreaterThanOrEqual(2)
    expect(onReload).not.toHaveBeenCalled()
  })

  it('reloads once a fresh answer says the reset is over', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const onReload = vi.fn()
    getStatus.mockResolvedValueOnce(demoStatus({ resetting: true })).mockResolvedValue(demoStatus({ resetting: false }))
    renderWithProviders(<DemoMaintenanceScreen onReload={onReload} />)
    await waitFor(() => expect(getStatus).toHaveBeenCalled())
    expect(onReload).not.toHaveBeenCalled()
    await act(async () => {
      await vi.advanceTimersByTimeAsync(DEMO_STATUS_POLL_MS + 100)
    })
    await waitFor(() => expect(onReload).toHaveBeenCalledTimes(1))
  })

  it('a stale cached "not resetting" (from before the 503) does not trigger a reload by itself', async () => {
    const onReload = vi.fn()
    let resolveFresh: (v: ReturnType<typeof demoStatus>) => void = () => {}
    getStatus.mockImplementation(
      () =>
        new Promise((r) => {
          resolveFresh = r
        }),
    )
    const { qc } = renderWithProviders(<div />)
    qc.setQueryData(['demo-status'], demoStatus({ resetting: false }))
    // cache is older than the screen mount
    renderWithProviders(<DemoMaintenanceScreen onReload={onReload} />, qc)
    await new Promise((r) => setTimeout(r, 20))
    expect(onReload).not.toHaveBeenCalled()
    await act(async () => resolveFresh(demoStatus({ resetting: true })))
    expect(onReload).not.toHaveBeenCalled()
  })
})
