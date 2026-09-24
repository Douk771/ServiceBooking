import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, act, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { usePhoneVerification, isTerminalStatus } from './usePhoneVerification'
import type { PhoneVerificationSessionCreated, PhoneVerificationSessionStatus } from '../types'

const startSession = vi.fn()
const getStatus = vi.fn()
const cancelSession = vi.fn()

vi.mock('../api/phoneVerification', () => ({
  phoneVerificationApi: {
    startSession: (...args: unknown[]) => startSession(...args),
    getStatus: (...args: unknown[]) => getStatus(...args),
    cancelSession: (...args: unknown[]) => cancelSession(...args),
  },
}))

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>
}

function session(overrides: Partial<PhoneVerificationSessionCreated> = {}): PhoneVerificationSessionCreated {
  return {
    sessionId: 's1',
    statusToken: 'tok1',
    method: 'MaxBot',
    deepLink: 'https://max.ru/ezbookbot?start=v1.abc',
    qrPngBase64: null,
    phoneMasked: '+7 (900) ***-**-01',
    expiresAtUtc: '2026-09-24T12:34:56Z',
    ttlSeconds: 600,
    ...overrides,
  }
}

function status(overrides: Partial<PhoneVerificationSessionStatus> = {}): PhoneVerificationSessionStatus {
  return {
    sessionId: 's1',
    status: 'Pending',
    failureReason: null,
    message: null,
    phoneMasked: '+7 (900) ***-**-01',
    expiresAtUtc: '2026-09-24T12:34:56Z',
    verifiedAtUtc: null,
    ...overrides,
  }
}

beforeEach(() => {
  startSession.mockReset()
  getStatus.mockReset()
  cancelSession.mockReset()
})

describe('isTerminalStatus', () => {
  it('Pending/Linked keep polling, everything else is final (§148.4)', () => {
    expect(isTerminalStatus('Pending')).toBe(false)
    expect(isTerminalStatus('Linked')).toBe(false)
    expect(isTerminalStatus('Verified')).toBe(true)
    expect(isTerminalStatus('Rejected')).toBe(true)
    expect(isTerminalStatus('Cancelled')).toBe(true)
    expect(isTerminalStatus('Expired')).toBe(true)
    expect(isTerminalStatus(undefined)).toBe(false)
  })
})

describe('usePhoneVerification', () => {
  it('start() on success stores the session and starts polling its status', async () => {
    startSession.mockResolvedValue(session())
    getStatus.mockResolvedValue(status())
    const { result } = renderHook(() => usePhoneVerification(), { wrapper })

    await act(async () => {
      await result.current.start('79000000001')
    })

    expect(startSession).toHaveBeenCalledWith('79000000001')
    expect(result.current.session?.sessionId).toBe('s1')
    expect(result.current.startError).toBeNull()
    await waitFor(() => expect(getStatus).toHaveBeenCalledWith('s1', 'tok1'))
    expect(result.current.isPolling).toBe(true)
  })

  it('start() on failure surfaces a Russian message and leaves no session behind', async () => {
    startSession.mockRejectedValue({
      isAxiosError: true,
      response: { status: 409, data: 'Подтверждение телефона сейчас недоступно.' },
    })
    const { result } = renderHook(() => usePhoneVerification(), { wrapper })

    await act(async () => {
      await result.current.start('79000000001')
    })

    expect(result.current.session).toBeNull()
    expect(result.current.startError).toBe('Подтверждение телефона сейчас недоступно.')
    expect(getStatus).not.toHaveBeenCalled()
  })

  it('a Verified status stops isPolling without an explicit cancel', async () => {
    startSession.mockResolvedValue(session())
    getStatus.mockResolvedValue(status({ status: 'Verified', verifiedAtUtc: '2026-09-24T12:31:00Z' }))
    const { result } = renderHook(() => usePhoneVerification(), { wrapper })

    await act(async () => {
      await result.current.start()
    })

    await waitFor(() => expect(result.current.status?.status).toBe('Verified'))
    expect(result.current.isPolling).toBe(false)
  })

  it('cancel() clears local state immediately and best-effort calls the API', async () => {
    startSession.mockResolvedValue(session())
    getStatus.mockResolvedValue(status())
    cancelSession.mockResolvedValue(undefined)
    const { result } = renderHook(() => usePhoneVerification(), { wrapper })

    await act(async () => {
      await result.current.start()
    })
    await act(async () => {
      await result.current.cancel()
    })

    expect(cancelSession).toHaveBeenCalledWith('s1', 'tok1')
    expect(result.current.session).toBeNull()
    expect(result.current.isPolling).toBe(false)
  })

  it('cancel() never throws even if the network call fails (US-12-05 is fire-and-forget)', async () => {
    startSession.mockResolvedValue(session())
    getStatus.mockResolvedValue(status())
    cancelSession.mockRejectedValue(new Error('network down'))
    const { result } = renderHook(() => usePhoneVerification(), { wrapper })

    await act(async () => {
      await result.current.start()
    })

    await act(async () => {
      await result.current.cancel()
    })
    await waitFor(() => expect(result.current.session).toBeNull())
  })

  it('syncPhone cancels the session when the tracked phone no longer matches what it was started for (R8)', async () => {
    startSession.mockResolvedValue(session())
    getStatus.mockResolvedValue(status())
    cancelSession.mockResolvedValue(undefined)
    const { result } = renderHook(() => usePhoneVerification(), { wrapper })

    await act(async () => {
      await result.current.start('79000000001')
    })
    act(() => {
      result.current.syncPhone('79000000002') // user edited the number after requesting verification
    })

    await waitFor(() => expect(cancelSession).toHaveBeenCalledWith('s1', 'tok1'))
    await waitFor(() => expect(result.current.session).toBeNull())
  })

  it('a failed status poll (e.g. 404 — session expired server-side) stops polling and surfaces an error', async () => {
    startSession.mockResolvedValue(session())
    getStatus.mockRejectedValue({
      isAxiosError: true,
      response: { status: 404, data: '' },
    })
    const { result } = renderHook(() => usePhoneVerification(), { wrapper })

    await act(async () => {
      await result.current.start('79000000001')
    })

    await waitFor(() => expect(result.current.statusError).not.toBeNull())
    expect(result.current.isPolling).toBe(false)
    // Only ever called once — refetchInterval must not keep retrying on a hard error.
    expect(getStatus).toHaveBeenCalledTimes(1)
  })

  it('syncPhone is a no-op when the phone still matches the session', async () => {
    startSession.mockResolvedValue(session())
    getStatus.mockResolvedValue(status())
    const { result } = renderHook(() => usePhoneVerification(), { wrapper })

    await act(async () => {
      await result.current.start('79000000001')
    })
    act(() => {
      result.current.syncPhone('79000000001')
    })

    expect(cancelSession).not.toHaveBeenCalled()
    expect(result.current.session).not.toBeNull()
  })
})
