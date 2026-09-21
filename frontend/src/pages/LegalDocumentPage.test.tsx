import { describe, it, expect, vi, beforeEach } from 'vitest'
import type { ReactElement } from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { LegalDocumentPage } from './LegalDocumentPage'
import type { LegalDocument } from '../types'

const getDocument = vi.fn()

vi.mock('../api/legal', () => ({
  legalApi: {
    getDocument: (...args: unknown[]) => getDocument(...args),
  },
}))

beforeEach(() => {
  getDocument.mockReset()
})

function renderWithClient(ui: ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>)
}

function makeDocument(overrides: Partial<LegalDocument> = {}): LegalDocument {
  return {
    type: 'Privacy',
    title: 'Политика обработки персональных данных',
    version: '2026-09-15-draft',
    effectiveFrom: '2026-09-15',
    isDraft: true,
    changeKind: 'Material',
    gate: 'Global',
    url: '/privacy',
    contentHtml: '<p>Документ подготовлен командой сервиса.</p><h2>1. Общие положения</h2><p>Текст.</p>',
    ...overrides,
  }
}

describe('LegalDocumentPage', () => {
  it('renders the title, version and effective date', async () => {
    getDocument.mockResolvedValueOnce(makeDocument())
    renderWithClient(<LegalDocumentPage type="Privacy" />)

    expect(await screen.findByRole('heading', { name: 'Политика обработки персональных данных' })).toBeInTheDocument()
    expect(screen.getByText(/Версия 2026-09-15-draft/)).toBeInTheDocument()
  })

  it('shows the draft banner when isDraft is true', async () => {
    getDocument.mockResolvedValueOnce(makeDocument({ isDraft: true }))
    renderWithClient(<LegalDocumentPage type="Privacy" />)

    expect(await screen.findByText('Черновая редакция.')).toBeInTheDocument()
  })

  it('does not show the draft banner when isDraft is false', async () => {
    getDocument.mockResolvedValueOnce(makeDocument({ isDraft: false }))
    renderWithClient(<LegalDocumentPage type="TermsClient" />)

    await screen.findByRole('heading', { name: 'Политика обработки персональных данных' })
    expect(screen.queryByText(/Черновая редакция\./)).not.toBeInTheDocument()
  })

  it('shows an error state with a retry button on failure, not a blank page', async () => {
    getDocument.mockRejectedValueOnce({
      isAxiosError: true,
      response: { status: 503, data: 'Правовые документы временно недоступны.' },
    })
    renderWithClient(<LegalDocumentPage type="Privacy" />)

    expect(await screen.findByText('Правовые документы временно недоступны.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Попробовать снова' })).toBeInTheDocument()
  })

  it('retry button re-triggers the fetch', async () => {
    getDocument.mockRejectedValueOnce({ isAxiosError: true, response: { status: 503, data: 'X' } })
    getDocument.mockResolvedValueOnce(makeDocument())
    renderWithClient(<LegalDocumentPage type="Privacy" />)

    const retry = await screen.findByRole('button', { name: 'Попробовать снова' })
    retry.click()

    await waitFor(() => expect(getDocument).toHaveBeenCalledTimes(2))
  })

  // §43 — `/offer-channel` redirects to `/terms-owner#offer-channel`; the offer text is an appendix
  // inside the TermsOwner document, so once the fetched content lands, the page must scroll to the
  // anchor itself (the browser's own hash-scroll already happened, before there was anything to find).
  it('scrolls to the URL hash once the content carrying that anchor has loaded', async () => {
    window.history.pushState({}, '', '/terms-owner#offer-channel')
    getDocument.mockResolvedValueOnce(
      makeDocument({
        type: 'TermsOwner',
        title: 'Соглашение с компанией',
        url: '/terms-owner',
        contentHtml: '<p>Вступление.</p><h2 id="offer-channel">Оферта на подключение канала</h2><p>Текст оферты.</p>',
      }),
    )
    const scrollIntoView = vi.fn()
    const original = HTMLElement.prototype.scrollIntoView
    HTMLElement.prototype.scrollIntoView = scrollIntoView

    renderWithClient(<LegalDocumentPage type="TermsOwner" />)

    await screen.findByRole('heading', { name: 'Соглашение с компанией' })
    await waitFor(() => expect(scrollIntoView).toHaveBeenCalled())

    HTMLElement.prototype.scrollIntoView = original
    window.history.pushState({}, '', '/')
  })
})
