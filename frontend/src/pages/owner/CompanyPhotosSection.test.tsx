import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CompanyPhotosSection } from './CompanyPhotosSection'
import { useAuthStore } from '../../store/authStore'
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

const getText = vi.fn()
vi.mock('../../api/legal', () => ({
  legalApi: { getText: (...args: unknown[]) => getText(...args) },
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
  getText.mockReset().mockResolvedValue({
    key: 'CompanyPhotoPeopleNotice',
    version: '2026-09-29-draft',
    isDraft: true,
    contentHtml: '<p>Meta.</p><h2>Текст</h2><p>Если на фото есть люди, загружайте только с их согласия.</p>',
  })
  useAuthStore.setState({ user: { id: 'o1', phone: '79990000000', firstName: 'В', lastName: 'В', roles: ['CompanyOwner'] }, token: 'tok' })
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

// Т20-07 п. 1/2 (US-20-06, US-20-07 typo guard — actually US-20-06/Т20-07, D2 цикла 10).
describe('CompanyPhotosSection — Т20-07 people-in-photo notice and SuperAdmin removal reason', () => {
  it('shows the "Текст" section of CompanyPhotoPeopleNotice before any file is picked', async () => {
    list.mockResolvedValue([])
    render(
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <CompanyPhotosSection companyId="co1" />
      </QueryClientProvider>,
    )

    expect(await screen.findByText(/только с их согласия/)).toBeInTheDocument()
  })

  it('owner clicking delete removes immediately, with no dialog and no reason param', async () => {
    const user = userEvent.setup()
    list.mockResolvedValue([photo({ id: 'p1', position: 0 })])
    remove.mockResolvedValue(undefined)
    render(
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <CompanyPhotosSection companyId="co1" />
      </QueryClientProvider>,
    )

    await user.click(await screen.findByLabelText('Удалить фото'))
    expect(screen.queryByText('Удалить фотографию')).not.toBeInTheDocument()
    await waitFor(() => expect(remove).toHaveBeenCalledWith('co1', 'p1', undefined))
  })

  it('SuperAdmin clicking delete opens a reason dialog; "по обращению" sends the reason param', async () => {
    const user = userEvent.setup()
    useAuthStore.setState({ user: { id: 'a1', phone: '79990000001', firstName: 'А', lastName: 'А', roles: ['SuperAdmin'] }, token: 'tok' })
    list.mockResolvedValue([photo({ id: 'p1', position: 0 })])
    remove.mockResolvedValue(undefined)
    render(
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <CompanyPhotosSection companyId="co1" />
      </QueryClientProvider>,
    )

    await user.click(await screen.findByLabelText('Удалить фото'))
    expect(await screen.findByText('Удалить фотографию')).toBeInTheDocument()
    expect(remove).not.toHaveBeenCalled()

    await user.click(screen.getByLabelText(/По обращению изображённого человека/))
    await user.click(screen.getByRole('button', { name: 'Удалить' }))

    await waitFor(() => expect(remove).toHaveBeenCalledWith('co1', 'p1', 'DepictedPersonRequest'))
  })

  it('SuperAdmin choosing "другая причина" (the default) sends no reason param', async () => {
    const user = userEvent.setup()
    useAuthStore.setState({ user: { id: 'a1', phone: '79990000001', firstName: 'А', lastName: 'А', roles: ['SuperAdmin'] }, token: 'tok' })
    list.mockResolvedValue([photo({ id: 'p1', position: 0 })])
    remove.mockResolvedValue(undefined)
    render(
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <CompanyPhotosSection companyId="co1" />
      </QueryClientProvider>,
    )

    await user.click(await screen.findByLabelText('Удалить фото'))
    await user.click(screen.getByRole('button', { name: 'Удалить' }))

    await waitFor(() => expect(remove).toHaveBeenCalledWith('co1', 'p1', undefined))
  })
})
