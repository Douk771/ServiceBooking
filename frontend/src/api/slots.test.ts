import { beforeEach, describe, expect, it, vi } from 'vitest'

const { get, post, put, del } = vi.hoisted(() => ({
  get: vi.fn(() => Promise.resolve({ data: {} })),
  post: vi.fn(() => Promise.resolve({ data: {} })),
  put: vi.fn(() => Promise.resolve({ data: {} })),
  del: vi.fn(() => Promise.resolve({ data: {} })),
}))
vi.mock('@/api/client', () => ({ api: { get, post, put, delete: del } }))

import { createSlotApi } from './slots'

beforeEach(() => vi.clearAllMocks())

describe.each([
  ['/stays' as const],
  ['/baths' as const],
])('createSlotApi(%s)', (prefix) => {
  const slot = createSlotApi(prefix)

  it('public routes sit under the prefix, slugs are encoded', async () => {
    await slot.publicServices.page('a b', 'sauna')
    expect(get).toHaveBeenCalledWith(`${prefix}/public/companies/a%20b/services/sauna`)
    await slot.publicServices.quote('s1', {} as never)
    expect(post).toHaveBeenCalledWith(`${prefix}/public/services/s1/quote`, {})
  })

  it('order routes use the token, encoded', async () => {
    await slot.orders.get('t/1')
    expect(get).toHaveBeenCalledWith(`${prefix}/service-orders/public/t%2F1`)
    await slot.orders.pushUnsubscribe('t', 'e')
    expect(post).toHaveBeenCalledWith(`${prefix}/service-orders/public/t/push-subscription/remove`, { endpoint: 'e' })
  })

  it('cabinet and session routes are company-scoped', async () => {
    await slot.cabinet.get('c1', 's1')
    expect(get).toHaveBeenCalledWith(`${prefix}/companies/c1/services/s1`)
    await slot.sessions.serviceDay('c1', '2026-10-09')
    expect(get).toHaveBeenCalledWith(`${prefix}/companies/c1/service-day`, { params: { date: '2026-10-09' } })
    await slot.sessions.cancelSession('c1', 'x', 3, 'why')
    expect(post).toHaveBeenCalledWith(`${prefix}/companies/c1/service-sessions/x/cancel`, { expectedVersion: 3, reason: 'why' })
  })
})
