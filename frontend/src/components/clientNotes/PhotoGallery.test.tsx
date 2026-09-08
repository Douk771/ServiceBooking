import { describe, it, expect, vi, beforeEach } from 'vitest'
import type { ReactElement } from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { PhotoGallery } from './PhotoGallery'
import type { ClientNote, ClientNotePhoto } from '../../api/masters'

vi.mock('../../api/clientNotes', () => ({
  clientNotesApi: {
    getPhotoBlob: vi.fn(() => Promise.resolve(new Blob(['x'], { type: 'image/jpeg' }))),
  },
}))

// jsdom implements neither IntersectionObserver nor the object-URL helpers that AuthedImage's lazy
// loading (useAuthedImage) depends on — stub both so photos actually attempt to load in tests instead
// of sitting forever in the "not yet visible" state.
class FakeIntersectionObserver implements IntersectionObserver {
  readonly root = null
  readonly rootMargin = ''
  readonly thresholds: ReadonlyArray<number> = []
  constructor(private callback: IntersectionObserverCallback) {}
  observe(target: Element) {
    this.callback([{ isIntersecting: true, target } as IntersectionObserverEntry], this)
  }
  disconnect() {}
  unobserve() {}
  takeRecords(): IntersectionObserverEntry[] {
    return []
  }
}

beforeEach(() => {
  vi.stubGlobal('IntersectionObserver', FakeIntersectionObserver)
  vi.stubGlobal('URL', { ...URL, createObjectURL: vi.fn(() => 'blob:mock'), revokeObjectURL: vi.fn() })
})

function renderWithClient(ui: ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>)
}

function makeNote(overrides: Partial<ClientNote> = {}): ClientNote {
  return {
    id: 'note-1',
    note: 'Покрасили корни',
    createdAt: '2026-03-12T10:00:00Z',
    authorId: 'u1',
    authorName: 'Ирина К.',
    bookingId: 'b1',
    bookingDate: '2026-03-12',
    bookingServiceName: 'Стрижка',
    canDelete: true,
    photos: [],
    ...overrides,
  }
}

function makePhoto(id: string): ClientNotePhoto {
  return {
    id,
    url: `/api/client-notes/photos/${id}`,
    thumbnailUrl: `/api/client-notes/photos/${id}/thumb`,
    width: 800,
    height: 600,
    sizeBytes: 12345,
    createdAt: '2026-03-12T10:00:00Z',
    uploadedByName: 'Ирина К.',
    canDelete: true,
  }
}

describe('PhotoGallery', () => {
  it('renders nothing for an empty photo list', () => {
    const { container } = renderWithClient(<PhotoGallery note={makeNote({ photos: [] })} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('renders one thumbnail button per photo with a meaningful alt-derived label', () => {
    const note = makeNote({ photos: [makePhoto('p1'), makePhoto('p2')] })
    renderWithClient(<PhotoGallery note={note} />)
    const buttons = screen.getAllByRole('button', { name: /Открыть фото: Фото работы, 12 марта, стрижка/i })
    expect(buttons).toHaveLength(2)
  })

  it('clicking a thumbnail opens the viewer modal', async () => {
    const user = userEvent.setup()
    const note = makeNote({ photos: [makePhoto('p1')] })
    renderWithClient(<PhotoGallery note={note} />)
    await user.click(screen.getByRole('button', { name: /Открыть фото/i }))
    expect(screen.getByRole('button', { name: 'Закрыть просмотр фото' })).toBeInTheDocument()
  })

  it('closes the viewer on Escape', async () => {
    const user = userEvent.setup()
    const note = makeNote({ photos: [makePhoto('p1')] })
    renderWithClient(<PhotoGallery note={note} />)
    await user.click(screen.getByRole('button', { name: /Открыть фото/i }))
    expect(screen.getByRole('button', { name: 'Закрыть просмотр фото' })).toBeInTheDocument()

    await user.keyboard('{Escape}')

    await waitFor(() => {
      expect(screen.queryByRole('button', { name: 'Закрыть просмотр фото' })).not.toBeInTheDocument()
    })
  })

  it('does not throw when a photo has no associated booking (note date/service fallback)', () => {
    const note = makeNote({ photos: [makePhoto('p1')], bookingDate: null, bookingServiceName: null })
    expect(() => renderWithClient(<PhotoGallery note={note} />)).not.toThrow()
  })
})
