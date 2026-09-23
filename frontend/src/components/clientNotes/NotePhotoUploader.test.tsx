import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { NotePhotoUploader } from './NotePhotoUploader'

const uploadPhoto = vi.fn()
const confirmPhotoConsent = vi.fn()
const getText = vi.fn()

vi.mock('../../api/clientNotes', () => ({
  clientNotesApi: { uploadPhoto: (...args: unknown[]) => uploadPhoto(...args) },
}))
vi.mock('../../api/clientConsents', () => ({
  clientConsentsApi: { confirmPhotoConsent: (...args: unknown[]) => confirmPhotoConsent(...args) },
}))
vi.mock('../../api/legal', () => ({
  legalApi: { getText: (...args: unknown[]) => getText(...args) },
}))

function file(name = 'photo.jpg') {
  return new File(['x'], name, { type: 'image/jpeg' })
}

function renderUploader(props: Partial<React.ComponentProps<typeof NotePhotoUploader>> = {}) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <NotePhotoUploader noteId="note1" remainingSlots={5} companyId="c1" clientKey="u1" {...props} />
    </QueryClientProvider>,
  )
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

describe('NotePhotoUploader — photo-consent gate (T5-F6)', () => {
  it('a missing-consent 400 opens the consent modal instead of a bare upload error', async () => {
    const user = userEvent.setup()
    uploadPhoto.mockRejectedValueOnce({
      isAxiosError: true,
      response: { status: 400, data: 'Перед загрузкой фото нужно подтвердить согласие клиента на фотофиксацию.' },
    })
    renderUploader()

    const input = document.querySelector('input[type="file"]:not([capture])') as HTMLInputElement
    await user.upload(input, file())

    expect(await screen.findByText('Согласие на фотофиксацию')).toBeInTheDocument()
    expect(screen.queryByText(/Не удалось загрузить фото/)).not.toBeInTheDocument()
  })

  it('confirming consent records it and automatically retries the upload', async () => {
    const user = userEvent.setup()
    uploadPhoto
      .mockRejectedValueOnce({
        isAxiosError: true,
        response: { status: 400, data: 'Перед загрузкой фото нужно подтвердить согласие клиента на фотофиксацию.' },
      })
      .mockResolvedValueOnce({ id: 'p1', url: '/x', thumbnailUrl: '/x', width: 1, height: 1, sizeBytes: 1, createdAt: '', uploadedByName: null, canDelete: true })
    confirmPhotoConsent.mockResolvedValueOnce(undefined)
    const onUploaded = vi.fn()
    renderUploader({ onUploaded })

    const input = document.querySelector('input[type="file"]:not([capture])') as HTMLInputElement
    await user.upload(input, file())
    await screen.findByText('Согласие на фотофиксацию')

    await user.click(screen.getByRole('button', { name: 'Клиент согласен' }))

    await waitFor(() => expect(confirmPhotoConsent).toHaveBeenCalledWith('c1', 'u1', '2026-09-15'))
    await waitFor(() => expect(uploadPhoto).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(onUploaded).toHaveBeenCalled())
  })

  it('an ordinary upload error (not a consent issue) shows the plain message, no modal', async () => {
    const user = userEvent.setup()
    uploadPhoto.mockRejectedValueOnce({
      isAxiosError: true,
      response: { status: 400, data: 'Слишком большой файл — максимум 5 МБ.' },
    })
    renderUploader()

    const input = document.querySelector('input[type="file"]:not([capture])') as HTMLInputElement
    await user.upload(input, file())

    expect(await screen.findByText(/Файл больше 5 МБ/)).toBeInTheDocument()
    expect(screen.queryByText('Согласие на фотофиксацию')).not.toBeInTheDocument()
  })

  it('without companyId/clientKey, a missing-consent 400 falls back to the plain error (no crash)', async () => {
    const user = userEvent.setup()
    uploadPhoto.mockRejectedValueOnce({
      isAxiosError: true,
      response: { status: 400, data: 'Перед загрузкой фото нужно подтвердить согласие клиента на фотофиксацию.' },
    })
    renderUploader({ companyId: undefined, clientKey: undefined })

    const input = document.querySelector('input[type="file"]:not([capture])') as HTMLInputElement
    await user.upload(input, file())

    expect(await screen.findByText(/Не удалось загрузить фото/)).toBeInTheDocument()
  })
})
