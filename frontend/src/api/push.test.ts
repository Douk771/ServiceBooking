// @vitest-environment node
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { api } from './client'
import { pushApi } from './push'

vi.mock('./client', () => ({ api: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() } }))

beforeEach(() => {
  vi.mocked(api.get).mockReset().mockResolvedValue({ data: { items: [] } })
  vi.mocked(api.post).mockReset().mockResolvedValue({ data: {} })
})

describe('pushApi — site (cycle 24, API_CONTRACT_CYCLE24.md §484)', () => {
  it('ezbook sends no `site` anywhere: the requests are the pre-cycle-24 ones', async () => {
    await pushApi.getConfig()
    expect(api.get).toHaveBeenLastCalledWith('/push/config', { params: undefined })
    await pushApi.listSubscriptions('https://push/x')
    expect(api.get).toHaveBeenLastCalledWith('/push/subscriptions', { params: { currentEndpoint: 'https://push/x' } })
    await pushApi.listSubscriptions()
    expect(api.get).toHaveBeenLastCalledWith('/push/subscriptions', { params: undefined })
    await pushApi.subscribe({ endpoint: 'e', keys: { p256dh: 'p', auth: 'a' }, deviceLabel: 'd' })
    expect(api.post).toHaveBeenLastCalledWith('/push/subscriptions', { endpoint: 'e', keys: { p256dh: 'p', auth: 'a' }, deviceLabel: 'd' })
  })

  it('goods sends site=Orders on config, list and subscribe', async () => {
    await pushApi.getConfig('Orders')
    expect(api.get).toHaveBeenLastCalledWith('/push/config', { params: { site: 'Orders' } })
    await pushApi.listSubscriptions('https://push/x', 'Orders')
    expect(api.get).toHaveBeenLastCalledWith('/push/subscriptions', { params: { currentEndpoint: 'https://push/x', site: 'Orders' } })
    await pushApi.listSubscriptions(undefined, 'Orders')
    expect(api.get).toHaveBeenLastCalledWith('/push/subscriptions', { params: { site: 'Orders' } })
    await pushApi.subscribe({ endpoint: 'e', keys: { p256dh: 'p', auth: 'a' }, site: 'Orders' })
    expect(api.post).toHaveBeenLastCalledWith('/push/subscriptions', expect.objectContaining({ site: 'Orders' }))
  })
})
