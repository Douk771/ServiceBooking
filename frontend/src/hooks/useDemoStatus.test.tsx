import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { useDemoStatus } from './useDemoStatus'
import { DemoProductProvider } from '../components/demo/DemoProductContext'
import { demoOrdersStatus, demoStatus } from '../components/demo/testUtils'
import type { DemoProduct } from '../api/demo'

const getStatus = vi.fn()
vi.mock('../api/demo', () => ({ demoApi: { getStatus: (...a: unknown[]) => getStatus(...a) } }))

function setup(product?: DemoProduct) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={qc}>
      {product ? <DemoProductProvider product={product}>{children}</DemoProductProvider> : children}
    </QueryClientProvider>
  )
  return { qc, ...renderHook(() => useDemoStatus(), { wrapper }) }
}

beforeEach(() => getStatus.mockReset())

describe('useDemoStatus (ARCHITECTURE_CYCLE35.md §35.7.3)', () => {
  it('without a provider: asks for `services` (the cycle-28 request) and caches under [demo-status, services]', async () => {
    getStatus.mockResolvedValue(demoStatus())
    const { result, qc } = setup()
    await waitFor(() => expect(result.current.isDemo).toBe(true))
    expect(getStatus).toHaveBeenCalledWith('services')
    expect(qc.getQueryData(['demo-status', 'services'])).toBeTruthy()
  })

  it('orders provider: asks for `orders` and exposes the shop roles', async () => {
    getStatus.mockResolvedValue(demoOrdersStatus())
    const { result, qc } = setup('orders')
    await waitFor(() => expect(result.current.isDemo).toBe(true))
    expect(getStatus).toHaveBeenCalledWith('orders')
    expect(result.current.status?.roles.map((r) => r.role)).toEqual(['shop-owner', 'shop-staff', 'shop-customer'])
    expect(qc.getQueryData(['demo-status', 'orders'])).toBeTruthy()
    expect(qc.getQueryData(['demo-status', 'services'])).toBeUndefined()
  })

  it('production (null): isDemo is false', async () => {
    getStatus.mockResolvedValue(null)
    const { result } = setup('orders')
    await waitFor(() => expect(getStatus).toHaveBeenCalled())
    await waitFor(() => expect(result.current.isFetching).toBe(false))
    expect(result.current.isDemo).toBe(false)
  })
})
