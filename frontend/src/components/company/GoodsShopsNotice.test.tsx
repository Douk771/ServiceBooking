import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { GoodsShopsNotice } from './GoodsShopsNotice'

const getKindsSummary = vi.fn()
// A plain function (not the spy) delegates the failing case: vitest's spy re-throws a rejection it records
// on a derived promise, which then shows up as an unhandled error attributed to the test.
let failWith: Error | null = null
vi.mock('../../api/companies', () => ({
  companiesApi: { getKindsSummary: (...a: unknown[]) => (failWith ? Promise.reject(failWith) : getKindsSummary(...a)) },
}))

function renderIt() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={qc}>
      <GoodsShopsNotice />
    </QueryClientProvider>,
  )
  return qc
}

beforeEach(() => {
  getKindsSummary.mockReset()
  failWith = null
})

describe('GoodsShopsNotice', () => {
  it('shows the goods link when the user has shops', async () => {
    getKindsSummary.mockResolvedValue({ services: { count: 1, siteUrl: 'https://ezbook.ru' }, orders: { count: 2, siteUrl: 'https://goods.ezbook.ru' } })
    renderIt()
    const link = await screen.findByRole('link', { name: 'goods.ezbook.ru' })
    expect(link).toHaveAttribute('href', 'https://goods.ezbook.ru')
  })
  it('renders nothing without shops', async () => {
    getKindsSummary.mockResolvedValue({ services: { count: 1, siteUrl: 'a' }, orders: { count: 0, siteUrl: 'https://goods.ezbook.ru' } })
    renderIt()
    await waitFor(() => expect(getKindsSummary).toHaveBeenCalled())
    expect(screen.queryByTestId('goods-shops-notice')).toBeNull()
  })
  it('renders nothing when the request fails', async () => {
    failWith = new Error('network down')
    const qc = renderIt()
    await waitFor(() => expect(qc.getQueryState(['kinds-summary'])?.status).toBe('error'))
    expect(screen.queryByTestId('goods-shops-notice')).toBeNull()
  })
})
