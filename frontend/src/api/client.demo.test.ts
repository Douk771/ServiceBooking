// @vitest-environment node
import { describe, it, expect, beforeEach } from 'vitest'
import { AxiosError, type InternalAxiosRequestConfig } from 'axios'
import { api } from './client'
import { useDemoStore } from '../store/demoStore'

// API_CONTRACT_CYCLE28.md §600a — the interceptor side of the demo reset: 503 + X-Demo-Resetting flips the store flag,
// an ordinary 503 does not. Only the adapter is faked (no network); the real interceptor chain runs.

function failWith(status: number, headers: Record<string, string>) {
  return (config: InternalAxiosRequestConfig) =>
    Promise.reject(
      new AxiosError('fail', String(status), config, undefined, {
        status,
        statusText: '',
        data: 'text',
        headers,
        config,
      }),
    )
}

beforeEach(() => {
  useDemoStore.setState({ resetting: false })
})

describe('api/client — demo reset interceptor', () => {
  it('503 + X-Demo-Resetting sets the maintenance flag and still rejects', async () => {
    await expect(
      api.get('/companies', { adapter: failWith(503, { 'x-demo-resetting': '1', 'retry-after': '60' }) }),
    ).rejects.toBeInstanceOf(AxiosError)
    expect(useDemoStore.getState().resetting).toBe(true)
  })

  it('an ordinary 503 leaves the flag alone', async () => {
    await expect(api.get('/companies', { adapter: failWith(503, {}) })).rejects.toBeInstanceOf(AxiosError)
    expect(useDemoStore.getState().resetting).toBe(false)
  })

  it('a 403 + X-Demo-Restricted is passed through untouched (the form shows its body)', async () => {
    const e = await api
      .post('/profile/change-password', {}, { adapter: failWith(403, { 'x-demo-restricted': '1' }) })
      .catch((x) => x)
    expect((e as AxiosError).response?.status).toBe(403)
    expect(useDemoStore.getState().resetting).toBe(false)
  })
})
