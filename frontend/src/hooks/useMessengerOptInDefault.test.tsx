import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, act, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { useMessengerOptInDefault } from './useMessengerOptInDefault'

const getPreferences = vi.fn()
vi.mock('../api/notifications', () => ({ notificationsApi: { getPreferences: (...a: unknown[]) => getPreferences(...a) } }))

function Wrapper({ children }: { children: ReactNode }) {
  const [qc] = useState(() => new QueryClient({ defaultOptions: { queries: { retry: false } } }))
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>
}
const wrapper = Wrapper

beforeEach(() => getPreferences.mockReset())

describe('useMessengerOptInDefault', () => {
  it('guest: unchecked and the profile is never requested', () => {
    const { result } = renderHook(() => useMessengerOptInDefault(false), { wrapper })
    expect(result.current.checked).toBe(false)
    expect(result.current.payload(true)).toBe(false)
    expect(getPreferences).not.toHaveBeenCalled()
  })

  it('signed in with enabled + providerDeliveryConsent: pre-ticked once the profile arrives', async () => {
    getPreferences.mockResolvedValue({ enabled: true, providerDeliveryConsent: true })
    const { result } = renderHook(() => useMessengerOptInDefault(true), { wrapper })
    expect(result.current.loading).toBe(true)
    await waitFor(() => expect(result.current.checked).toBe(true))
    expect(result.current.payload(true)).toBe(true)
  })

  it('the person\'s own choice survives the profile loading afterwards', async () => {
    getPreferences.mockResolvedValue({ enabled: true, providerDeliveryConsent: true })
    const { result } = renderHook(() => useMessengerOptInDefault(true), { wrapper })
    act(() => result.current.setChecked(false))
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.checked).toBe(false)
  })

  it('opted out: no box, never ticked, field not sent', async () => {
    getPreferences.mockResolvedValue({ enabled: false, providerDeliveryConsent: true })
    const { result } = renderHook(() => useMessengerOptInDefault(true), { wrapper })
    await waitFor(() => expect(result.current.optedOut).toBe(true))
    act(() => result.current.setChecked(true))
    expect(result.current.checked).toBe(false)
    expect(result.current.payload(true)).toBeUndefined()
  })

  it('an old server without providerDeliveryConsent leaves the safe default: unchecked', async () => {
    getPreferences.mockResolvedValue({ enabled: true })
    const { result } = renderHook(() => useMessengerOptInDefault(true), { wrapper })
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.checked).toBe(false)
    expect(result.current.optedOut).toBe(false)
  })

  it('not offered: nothing to send even if ticked', async () => {
    const { result } = renderHook(() => useMessengerOptInDefault(false), { wrapper })
    act(() => result.current.setChecked(true))
    expect(result.current.payload(false)).toBeUndefined()
  })
})
