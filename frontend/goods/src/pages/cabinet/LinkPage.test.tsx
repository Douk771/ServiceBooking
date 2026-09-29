import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom'
import { LinkPage } from './LinkPage'
import type { ShopManageDto } from '../../types'

const qr = vi.fn()
const slugCheck = vi.fn()
const updateSlug = vi.fn()
vi.mock('../../api/shops', () => ({ shopsApi: { qr: (...a: unknown[]) => qr(...a), slugCheck: (...a: unknown[]) => slugCheck(...a), updateSlug: (...a: unknown[]) => updateSlug(...a) } }))

const shop = { id: 's1', name: 'Шаурма', slug: 'shaurma', publicUrl: 'https://goods.ezbook.ru/shaurma' } as ShopManageDto

function renderPage(isOwner = true) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/cabinet/s1/link']}>
        <Routes>
          <Route element={<Outlet context={{ shop, isOwner }} />}>
            <Route path="/cabinet/:shopId/link" element={<LinkPage />} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  qr.mockReset().mockResolvedValue(new Blob(['png'], { type: 'image/png' }))
  slugCheck.mockReset().mockResolvedValue({ slug: 'new-shop', available: true })
  updateSlug.mockReset()
  URL.createObjectURL = vi.fn(() => 'blob:qr')
  URL.revokeObjectURL = vi.fn()
})

describe('LinkPage', () => {
  it('shows the server-built link, a QR preview and a PNG download named after the address', async () => {
    renderPage()
    expect(screen.getByTestId('shop-public-url')).toHaveTextContent('https://goods.ezbook.ru/shaurma')
    expect(await screen.findByAltText('QR-код магазина Шаурма')).toHaveAttribute('src', 'blob:qr')
    expect(screen.getByRole('link', { name: /Скачать PNG/ })).toHaveAttribute('download', 'shaurma-qr.png')
  })

  it('copies the link', async () => {
    const user = userEvent.setup() // installs its own clipboard stub
    renderPage()
    await user.click(screen.getByRole('button', { name: /Скопировать/ }))
    expect(await navigator.clipboard.readText()).toBe('https://goods.ezbook.ru/shaurma')
    expect(await screen.findByText('Скопировано')).toBeInTheDocument()
  })

  it('has a retry when the QR cannot be loaded', async () => {
    qr.mockRejectedValue({ response: { status: 500, data: '' } })
    renderPage()
    // the query retries once (1 s) before it gives up
    expect(await screen.findByRole('button', { name: 'Повторить' }, { timeout: 3000 })).toBeInTheDocument()
  })

  it('warns that old links and printed QR codes stop working and asks to confirm before changing the address', async () => {
    updateSlug.mockResolvedValue({ ...shop, slug: 'new-shop', publicUrl: 'https://goods.ezbook.ru/new-shop' })
    const user = userEvent.setup()
    renderPage()
    await user.click(screen.getByRole('button', { name: 'Изменить адрес' }))
    expect(screen.getByText(/Старая ссылка и напечатанные QR-коды перестанут работать/)).toBeInTheDocument()
    const input = screen.getByLabelText('Новый адрес')
    await user.clear(input)
    await user.type(input, 'new-shop')
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Адрес свободен'))
    await user.click(screen.getAllByRole('button', { name: 'Изменить адрес' })[0])
    expect(updateSlug).not.toHaveBeenCalled() // still needs the confirmation dialog
    await user.click(screen.getByRole('button', { name: 'Да, изменить' }))
    await waitFor(() => expect(updateSlug).toHaveBeenCalledWith('s1', 'new-shop'))
  })

  it('does not offer changing the address to staff', () => {
    renderPage(false)
    expect(screen.queryByRole('button', { name: 'Изменить адрес' })).toBeNull()
  })
})
