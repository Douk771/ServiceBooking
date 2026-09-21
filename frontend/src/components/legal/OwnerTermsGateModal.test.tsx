import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { OwnerTermsGateModal } from './OwnerTermsGateModal'
import { useAuthStore } from '../../store/authStore'
import { useOwnerGateStore } from '../../store/ownerGateStore'

const accept = vi.fn()

vi.mock('../../api/legal', () => ({
  legalApi: { accept: (...args: unknown[]) => accept(...args) },
}))

function renderModal() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <OwnerTermsGateModal />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  accept.mockReset()
  useAuthStore.setState({
    user: { id: 'u1', phone: '79991234567', firstName: 'Иван', lastName: 'Петров', roles: ['CompanyOwner'] },
    token: 'old-token',
  })
  useOwnerGateStore.setState({ pending: null })
})

describe('OwnerTermsGateModal', () => {
  it('renders nothing while there is no pending owner gate', () => {
    const { container } = renderModal()
    expect(container).toBeEmptyDOMElement()
  })

  it('opens with the document type/version from the 451 body once one is set', () => {
    useOwnerGateStore.setState({
      pending: { reason: 'OwnerTermsNotAccepted', documentType: 'TermsOwner', version: '2026-09-21' },
    })
    renderModal()
    expect(screen.getByText('Обновилось соглашение с компанией')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Соглашение с компанией/ })).toHaveAttribute('href', '/terms-owner')
  })

  it('the accept button stays disabled until the checkbox is ticked', async () => {
    const user = userEvent.setup()
    useOwnerGateStore.setState({
      pending: { reason: 'OwnerTermsNotAccepted', documentType: 'TermsOwner', version: '2026-09-21' },
    })
    renderModal()

    const acceptButton = screen.getByRole('button', { name: 'Принимаю' })
    expect(acceptButton).toBeDisabled()

    await user.click(screen.getByRole('checkbox'))
    expect(acceptButton).not.toBeDisabled()
  })

  it('accepting sends exactly the pending type/version, stores the new token, and clears the gate', async () => {
    const user = userEvent.setup()
    accept.mockResolvedValueOnce({ token: 'new-token', acceptedAt: '2026-09-22T08:00:00Z' })
    useOwnerGateStore.setState({
      pending: { reason: 'OwnerTermsNotAccepted', documentType: 'TermsOwner', version: '2026-09-21' },
    })
    renderModal()

    await user.click(screen.getByRole('checkbox'))
    await user.click(screen.getByRole('button', { name: 'Принимаю' }))

    await waitFor(() => expect(accept).toHaveBeenCalledWith([{ type: 'TermsOwner', version: '2026-09-21' }]))
    await waitFor(() => expect(useAuthStore.getState().token).toBe('new-token'))
    await waitFor(() => expect(useOwnerGateStore.getState().pending).toBeNull())
  })

  it('dismissing without accepting just clears the gate, without calling accept', async () => {
    const user = userEvent.setup()
    useOwnerGateStore.setState({
      pending: { reason: 'OwnerTermsNotAccepted', documentType: 'TermsOwner', version: '2026-09-21' },
    })
    renderModal()

    await user.click(screen.getByRole('button', { name: 'Позже' }))

    expect(accept).not.toHaveBeenCalled()
    expect(useOwnerGateStore.getState().pending).toBeNull()
  })
})
