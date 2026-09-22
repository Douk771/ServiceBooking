import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ChannelRequestModal } from './ChannelRequestModal'
import type { LegalManifest } from '../../types'

const request = vi.fn()
const getManifest = vi.fn()

vi.mock('../../api/notificationChannels', () => ({
  notificationChannelsApi: { request: (...args: unknown[]) => request(...args) },
}))
vi.mock('../../api/legal', () => ({
  legalApi: { getManifest: (...args: unknown[]) => getManifest(...args) },
}))

function manifest(): LegalManifest {
  return {
    documents: [
      {
        type: 'TermsOwner',
        title: 'Соглашение с компанией',
        version: '2026-09-21',
        effectiveFrom: '2026-09-21',
        isDraft: true,
        changeKind: 'Material',
        gate: 'OwnerScope',
        url: '/terms-owner',
      },
    ],
    uiTexts: [],
  }
}

function renderModal() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <ChannelRequestModal onClose={() => {}} />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  request.mockReset()
  getManifest.mockReset()
  getManifest.mockResolvedValue(manifest())
})

describe('ChannelRequestModal', () => {
  it('keeps submit disabled until the INN is a plausible length and the offer is accepted', async () => {
    const user = userEvent.setup()
    renderModal()

    const submit = await screen.findByRole('button', { name: 'Подать заявку' })
    expect(submit).toBeDisabled()

    await user.type(screen.getByLabelText('ИНН'), '123')
    expect(submit).toBeDisabled()
    expect(screen.getByText('ИНН должен содержать 10 или 12 цифр')).toBeInTheDocument()

    await user.clear(screen.getByLabelText('ИНН'))
    await user.type(screen.getByLabelText('ИНН'), '7707083893')
    expect(submit).toBeDisabled()

    await user.click(screen.getByRole('checkbox'))
    expect(submit).not.toBeDisabled()
  })

  it('submits with the TermsOwner version as offerAccepted.version, not a separate ChannelOffer type', async () => {
    const user = userEvent.setup()
    request.mockResolvedValueOnce({ id: 'ch1' })
    renderModal()

    await user.selectOptions(await screen.findByLabelText('Форма'), 'Company')
    await user.type(screen.getByLabelText('ИНН'), '7707083893')
    await user.click(screen.getByRole('checkbox'))
    await user.click(screen.getByRole('button', { name: 'Подать заявку' }))

    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({
        legalEntityForm: 'Company',
        inn: '7707083893',
        offerAccepted: { version: '2026-09-21' },
      }),
    )
  })
})
