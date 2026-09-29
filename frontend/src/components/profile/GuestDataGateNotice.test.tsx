import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { GuestDataGateNotice } from './GuestDataGateNotice'

const getText = vi.fn()

vi.mock('../../api/legal', () => ({
  legalApi: { getText: (...args: unknown[]) => getText(...args) },
}))

function renderNotice(phoneVerified: boolean, textKey?: 'GuestDataGateNotice' | 'GuestDataGateDeleteNotice') {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <GuestDataGateNotice phoneVerified={phoneVerified} textKey={textKey} />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

// ARCHITECTURE_CYCLE16.md §245.6: the ONLY allowed trigger is `phoneVerified === false` — never an
// inference from whether the export/response came back empty. These tests pin that contract down so a
// future edit can't quietly turn the component into the oracle §245.6 explicitly forbids.
describe('GuestDataGateNotice', () => {
  it('renders nothing when the phone is verified, regardless of legal text state', () => {
    getText.mockResolvedValueOnce({ contentHtml: '<p>ignored</p>' })
    const { container } = renderNotice(true)
    expect(container).toBeEmptyDOMElement()
  })

  it('shows the neutral fallback text and a link to /data-request when unverified and no legal key yet', async () => {
    getText.mockRejectedValueOnce(new Error('404'))
    renderNotice(false)
    expect(
      await screen.findByText(/Эти сведения доступны после подтверждения номера телефона/),
    ).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Оставить обращение' })).toHaveAttribute(
      'href',
      '/data-request',
    )
  })

  it('renders the legal-counsel text instead of the fallback once the key is published', async () => {
    getText.mockResolvedValueOnce({ contentHtml: '<p>Официальный текст юриста</p>' })
    renderNotice(false)
    expect(await screen.findByText('Официальный текст юриста')).toBeInTheDocument()
    expect(screen.queryByText(/Эти сведения доступны после подтверждения/)).not.toBeInTheDocument()
  })

  // Т20-08 (ARCHITECTURE_CYCLE20.md §411) — only the "Текст" section is shown; the служебная справка
  // paragraph authored alongside it in the manifest file must never leak into the UI.
  it('shows only the "Текст" section, not the служебная справка paragraph before it', async () => {
    getText.mockResolvedValueOnce({
      contentHtml: '<p><em>Служебная справка для команды: не показывать это.</em></p><h2>Текст</h2><p>Видимый текст для пользователя.</p>',
    })
    renderNotice(false)
    expect(await screen.findByText('Видимый текст для пользователя.')).toBeInTheDocument()
    expect(screen.queryByText(/Служебная справка/)).not.toBeInTheDocument()
  })

  it('reads GuestDataGateDeleteNotice on the delete screen, a DIFFERENT key from the export screen default', async () => {
    getText.mockResolvedValueOnce({ contentHtml: '<h2>Текст</h2><p>Текст экрана удаления.</p>' })
    renderNotice(false, 'GuestDataGateDeleteNotice')
    expect(await screen.findByText('Текст экрана удаления.')).toBeInTheDocument()
    expect(getText).toHaveBeenCalledWith('GuestDataGateDeleteNotice')
  })
})
