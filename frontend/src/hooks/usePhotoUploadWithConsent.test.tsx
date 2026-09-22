import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { usePhotoUploadWithConsent } from './usePhotoUploadWithConsent'

const uploadPhoto = vi.fn()
const confirmPhotoConsent = vi.fn()
const getText = vi.fn()

vi.mock('../api/clientNotes', () => ({
  clientNotesApi: { uploadPhoto: (...args: unknown[]) => uploadPhoto(...args) },
}))
vi.mock('../api/clientConsents', () => ({
  clientConsentsApi: { confirmPhotoConsent: (...args: unknown[]) => confirmPhotoConsent(...args) },
}))
vi.mock('../api/legal', () => ({
  legalApi: { getText: (...args: unknown[]) => getText(...args) },
}))

function file(name = 'a.jpg') {
  return new File(['x'], name, { type: 'image/jpeg' })
}

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>
}

beforeEach(() => {
  uploadPhoto.mockReset()
  confirmPhotoConsent.mockReset()
  getText.mockReset()
  getText.mockResolvedValue({
    key: 'PhotoConsent',
    version: '2026-09-15',
    isDraft: true,
    contentHtml: '<p>Meta.</p><h2>Текст для клиента</h2><p>Мастер хочет сфотографировать выполненную работу.</p>',
  })
})

describe('usePhotoUploadWithConsent', () => {
  it('uploads files in order and calls onUploaded per success, no modal when nothing fails', async () => {
    const onUploaded = vi.fn()
    uploadPhoto.mockResolvedValue({ id: 'p' })
    const { result } = renderHook(() => usePhotoUploadWithConsent({ companyId: 'c1', clientKey: 'u1', onUploaded }), { wrapper })

    await act(async () => {
      await result.current.uploadSequentially('note1', [file('a.jpg'), file('b.jpg')])
    })

    expect(uploadPhoto).toHaveBeenNthCalledWith(1, 'note1', expect.objectContaining({ name: 'a.jpg' }))
    expect(uploadPhoto).toHaveBeenNthCalledWith(2, 'note1', expect.objectContaining({ name: 'b.jpg' }))
    expect(onUploaded).toHaveBeenCalledTimes(2)
    expect(result.current.consentModal).toBeNull()
  })

  it('a missing-consent error parks the REMAINING files (this one included), not just the failed one', async () => {
    uploadPhoto
      .mockResolvedValueOnce({ id: 'p1' }) // a.jpg succeeds
      .mockRejectedValueOnce({
        isAxiosError: true,
        response: { status: 400, data: 'Перед загрузкой фото нужно подтвердить согласие клиента на фотофиксацию.' },
      }) // b.jpg fails on consent
    const onUploaded = vi.fn()
    const { result } = renderHook(() => usePhotoUploadWithConsent({ companyId: 'c1', clientKey: 'u1', onUploaded }), { wrapper })

    await act(async () => {
      await result.current.uploadSequentially('note1', [file('a.jpg'), file('b.jpg'), file('c.jpg')])
    })

    expect(onUploaded).toHaveBeenCalledTimes(1) // only a.jpg
    expect(result.current.consentModal).not.toBeNull()
    // b.jpg and c.jpg have not been attempted yet
    expect(uploadPhoto).toHaveBeenCalledTimes(2)
  })

  it('without companyId/clientKey, a missing-consent error is treated as an ordinary failure (no modal)', async () => {
    uploadPhoto.mockRejectedValueOnce({
      isAxiosError: true,
      response: { status: 400, data: 'Перед загрузкой фото нужно подтвердить согласие клиента на фотофиксацию.' },
    })
    const { result } = renderHook(() => usePhotoUploadWithConsent({}), { wrapper })

    await act(async () => {
      await result.current.uploadSequentially('note1', [file()])
    })

    expect(result.current.consentModal).toBeNull()
    expect(result.current.error).toContain('Не удалось загрузить фото')
  })

  it('an ordinary (non-consent) upload error surfaces as `error`, without opening the modal', async () => {
    uploadPhoto.mockRejectedValueOnce({ isAxiosError: true, response: { status: 400, data: 'Image too large' } })
    const { result } = renderHook(() => usePhotoUploadWithConsent({ companyId: 'c1', clientKey: 'u1' }), { wrapper })

    await act(async () => {
      await result.current.uploadSequentially('note1', [file()])
    })

    expect(result.current.consentModal).toBeNull()
    expect(result.current.error).toBeTruthy()
  })
})
