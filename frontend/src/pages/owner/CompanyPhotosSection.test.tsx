import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CompanyPhotosSection } from './CompanyPhotosSection'
import type { CompanyPhoto } from '../../types'

const list = vi.fn()
const upload = vi.fn()
const remove = vi.fn()
const reorder = vi.fn()

vi.mock('../../api/companyPhotos', () => ({
  companyPhotosApi: {
    list: (...args: unknown[]) => list(...args),
    upload: (...args: unknown[]) => upload(...args),
    remove: (...args: unknown[]) => remove(...args),
    reorder: (...args: unknown[]) => reorder(...args),
  },
}))

function photo(overrides: Partial<CompanyPhoto> = {}): CompanyPhoto {
  return {
    id: 'p1',
    url: '/uploads/companies/p1.jpg',
    thumbnailUrl: '/uploads/companies/p1-thumb.jpg',
    width: 1600,
    height: 1067,
    position: 0,
    isCover: true,
    ...overrides,
  }
}

function renderSection() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <CompanyPhotosSection companyId="co1" />
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  list.mockReset()
  upload.mockReset()
  remove.mockReset()
  reorder.mockReset()
})

describe('CompanyPhotosSection — API_CONTRACT_CYCLE10.md §125–§128', () => {
  it('shows an empty state with no technical text when there are no photos', async () => {
    list.mockResolvedValue([])
    renderSection()

    expect(await screen.findByText('В галерее пока нет фотографий')).toBeInTheDocument()
    expect(screen.getByText('0 / 10')).toBeInTheDocument()
  })

  it('labels position 0 as the cover', async () => {
    list.mockResolvedValue([photo({ id: 'p1', position: 0 }), photo({ id: 'p2', position: 1, isCover: false })])
    renderSection()

    expect(await screen.findByText('Обложка')).toBeInTheDocument()
  })

  it('disables the upload drop zone once the gallery has 10 photos', async () => {
    list.mockResolvedValue(
      Array.from({ length: 10 }, (_, i) => photo({ id: `p${i}`, position: i, isCover: i === 0 })),
    )
    renderSection()

    await screen.findByText('10 / 10')
    expect(screen.queryByRole('button', { name: 'Выбрать файл' })).not.toBeInTheDocument()
    expect(screen.getByText(`Достигнут лимит в 10 фото`)).toBeInTheDocument()
  })

  it('swapping a photo one position right calls reorder with the full permutation, cover first', async () => {
    list.mockResolvedValue([photo({ id: 'p1', position: 0 }), photo({ id: 'p2', position: 1, isCover: false })])
    reorder.mockResolvedValue([])
    renderSection()

    await screen.findByText('Обложка')
    const rightButtons = screen.getAllByLabelText('Переместить правее')
    rightButtons[0].click()

    await waitFor(() => expect(reorder).toHaveBeenCalledWith('co1', ['p2', 'p1']))
  })
})
