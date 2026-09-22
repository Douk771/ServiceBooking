import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { HealthNoteCard } from './HealthNoteCard'

const getHealthNote = vi.fn()
const updateHealthNote = vi.fn()
const deleteHealthNote = vi.fn()
const confirmHealthConsent = vi.fn()
const getText = vi.fn()

vi.mock('../../api/clientConsents', () => ({
  clientConsentsApi: {
    getHealthNote: (...args: unknown[]) => getHealthNote(...args),
    updateHealthNote: (...args: unknown[]) => updateHealthNote(...args),
    deleteHealthNote: (...args: unknown[]) => deleteHealthNote(...args),
    confirmHealthConsent: (...args: unknown[]) => confirmHealthConsent(...args),
  },
}))
vi.mock('../../api/legal', () => ({
  legalApi: { getText: (...args: unknown[]) => getText(...args) },
}))

function renderCard() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <HealthNoteCard companyId="c1" clientKey="u1" />
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  getHealthNote.mockReset()
  updateHealthNote.mockReset()
  deleteHealthNote.mockReset()
  confirmHealthConsent.mockReset()
  getText.mockReset()
  getText.mockResolvedValue({
    key: 'HealthDataConsent',
    version: '2026-09-21',
    isDraft: true,
    contentHtml: '<p>Meta.</p><h2>Текст для клиента</h2><p>Мастеру нужно записать, чего вам нельзя.</p>',
  })
})

describe('HealthNoteCard', () => {
  it('shows the empty state and requires an explicit "Заполнить" action to start editing', async () => {
    getHealthNote.mockResolvedValueOnce({ value: null, consentRequired: true })
    renderCard()

    expect(await screen.findByText('Поле пусто. Заполняется только с согласия клиента.')).toBeInTheDocument()
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
  })

  it('shows the saved value with its author/date when present', async () => {
    getHealthNote.mockResolvedValueOnce({ value: 'аллергия на аммиак', updatedAt: '2026-09-20T10:00:00Z', updatedBy: 'Иванова М.' })
    renderCard()

    expect(await screen.findByText('аллергия на аммиак')).toBeInTheDocument()
    expect(screen.getByText(/Иванова М\./)).toBeInTheDocument()
  })

  it('saving without consent opens the consent modal instead of a bare error, keeping the typed text', async () => {
    const user = userEvent.setup()
    getHealthNote.mockResolvedValue({ value: null, consentRequired: true })
    updateHealthNote.mockRejectedValueOnce({
      isAxiosError: true,
      response: { status: 400, data: { requiredTextKey: 'HealthDataConsent' } },
    })
    renderCard()

    await user.click(await screen.findByRole('button', { name: 'Заполнить' }))
    await user.type(screen.getByPlaceholderText(/Аллергия на состав/), 'беременность')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))

    expect(await screen.findByText('Согласие на обработку сведений о здоровье')).toBeInTheDocument()
    expect(screen.getByDisplayValue('беременность')).toBeInTheDocument()
  })

  it('confirming consent retries the save automatically', async () => {
    const user = userEvent.setup()
    getHealthNote.mockResolvedValue({ value: null, consentRequired: true })
    updateHealthNote
      .mockRejectedValueOnce({ isAxiosError: true, response: { status: 400, data: { requiredTextKey: 'HealthDataConsent' } } })
      .mockResolvedValueOnce(undefined)
    confirmHealthConsent.mockResolvedValueOnce(undefined)
    renderCard()

    await user.click(await screen.findByRole('button', { name: 'Заполнить' }))
    await user.type(screen.getByPlaceholderText(/Аллергия на состав/), 'беременность')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    await screen.findByText('Согласие на обработку сведений о здоровье')

    await user.click(screen.getByRole('button', { name: 'Клиент согласен' }))

    await waitFor(() => expect(confirmHealthConsent).toHaveBeenCalledWith('c1', 'u1', '2026-09-21'))
    await waitFor(() => expect(updateHealthNote).toHaveBeenCalledTimes(2))
    expect(updateHealthNote).toHaveBeenLastCalledWith('c1', 'u1', 'беременность')
  })

  it('clearing calls delete and returns to the empty state', async () => {
    const user = userEvent.setup()
    getHealthNote
      .mockResolvedValueOnce({ value: 'аллергия', updatedAt: '2026-09-20T10:00:00Z', updatedBy: 'Иванова М.' })
      .mockResolvedValueOnce({ value: null, consentRequired: true })
    deleteHealthNote.mockResolvedValueOnce(undefined)
    renderCard()

    await user.click(await screen.findByRole('button', { name: 'Очистить' }))

    await waitFor(() => expect(deleteHealthNote).toHaveBeenCalledWith('c1', 'u1'))
    expect(await screen.findByText('Поле пусто. Заполняется только с согласия клиента.')).toBeInTheDocument()
  })
})
