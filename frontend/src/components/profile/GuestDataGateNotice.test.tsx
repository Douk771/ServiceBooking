import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { GuestDataGateNotice } from './GuestDataGateNotice'

const getText = vi.fn()

vi.mock('../../api/legal', () => ({
  legalApi: { getText: (...args: unknown[]) => getText(...args) },
}))

function renderNotice(phoneVerified: boolean) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <GuestDataGateNotice phoneVerified={phoneVerified} />
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
})
