import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import type { ReactElement } from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MasterClientsPage } from './MasterClientsPage'
import { useAuthStore } from '../store/authStore'
import type { MasterClient } from '../api/masters'
import type { Paged } from '../types'

const getClients = vi.fn()
const addNote = vi.fn()
const uploadPhoto = vi.fn()
const confirmPhotoConsent = vi.fn()

vi.mock('../api/masters', () => ({
  mastersApi: {
    getClients: (...args: unknown[]) => getClients(...args),
    addNote: (...args: unknown[]) => addNote(...args),
    deleteNote: vi.fn(),
  },
}))
// Used inside usePhotoUploadWithConsent (imported transitively via MasterClientsPage → the hook),
// not directly by this page — mocked here because Vitest intercepts by resolved module path, which
// is the same file regardless of which module does the importing.
vi.mock('../api/clientNotes', () => ({
  clientNotesApi: { uploadPhoto: (...args: unknown[]) => uploadPhoto(...args) },
}))
// Cycle 5 additions pull in the health-note block and its warning text on every render (T5-F5) —
// none of these tests expand a card or exercise that flow, but without a mock these queries would
// hit the network for real and fail noisily in the background.
vi.mock('../api/clientConsents', () => ({
  clientConsentsApi: {
    getHealthNote: vi.fn().mockResolvedValue({ value: null, consentRequired: true }),
    updateHealthNote: vi.fn(),
    deleteHealthNote: vi.fn(),
    confirmHealthConsent: vi.fn(),
    getPhotoConsent: vi.fn().mockResolvedValue({ granted: false, grantedAt: null, version: null, confirmedBy: null, textVersionOutdated: false, source: null }),
    confirmPhotoConsent: (...args: unknown[]) => confirmPhotoConsent(...args),
  },
}))
vi.mock('../api/legal', () => ({
  legalApi: {
    getText: vi.fn().mockImplementation((key: string) => {
      if (key === 'PhotoConsent')
        return Promise.resolve({
          key,
          version: '2026-09-15',
          isDraft: true,
          contentHtml: '<p>Meta.</p><h2>Текст для клиента</h2><p>Мастер хочет сфотографировать выполненную работу.</p>',
        })
      return Promise.resolve({ key, version: '2026-09-21', isDraft: true, contentHtml: '<p>Meta.</p>' })
    }),
  },
}))

function photo(id = 'p1') {
  return { id, url: '/x', thumbnailUrl: '/x', width: 1, height: 1, sizeBytes: 1, createdAt: '', uploadedByName: null, canDelete: true }
}

beforeEach(() => {
  getClients.mockReset()
  addNote.mockReset()
  uploadPhoto.mockReset()
  confirmPhotoConsent.mockReset()
  useAuthStore.setState({ user: null, token: null })
  // jsdom doesn't implement createObjectURL — NotePhotoUploader calls it to preview staged photos.
  vi.stubGlobal('URL', { ...URL, createObjectURL: vi.fn().mockReturnValue('blob:mock'), revokeObjectURL: vi.fn() })
})

afterEach(() => {
  vi.unstubAllGlobals()
})

function renderWithClient(ui: ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>)
}

function makeClient(overrides: Partial<MasterClient> = {}): MasterClient {
  return {
    clientId: 'c1',
    guestPhone: null,
    name: 'Анна Петрова',
    phone: '+79990000000',
    email: null,
    lastVisitDate: '2026-08-30',
    totalVisits: 2,
    notes: [],
    bookingSummaries: [],
    ...overrides,
  }
}

function page(items: MasterClient[], overrides: Partial<Paged<MasterClient>> = {}): Paged<MasterClient> {
  return { items, page: 1, pageSize: 20, total: items.length, hasNext: false, ...overrides }
}

