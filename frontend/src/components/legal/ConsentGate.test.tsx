import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ConsentGate } from './ConsentGate'
import { useAuthStore } from '../../store/authStore'
import { useLegalStore } from '../../store/legalStore'
import type { ConsentStatus } from '../../types'

const accept = vi.fn()
const exportData = vi.fn()

vi.mock('../../api/legal', () => ({
  legalApi: { accept: (...args: unknown[]) => accept(...args) },
}))

vi.mock('../../api/profile', () => ({
  profileApi: { exportData: (...args: unknown[]) => exportData(...args) },
}))

function renderGate(status: ConsentStatus) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <ConsentGate status={status} />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const status: ConsentStatus = {
  requiresAcceptance: true,
  showBanner: false,
  documents: [
    { type: 'Privacy', version: '2026-10-01', acceptedVersion: '2026-09-15-draft', changeKind: 'Material' },
    { type: 'Terms', version: '2026-10-01', acceptedVersion: '2026-09-15-draft', changeKind: 'Material' },
  ],
}

beforeEach(() => {
  accept.mockReset()
  exportData.mockReset()
  useAuthStore.setState({
    user: { id: 'u1', phone: '79991234567', firstName: 'Иван', lastName: 'Петров', roles: ['Client'] },
    token: 'old-token',
  })
  useLegalStore.setState({ consentRequired: true })
})

describe('ConsentGate', () => {
  it('offers exactly reading the documents, signing out, exporting data, and deleting the account — nothing else', () => {
    renderGate(status)
    expect(screen.getByRole('link', { name: /Политика обработки персональных данных/ })).toHaveAttribute(
      'href',
      '/privacy',
    )
    expect(screen.getByRole('link', { name: /Пользовательское соглашение/ })).toHaveAttribute('href', '/terms')
    expect(screen.getByRole('button', { name: 'Выйти' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Выгрузить мои данные' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Удалить аккаунт' })).toHaveAttribute('href', '/profile/delete')
  })

  it('lets the user download their data export from the blocking screen', async () => {
    const user = userEvent.setup()
    exportData.mockResolvedValueOnce(new Blob(['{}'], { type: 'application/json' }))
    const createObjectURL = vi.fn().mockReturnValue('blob:mock')
    const revokeObjectURL = vi.fn()
    vi.stubGlobal('URL', { ...URL, createObjectURL, revokeObjectURL })
    // jsdom doesn't implement navigation — stub the anchor click so the download trigger doesn't
    // spam an unrelated "Not implemented: navigation" error to stderr.
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})

    renderGate(status)
    await user.click(screen.getByRole('button', { name: 'Выгрузить мои данные' }))

    await waitFor(() => expect(exportData).toHaveBeenCalled())
    await waitFor(() => expect(createObjectURL).toHaveBeenCalled())
    expect(clickSpy).toHaveBeenCalled()

    clickSpy.mockRestore()
    vi.unstubAllGlobals()
  })

  it('accepting stores the new token and clears the consentRequired flag', async () => {
    const user = userEvent.setup()
    accept.mockResolvedValueOnce({ token: 'new-token', acceptedAt: '2026-10-02T08:41:12Z' })
    renderGate(status)

    await user.click(screen.getByRole('button', { name: 'Принимаю новую редакцию' }))

    await waitFor(() => expect(accept).toHaveBeenCalledWith('2026-10-01', '2026-10-01'))
    await waitFor(() => expect(useAuthStore.getState().token).toBe('new-token'))
    expect(useLegalStore.getState().consentRequired).toBe(false)
  })

  it('shows an error message if accepting fails (e.g. a race on the document version)', async () => {
    const user = userEvent.setup()
    accept.mockRejectedValueOnce({
      isAxiosError: true,
      response: { status: 409, data: 'Документы были обновлены ещё раз — перечитайте и примите новую редакцию.' },
    })
    renderGate(status)

    await user.click(screen.getByRole('button', { name: 'Принимаю новую редакцию' }))

    expect(
      await screen.findByText('Документы были обновлены ещё раз — перечитайте и примите новую редакцию.'),
    ).toBeInTheDocument()
  })
})
