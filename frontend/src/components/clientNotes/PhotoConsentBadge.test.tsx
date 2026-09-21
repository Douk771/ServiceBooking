import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { PhotoConsentBadge } from './PhotoConsentBadge'

const getPhotoConsent = vi.fn()

vi.mock('../../api/clientConsents', () => ({
  clientConsentsApi: { getPhotoConsent: (...args: unknown[]) => getPhotoConsent(...args) },
}))

function renderBadge() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <PhotoConsentBadge companyId="c1" clientKey="u1" />
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  getPhotoConsent.mockReset()
})

describe('PhotoConsentBadge', () => {
  it('shows "клиент согласие дал" when granted and the text version is current', async () => {
    getPhotoConsent.mockResolvedValueOnce({
      granted: true,
      grantedAt: '2026-09-15T10:00:00Z',
      version: '2026-09-15',
      confirmedBy: 'Иванова М.',
      textVersionOutdated: false,
      source: 'PhotoForm',
    })
    renderBadge()

    expect(await screen.findByText(/клиент согласие дал/)).toBeInTheDocument()
  })

  it('treats granted + outdated text version the same as not granted (§44.1)', async () => {
    getPhotoConsent.mockResolvedValueOnce({
      granted: true,
      grantedAt: '2026-01-01T10:00:00Z',
      version: '2026-01-01',
      confirmedBy: 'Иванова М.',
      textVersionOutdated: true,
      source: 'PhotoForm',
    })
    renderBadge()

    expect(await screen.findByText('Фото: согласие клиента не получено')).toBeInTheDocument()
  })

  it('shows "согласие не получено" when nothing was ever granted', async () => {
    getPhotoConsent.mockResolvedValueOnce({
      granted: false,
      grantedAt: null,
      version: null,
      confirmedBy: null,
      textVersionOutdated: false,
      source: null,
    })
    renderBadge()

    expect(await screen.findByText('Фото: согласие клиента не получено')).toBeInTheDocument()
  })
})
