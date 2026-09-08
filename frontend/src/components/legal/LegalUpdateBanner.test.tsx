import { describe, it, expect, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { LegalUpdateBanner } from './LegalUpdateBanner'
import type { ConsentStatus } from '../../types'

function makeStatus(overrides: Partial<ConsentStatus> = {}): ConsentStatus {
  return {
    requiresAcceptance: false,
    showBanner: true,
    documents: [
      { type: 'Privacy', version: '2026-09-15', acceptedVersion: '2026-01-01', changeKind: 'Editorial' },
      { type: 'Terms', version: '2026-01-01', acceptedVersion: '2026-01-01', changeKind: 'Editorial' },
    ],
    ...overrides,
  }
}

beforeEach(() => {
  localStorage.clear()
})

describe('LegalUpdateBanner', () => {
  it('renders nothing when showBanner is false', () => {
    const { container } = render(
      <MemoryRouter>
        <LegalUpdateBanner status={makeStatus({ showBanner: false })} />
      </MemoryRouter>,
    )
    expect(container).toBeEmptyDOMElement()
  })

  it('shows the banner with a "понятно" dismiss button when showBanner is true', () => {
    render(
      <MemoryRouter>
        <LegalUpdateBanner status={makeStatus()} />
      </MemoryRouter>,
    )
    expect(screen.getByText(/Документы обновлены/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Понятно' })).toBeInTheDocument()
  })

  it('dismissing the banner hides it and it stays hidden on a fresh mount for the same version', async () => {
    const user = userEvent.setup()
    const status = makeStatus()
    const { unmount } = render(
      <MemoryRouter>
        <LegalUpdateBanner status={status} />
      </MemoryRouter>,
    )

    await user.click(screen.getByRole('button', { name: 'Понятно' }))
    expect(screen.queryByText(/Документы обновлены/)).not.toBeInTheDocument()
    unmount()

    render(
      <MemoryRouter>
        <LegalUpdateBanner status={status} />
      </MemoryRouter>,
    )
    expect(screen.queryByText(/Документы обновлены/)).not.toBeInTheDocument()
  })

  it('a later real version change re-shows the banner after an earlier dismissal', () => {
    localStorage.setItem('legal-banner-dismissed-version', 'Privacy:2026-01-01|Terms:2026-01-01')
    render(
      <MemoryRouter>
        <LegalUpdateBanner status={makeStatus()} />
      </MemoryRouter>,
    )
    expect(screen.getByText(/Документы обновлены/)).toBeInTheDocument()
  })
})
