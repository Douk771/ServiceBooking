import { describe, it, expect, vi, beforeEach } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
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

function renderSection(props: Partial<React.ComponentProps<typeof CompanyPhotosSection>> = {}) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <CompanyPhotosSection companyId="co1" {...props} />
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

  it('salon (default) keeps the salon heading; shop kind uses shop heading and empty state', async () => {
    list.mockResolvedValue([])
    const { unmount } = renderSection()
    expect(await screen.findByText('Фотографии салона')).toBeInTheDocument()
    unmount()
    renderSection({ kind: 'shop' })
    expect(await screen.findByText('Фотографии магазина')).toBeInTheDocument()
    expect(await screen.findByText('В галерее магазина пока нет фотографий')).toBeInTheDocument()
  })

  it('calls onChanged after a successful removal', async () => {
    list.mockResolvedValue([photo()])
    remove.mockResolvedValue(undefined)
    const onChanged = vi.fn()
    renderSection({ onChanged })
    await userEvent.click(await screen.findByRole('button', { name: /Удалить/ }))
    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1))
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
    expect(screen.queryByRole('button', { name: 'Выбрать фото' })).not.toBeInTheDocument()
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

// ARCHITECTURE_CYCLE31.md §31.9 / §31.8 — multi-file upload queue and the tile panel.
describe('CompanyPhotosSection — цикл 31: мультизагрузка', () => {
  const img = (name: string, type = 'image/jpeg', size = 1000) => {
    const f = new File(['x'], name, { type })
    Object.defineProperty(f, 'size', { value: size })
    return f
  }
  const input = () => document.querySelector('input[type="file"]') as HTMLInputElement
  const deferred = <T,>() => {
    let resolve!: (v: T) => void
    let reject!: (e: unknown) => void
    const promise = new Promise<T>((res, rej) => {
      resolve = res
      reject = rej
    })
    return { promise, resolve, reject }
  }
  const axiosErr = (status?: number, data = '') => ({ response: status === undefined ? undefined : { status, data } })
  const gallery = (n: number) =>
    Array.from({ length: n }, (_, i) => photo({ id: `p${i}`, position: i, isCover: i === 0 }))

  it('the input accepts several files and points at the people notice', async () => {
    list.mockResolvedValue([])
    renderSection()
    await screen.findByText('0 / 10')
    expect(input()).toHaveAttribute('multiple')
    expect(input()).toHaveAttribute('aria-describedby', 'company-photo-people-notice')
    expect(screen.getByRole('button', { name: 'Выбрать фото' })).toBeInTheDocument()
  })

  it('uploads three files strictly one at a time, in selection order', async () => {
    list.mockResolvedValue([])
    const d = [deferred<CompanyPhoto>(), deferred<CompanyPhoto>(), deferred<CompanyPhoto>()]
    d.forEach((x) => upload.mockImplementationOnce(() => x.promise))
    renderSection()
    await screen.findByText('0 / 10')

    await userEvent.upload(input(), [img('a.jpg'), img('b.jpg'), img('c.jpg')])
    await waitFor(() => expect(upload).toHaveBeenCalledTimes(1))
    expect(upload.mock.calls[0][1].name).toBe('a.jpg')

    d[0].resolve(photo({ id: 'n0', position: 0 }))
    await waitFor(() => expect(upload).toHaveBeenCalledTimes(2))
    expect(upload.mock.calls[1][1].name).toBe('b.jpg')
    expect(screen.getByRole('status')).toHaveTextContent('Загружено 1 из 3')

    d[1].resolve(photo({ id: 'n1', position: 1, isCover: false }))
    await waitFor(() => expect(upload).toHaveBeenCalledTimes(3))
    d[2].resolve(photo({ id: 'n2', position: 2, isCover: false }))
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Загружено 3 из 3'))
  })

  it('dropping two files uploads both', async () => {
    list.mockResolvedValue([])
    upload.mockImplementation((_c: string, f: File) => Promise.resolve(photo({ id: f.name, position: f.name === 'a.jpg' ? 0 : 1 })))
    renderSection()
    await screen.findByText('0 / 10')
    const zone = screen.getByText('Перетащите фото сюда или').parentElement as HTMLElement
    fireEvent.drop(zone, { dataTransfer: { files: [img('a.jpg'), img('b.jpg')] } })
    await waitFor(() => expect(upload).toHaveBeenCalledTimes(2))
  })

  it('8 photos + 5 files: two uploads and one message naming the three that did not fit', async () => {
    list.mockResolvedValue(gallery(8))
    upload.mockImplementation((_c: string, f: File) => Promise.resolve(photo({ id: f.name, position: 8, isCover: false })))
    renderSection()
    await screen.findByText('8 / 10')
    await userEvent.upload(input(), ['a', 'b', 'c', 'd', 'e'].map((n) => img(`${n}.jpg`)))
    await waitFor(() => expect(upload).toHaveBeenCalledTimes(2))
    expect(await screen.findByText(/Не добавлено 3 фото: в галерее не больше 10 фото: c\.jpg, d\.jpg, e\.jpg/)).toBeInTheDocument()
  })

  it('a wrong type and an oversized file show an error at the file, are not sent, the rest uploads', async () => {
    list.mockResolvedValue([])
    upload.mockResolvedValue(photo({ id: 'ok', position: 0 }))
    renderSection()
    await screen.findByText('0 / 10')
    // userEvent.upload would drop the gif itself (it honours `accept`), so the change is fired directly.
    fireEvent.change(input(), { target: { files: [img('a.gif', 'image/gif'), img('big.jpg', 'image/jpeg', 6 * 1024 * 1024), img('ok.jpg')] } })
    await waitFor(() => expect(upload).toHaveBeenCalled())
    expect(upload.mock.calls.every((c) => c[1].name === 'ok.jpg')).toBe(true)
    expect(await screen.findByText(/Формат не поддерживается/)).toBeInTheDocument()
    expect(screen.getByText(/Файл больше 5 МБ/)).toBeInTheDocument()
  })

  it('a 400 on the second file does not stop the third; the mapped text is shown at that file', async () => {
    list.mockResolvedValue([])
    upload
      .mockResolvedValueOnce(photo({ id: 'n0', position: 0 }))
      .mockRejectedValueOnce(axiosErr(400, 'Можно загрузить JPEG'))
      .mockResolvedValueOnce(photo({ id: 'n2', position: 1, isCover: false }))
    renderSection()
    await screen.findByText('0 / 10')
    await userEvent.upload(input(), [img('a.jpg'), img('b.jpg'), img('c.jpg')])
    await waitFor(() => expect(upload).toHaveBeenCalledTimes(3))
    expect(await screen.findByText(/ошибка: Поддерживаются только JPEG, PNG и WEBP/)).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Загружено 2 из 3')
    expect(screen.queryByRole('button', { name: 'Повторить неудавшиеся' })).not.toBeInTheDocument()
  })

  it('a repeated answer (same id) does not add a second tile', async () => {
    list.mockResolvedValue([photo({ id: 'p1', position: 0 })])
    upload.mockResolvedValue(photo({ id: 'p1', position: 0 }))
    renderSection()
    await screen.findByText('1 / 10')
    await userEvent.upload(input(), [img('a.jpg')])
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Загружено 1 из 1'))
    expect(screen.getByText('1 / 10')).toBeInTheDocument()
  })

  it('during a batch arrows, cover, delete and the picker are disabled', async () => {
    list.mockResolvedValue([photo({ id: 'p1', position: 0 }), photo({ id: 'p2', position: 1, isCover: false })])
    const d = deferred<CompanyPhoto>()
    upload.mockReturnValue(d.promise)
    renderSection()
    await screen.findByText('2 / 10')
    await userEvent.upload(input(), [img('a.jpg')])
    await waitFor(() => expect(upload).toHaveBeenCalled())
    for (const b of [...screen.getAllByLabelText('Переместить правее'), ...screen.getAllByLabelText('Переместить левее'), ...screen.getAllByLabelText('Удалить фото'), screen.getByLabelText('Сделать обложкой')]) {
      expect(b).toBeDisabled()
    }
    expect(screen.getByRole('button', { name: /Выбрать фото/ })).toBeDisabled()
    d.resolve(photo({ id: 'n', position: 2, isCover: false }))
    await waitFor(() => expect(screen.getAllByLabelText('Удалить фото')[0]).not.toBeDisabled())
  })

  it('"Повторить неудавшиеся" retries only the transient failure (429), not a 403', async () => {
    list.mockResolvedValue([])
    upload
      .mockRejectedValueOnce(axiosErr(429, 'Too many uploads'))
      .mockRejectedValueOnce(axiosErr(403))
      .mockResolvedValueOnce(photo({ id: 'n0', position: 0 }))
    renderSection()
    await screen.findByText('0 / 10')
    await userEvent.upload(input(), [img('a.jpg'), img('b.jpg')])
    const retry = await screen.findByRole('button', { name: 'Повторить неудавшиеся' })
    await userEvent.click(retry)
    await waitFor(() => expect(upload).toHaveBeenCalledTimes(3))
    expect(upload.mock.calls[2][1].name).toBe('a.jpg')
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Загружено 1 из 2'))
    expect(screen.queryByRole('button', { name: 'Повторить неудавшиеся' })).not.toBeInTheDocument()
  })

  it('"Скрыть" clears the status panel', async () => {
    list.mockResolvedValue([])
    upload.mockResolvedValue(photo({ id: 'n0', position: 0 }))
    renderSection()
    await screen.findByText('0 / 10')
    await userEvent.upload(input(), [img('a.jpg')])
    await userEvent.click(await screen.findByRole('button', { name: 'Скрыть' }))
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })
})

// §31.8 / US-31-03 / R-6 — no browser e2e: a guard that the tile panel keeps the classes making it reachable without a mouse.
describe('CompanyPhotosSection — цикл 31: плитка', () => {
  it('cover button has an accessible name and the panel is visible on focus/touch', async () => {
    list.mockResolvedValue([photo({ id: 'p1', position: 0 }), photo({ id: 'p2', position: 1, isCover: false })])
    renderSection()
    const cover = await screen.findByRole('button', { name: 'Сделать обложкой' })
    const panel = cover.parentElement as HTMLElement
    expect(panel.className).toContain('opacity-100')
    expect(panel.className).toContain('group-focus-within:opacity-100')
    expect(panel.className).toContain('[@media(hover:hover)_and_(pointer:fine)]:opacity-0')
    expect(panel.parentElement?.parentElement?.className).toContain('grid-cols-2')
  })
})
