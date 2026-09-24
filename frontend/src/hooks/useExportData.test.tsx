import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, act, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { useExportData } from './useExportData'

const exportData = vi.fn()

vi.mock('../api/profile', () => ({
  profileApi: { exportData: (...args: unknown[]) => exportData(...args) },
}))

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>
}

function jsonBlob(obj: unknown) {
  return new Blob([JSON.stringify(obj)], { type: 'application/json' })
}

describe('useExportData', () => {
  beforeEach(() => {
    exportData.mockReset()
    // jsdom doesn't implement these — the hook needs them to trigger the download.
    URL.createObjectURL = vi.fn(() => 'blob:mock')
    URL.revokeObjectURL = vi.fn()
  })

  it('downloads and leaves gateNotice null when the export has no guestDataGate section (pre-cycle-16 shape)', async () => {
    exportData.mockResolvedValue(jsonBlob({ profile: {} }))
    const { result } = renderHook(() => useExportData(), { wrapper })

    await act(async () => {
      await result.current.exportMut.mutateAsync()
    })

    expect(result.current.gateNotice).toBeNull()
    expect(result.current.exportError).toBe('')
  })

  it('downloads and leaves gateNotice null when guestDataGate.applied is false (§273.3 regression)', async () => {
    exportData.mockResolvedValue(
      jsonBlob({ profile: {}, guestDataGate: { applied: false, reason: null, explanation: null, subjectRequestPath: null } }),
    )
    const { result } = renderHook(() => useExportData(), { wrapper })

    await act(async () => {
      await result.current.exportMut.mutateAsync()
    })

    expect(result.current.gateNotice).toBeNull()
  })

  it('surfaces the server-written explanation when guestDataGate.applied is true (§273.1)', async () => {
    exportData.mockResolvedValue(
      jsonBlob({
        profile: {},
        guestDataGate: {
          applied: true,
          reason: 'PhoneNotVerified',
          explanation: 'Часть данных не попала в файл — номер не подтверждён.',
          subjectRequestPath: '/subject-request',
        },
      }),
    )
    const { result } = renderHook(() => useExportData(), { wrapper })

    await act(async () => {
      await result.current.exportMut.mutateAsync()
    })

    // `onSuccess` reads the blob's text asynchronously, which settles after `mutateAsync`'s own
    // promise — the state update lands on a later microtask.
    await waitFor(() => expect(result.current.gateNotice).toBe('Часть данных не попала в файл — номер не подтверждён.'))
  })

  it('applied true with explanation null still flips the notice on, just with no text to show (schema allows it)', async () => {
    exportData.mockResolvedValue(
      jsonBlob({ guestDataGate: { applied: true, reason: 'PhoneNotVerified', explanation: null, subjectRequestPath: '/subject-request' } }),
    )
    const { result } = renderHook(() => useExportData(), { wrapper })

    await act(async () => {
      await result.current.exportMut.mutateAsync()
    })

    expect(result.current.gateNotice).toBeNull()
  })

  it('a malformed body does not block the download or set an error — the file itself still downloads', async () => {
    exportData.mockResolvedValue(new Blob(['not json'], { type: 'application/json' }))
    const { result } = renderHook(() => useExportData(), { wrapper })

    await act(async () => {
      await result.current.exportMut.mutateAsync()
    })

    expect(result.current.gateNotice).toBeNull()
    expect(result.current.exportError).toBe('')
    expect(URL.createObjectURL).toHaveBeenCalled()
  })

  it('resets gateNotice on a fresh attempt (onMutate)', async () => {
    exportData.mockResolvedValueOnce(
      jsonBlob({ guestDataGate: { applied: true, reason: 'PhoneNotVerified', explanation: 'Текст.', subjectRequestPath: '/subject-request' } }),
    )
    const { result } = renderHook(() => useExportData(), { wrapper })

    await act(async () => {
      await result.current.exportMut.mutateAsync()
    })
    await waitFor(() => expect(result.current.gateNotice).toBe('Текст.'))

    exportData.mockResolvedValueOnce(jsonBlob({ profile: {} }))
    await act(async () => {
      await result.current.exportMut.mutateAsync()
    })
    expect(result.current.gateNotice).toBeNull()
  })
})
