import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { LegalGuard } from './LegalGuard'
import { useAuthStore } from '../../store/authStore'

// The banner itself is covered by PlatformNoticeBanner.test.tsx — here only "is it mounted".
vi.mock('./PlatformNoticeBanner', () => ({ PlatformNoticeBanner: () => <div>platform-notices</div> }))
vi.mock('../../api/legal', () => ({
  legalApi: { getConsentStatus: vi.fn().mockResolvedValue({ requiresAcceptance: false, showBanner: false }) },
}))

function renderGuard(props: { showPlatformNotices?: boolean }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <LegalGuard bypassPaths={[]} {...props}>
          <div>app</div>
        </LegalGuard>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('LegalGuard — cycle 20 platform notices (ezbook only)', () => {
  beforeEach(() => {
    useAuthStore.setState({ token: null })
  })

  it('mounts PlatformNoticeBanner for an authenticated caller when showPlatformNotices is set (ezbook)', async () => {
    useAuthStore.setState({ token: 't' })
    renderGuard({ showPlatformNotices: true })
    await waitFor(() => expect(screen.getByText('app')).toBeInTheDocument())
    expect(screen.getByText('platform-notices')).toBeInTheDocument()
  })

  it('does not mount it without the prop (goods)', async () => {
    useAuthStore.setState({ token: 't' })
    renderGuard({})
    await waitFor(() => expect(screen.getByText('app')).toBeInTheDocument())
    expect(screen.queryByText('platform-notices')).not.toBeInTheDocument()
  })

  it('does not mount it for an anonymous caller even on ezbook', () => {
    renderGuard({ showPlatformNotices: true })
    expect(screen.getByText('app')).toBeInTheDocument()
    expect(screen.queryByText('platform-notices')).not.toBeInTheDocument()
  })
})
