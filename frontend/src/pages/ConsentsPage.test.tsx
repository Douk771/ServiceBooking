import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ConsentsPage } from './ConsentsPage'
import type { ProfileConsentsResponse } from '../types'

const get = vi.fn()
const grant = vi.fn()
const revoke = vi.fn()
const revokePreview = vi.fn()

vi.mock('../api/consents', () => ({
  consentsApi: {
    get: (...args: unknown[]) => get(...args),
    grant: (...args: unknown[]) => grant(...args),
    revoke: (...args: unknown[]) => revoke(...args),
    revokePreview: (...args: unknown[]) => revokePreview(...args),
  },
}))

function response(overrides: Partial<ProfileConsentsResponse> = {}): ProfileConsentsResponse {
  return {
    document: {
      type: 'PdnConsent',
      version: '2026-09-21',
      isDraft: true,
      purposes: [
        { key: 'ProviderDelivery', title: 'Передача привлекаемым лицам для доставки уведомлений' },
        { key: 'WorkPhotos', title: 'Фотофиксация выполненной работы' },
      ],
    },
    granted: [{ purpose: 'ProviderDelivery', version: '2026-09-21', grantedAt: '2026-09-21T10:00:00Z', revokedAt: null }],
    versionOutdated: false,
    history: [],
    ...overrides,
  }
}

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <ConsentsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  get.mockReset()
  grant.mockReset()
  revoke.mockReset()
  revokePreview.mockReset()
})

describe('ConsentsPage', () => {
  it('shows a granted purpose with its date/version, and an ungranted one as a checkbox to opt in', async () => {
    get.mockResolvedValueOnce(response())
    renderPage()

    expect(await screen.findByText(/Согласие дано/)).toBeInTheDocument()
    expect(screen.getByText('Согласие не дано')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Отозвать' })).toBeInTheDocument()
  })

  it('shows the soft "versionOutdated" banner without blocking anything', async () => {
    get.mockResolvedValueOnce(response({ versionOutdated: true }))
    renderPage()

    expect(await screen.findByText(/Документ обновился/)).toBeInTheDocument()
  })

  it('granting sends only the newly checked purposes, not the whole purpose list', async () => {
    const user = userEvent.setup()
    get.mockResolvedValue(response())
    grant.mockResolvedValueOnce(response())
    renderPage()

    await user.click(await screen.findByText('Согласие не дано'))
    await user.click(screen.getByRole('button', { name: 'Дать согласие на отмеченное' }))

    await waitFor(() => expect(grant).toHaveBeenCalledWith('PdnConsent', '2026-09-21', ['WorkPhotos']))
  })

  it('opening "Отозвать" fetches the preview and shows the numbers before any confirmation', async () => {
    const user = userEvent.setup()
    get.mockResolvedValue(response())
    revokePreview.mockResolvedValueOnce({
      photosDeleted: 12,
      healthNotesDeleted: 0,
      profileFieldsCleared: [],
      queuedNotificationsCancelled: 3,
    })
    renderPage()

    await user.click(await screen.findByRole('button', { name: 'Отозвать' }))

    expect(await screen.findByText('Будет удалено фотографий: 12')).toBeInTheDocument()
    expect(screen.getByText('Будет отменено уже поставленных в очередь уведомлений: 3')).toBeInTheDocument()
    expect(revoke).not.toHaveBeenCalled()
  })

  // §41.3 — revoking ProviderDelivery must say plainly, before confirmation, that WhatsApp
  // notifications stop — this is the exact promise `legal/04-pdn-consent.html` (цель 1) makes to the
  // user, and the screen silently disagreeing with it would itself be a defect.
  it('revoking ProviderDelivery says plainly that notifications will stop, before confirmation', async () => {
    const user = userEvent.setup()
    get.mockResolvedValue(response())
    revokePreview.mockResolvedValueOnce({
      photosDeleted: 0,
      healthNotesDeleted: 0,
      profileFieldsCleared: [],
      queuedNotificationsCancelled: 0,
    })
    renderPage()

    await user.click(await screen.findByRole('button', { name: 'Отозвать' }))

    expect(await screen.findByText(/Сообщений о записи в WhatsApp вы больше получать не будете/)).toBeInTheDocument()
    expect(screen.getByText(/не удаляет историю визитов/)).toBeInTheDocument()
    expect(screen.getByText(/не включает и не снимает отписку/)).toBeInTheDocument()
  })

  it('confirming the revoke calls the API with the purpose and shows the returned effects', async () => {
    const user = userEvent.setup()
    get.mockResolvedValue(response())
    revokePreview.mockResolvedValueOnce({
      photosDeleted: 5,
      healthNotesDeleted: 0,
      profileFieldsCleared: [],
      queuedNotificationsCancelled: 0,
    })
    revoke.mockResolvedValueOnce({
      revoked: 1,
      effects: { photosDeleted: 5, healthNotesDeleted: 0, profileFieldsCleared: [], queuedNotificationsCancelled: 0 },
    })
    renderPage()

    await user.click(await screen.findByRole('button', { name: 'Отозвать' }))
    await screen.findByText('Будет удалено фотографий: 5')
    await user.click(screen.getByRole('button', { name: 'Отозвать согласие' }))

    await waitFor(() => expect(revoke).toHaveBeenCalledWith('PdnConsent', 'ProviderDelivery', undefined))
    expect(await screen.findByText('Согласие отозвано')).toBeInTheDocument()
  })
})