describe('MasterClientsPage', () => {
  it('requests clients without a search param on first render', async () => {
    getClients.mockResolvedValueOnce(page([makeClient()]))
    renderWithClient(<MasterClientsPage companyId="co1" />)

    await screen.findByText('Анна Петрова')
    expect(getClients).toHaveBeenCalledWith('co1', 1, 20, '')
  })

  it('debounces typing before issuing a server search request', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const user = userEvent.setup({ delay: null, advanceTimers: vi.advanceTimersByTime })
    getClients.mockResolvedValue(page([makeClient()]))
    renderWithClient(<MasterClientsPage companyId="co1" />)

    await waitFor(() => expect(getClients).toHaveBeenCalledTimes(1))

    const input = screen.getByPlaceholderText('Поиск по имени или телефону…')
    await user.type(input, 'Ир')

    // Still just the initial request — debounce hasn't elapsed yet.
    expect(getClients).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(400)
    await waitFor(() => expect(getClients).toHaveBeenCalledTimes(2))
    expect(getClients).toHaveBeenLastCalledWith('co1', 1, 20, 'Ир')

    vi.useRealTimers()
  })

  it('resets to page 1 when the search term changes', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const user = userEvent.setup({ delay: null, advanceTimers: vi.advanceTimersByTime })
    getClients.mockResolvedValue(page(Array.from({ length: 20 }, (_, i) => makeClient({ clientId: `c${i}` }))))
    renderWithClient(<MasterClientsPage companyId="co1" />)

    await waitFor(() => expect(getClients).toHaveBeenCalledTimes(1))
    expect(getClients).toHaveBeenLastCalledWith('co1', 1, 20, '')

    const input = screen.getByPlaceholderText('Поиск по имени или телефону…')
    await user.type(input, 'Client on page 2')
    await vi.advanceTimersByTimeAsync(400)

    await waitFor(() => expect(getClients).toHaveBeenLastCalledWith('co1', 1, 20, 'Client on page 2'))

    vi.useRealTimers()
  })

  it('shows "not found" when a search yields no results, without hiding the count elsewhere', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const user = userEvent.setup({ delay: null, advanceTimers: vi.advanceTimersByTime })
    getClients.mockResolvedValueOnce(page([makeClient()]))
    renderWithClient(<MasterClientsPage companyId="co1" />)
    await screen.findByText('Анна Петрова')

    getClients.mockResolvedValueOnce(page([], { total: 0 }))
    const input = screen.getByPlaceholderText('Поиск по имени или телефону…')
    await user.type(input, 'Несуществующий')
    await vi.advanceTimersByTimeAsync(400)

    expect(await screen.findByText('Клиентов не найдено')).toBeInTheDocument()

    vi.useRealTimers()
  })

  it('shows the empty-state without a search hint when there is no search term', async () => {
    getClients.mockResolvedValueOnce(page([], { total: 0 }))
    renderWithClient(<MasterClientsPage companyId="co1" />)

    expect(await screen.findByText('У вас пока нет клиентов')).toBeInTheDocument()
    expect(screen.getByText('Здесь появятся клиенты после первых записей')).toBeInTheDocument()
  })

  it('shows an error state on API failure', async () => {
    getClients.mockRejectedValueOnce(new Error('network error'))
    renderWithClient(<MasterClientsPage companyId="co1" />)

    expect(await screen.findByText('Не удалось загрузить клиентов')).toBeInTheDocument()
  })

  // US-77 п. 4 — the health-note ("Противопоказания") block is shown to Master/CompanyOwner...
  it('expanding a client for a Master shows the "Противопоказания" block', async () => {
    const user = userEvent.setup()
    useAuthStore.setState({
      user: { id: 'm1', phone: '79990000001', firstName: 'М', lastName: 'М', roles: ['Master'] },
      token: 'tok',
    })
    getClients.mockResolvedValueOnce(page([makeClient()]))
    renderWithClient(<MasterClientsPage companyId="co1" />)

    await user.click(await screen.findByText('Анна Петрова'))
    expect(await screen.findByText('Противопоказания и особенности здоровья')).toBeInTheDocument()
  })

  // ...and hidden entirely — not shown empty, not 403 — for SuperAdmin (§45.1, checklist §53).
  it('expanding a client for SuperAdmin shows NO "Противопоказания" block at all', async () => {
    const user = userEvent.setup()
    useAuthStore.setState({
      user: { id: 'a1', phone: '79990000002', firstName: 'А', lastName: 'А', roles: ['SuperAdmin'] },
      token: 'tok',
    })
    getClients.mockResolvedValueOnce(page([makeClient()]))
    renderWithClient(<MasterClientsPage companyId="co1" />)

    await user.click(await screen.findByText('Анна Петрова'))
    await screen.findByText('История визитов')
    expect(screen.queryByText('Противопоказания и особенности здоровья')).not.toBeInTheDocument()
  })

  // T5-F6 follow-up — "add a note with photos already selected" used to show a generic
  // "не все фото удалось загрузить" instead of the same reactive consent form NotePhotoUploader shows
  // when attaching to an already-existing note.
  describe('creating a note together with staged photos', () => {
    async function expandAndStagePhoto(user: ReturnType<typeof userEvent.setup>) {
      // Not `...Once`: adding the note (and later, resuming the photo upload) both invalidate and
      // refetch this same query — it needs to keep resolving across every one of those refetches.
      getClients.mockResolvedValue(page([makeClient()]))
      renderWithClient(<MasterClientsPage companyId="co1" />)
      await user.click(await screen.findByText('Анна Петрова'))

      const galleryInput = screen.getByLabelText('Выбрать фото из галереи')
      await user.upload(galleryInput, new File(['x'], 'photo.jpg', { type: 'image/jpeg' }))
      expect(screen.getByAltText('Фото для загрузки 1')).toBeInTheDocument()

      await user.type(screen.getByLabelText('Добавить заметку'), 'Покрасили корни')
    }

    it('uploads staged photos to the freshly created note when there is no consent issue', async () => {
      const user = userEvent.setup()
      addNote.mockResolvedValueOnce({ id: 'note1', note: 'Покрасили корни', createdAt: '', authorId: '', authorName: '', bookingId: null, bookingDate: null, bookingServiceName: null, canDelete: true, photos: [] })
      uploadPhoto.mockResolvedValueOnce(photo())

      await expandAndStagePhoto(user)
      await user.click(screen.getByRole('button', { name: 'Добавить' }))

      await waitFor(() => expect(addNote).toHaveBeenCalled())
      await waitFor(() => expect(uploadPhoto).toHaveBeenCalledWith('note1', expect.any(File)))
    })

    it('a missing-consent error while uploading staged photos opens the SAME consent form NotePhotoUploader uses for an existing note — not a generic error', async () => {
      const user = userEvent.setup()
      addNote.mockResolvedValueOnce({ id: 'note1', note: 'Покрасили корни', createdAt: '', authorId: '', authorName: '', bookingId: null, bookingDate: null, bookingServiceName: null, canDelete: true, photos: [] })
      uploadPhoto.mockRejectedValueOnce({
        isAxiosError: true,
        response: { status: 400, data: 'Перед загрузкой фото нужно подтвердить согласие клиента на фотофиксацию.' },
      })

      await expandAndStagePhoto(user)
      await user.click(screen.getByRole('button', { name: 'Добавить' }))

      expect(await screen.findByText('Согласие на фотофиксацию')).toBeInTheDocument()
      expect(screen.queryByText(/не все фото удалось загрузить/)).not.toBeInTheDocument()
    })

    it('confirming consent resumes the upload automatically — the master never reselects the photo', async () => {
      const user = userEvent.setup()
      addNote.mockResolvedValueOnce({ id: 'note1', note: 'Покрасили корни', createdAt: '', authorId: '', authorName: '', bookingId: null, bookingDate: null, bookingServiceName: null, canDelete: true, photos: [] })
      uploadPhoto
        .mockRejectedValueOnce({
          isAxiosError: true,
          response: { status: 400, data: 'Перед загрузкой фото нужно подтвердить согласие клиента на фотофиксацию.' },
        })
        .mockResolvedValueOnce(photo())
      confirmPhotoConsent.mockResolvedValueOnce(undefined)

      await expandAndStagePhoto(user)
      await user.click(screen.getByRole('button', { name: 'Добавить' }))
      await screen.findByText('Согласие на фотофиксацию')

      // The note's own text field is already clear again (the note itself was saved) — the pending
      // photo is not sitting in that form any more, it lives inside the upload flow that is about to
      // resume, which is exactly the point: nothing here asks the master to pick the file again.
      await waitFor(() => expect(screen.getByLabelText('Добавить заметку')).toHaveValue(''))

      await user.click(screen.getByRole('button', { name: 'Клиент согласен' }))

      await waitFor(() => expect(confirmPhotoConsent).toHaveBeenCalledWith('co1', 'c1', '2026-09-15'))
      await waitFor(() => expect(uploadPhoto).toHaveBeenCalledTimes(2))
      expect(uploadPhoto).toHaveBeenLastCalledWith('note1', expect.any(File))
    })
  })
})
