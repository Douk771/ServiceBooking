// @vitest-environment node
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { AxiosError } from 'axios'

const get = vi.fn()
const post = vi.fn()
vi.mock('./client', () => ({ api: { get: (...a: unknown[]) => get(...a), post: (...a: unknown[]) => post(...a) } }))

import { demoApi } from './demo'

const axiosError = (status: number) =>
  new AxiosError('x', String(status), undefined, undefined, {
    status,
    data: '',
    statusText: '',
    headers: {},
    config: {} as never,
  })

beforeEach(() => {
  get.mockReset()
  post.mockReset()
})

describe('demoApi.getStatus (API_CONTRACT_CYCLE28.md §597)', () => {
  it('200 → the status body', async () => {
    const dto = { demoMode: true, resetting: false, resetLocalTime: '04:00', timeZoneId: 'Europe/Moscow', roles: [] }
    get.mockResolvedValue({ data: dto })
    await expect(demoApi.getStatus()).resolves.toEqual(dto)
    expect(get).toHaveBeenCalledWith('/demo/status')
  })

  it('404 (production) → null, not an error', async () => {
    get.mockRejectedValue(axiosError(404))
    await expect(demoApi.getStatus()).resolves.toBeNull()
  })

  it('other failures still reject (a network blip must not look like "not a demo" forever)', async () => {
    get.mockRejectedValue(axiosError(500))
    await expect(demoApi.getStatus()).rejects.toBeInstanceOf(AxiosError)
    get.mockRejectedValue(new Error('network'))
    await expect(demoApi.getStatus()).rejects.toThrow('network')
  })
})

describe('demoApi.login (§598)', () => {
  it('posts the role and returns the auth response', async () => {
    post.mockResolvedValue({ data: { token: 't' } })
    await expect(demoApi.login('master')).resolves.toEqual({ token: 't' })
    expect(post).toHaveBeenCalledWith('/demo/login', { role: 'master' })
  })
})
