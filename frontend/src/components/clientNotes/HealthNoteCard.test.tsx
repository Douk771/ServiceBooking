import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { HealthNoteCard } from './HealthNoteCard'
import { saveLastPrintedHealthForm, clearLastPrintedHealthFormsForTests } from '../../utils/healthConsentForm'

const getHealthNote = vi.fn()
const updateHealthNote = vi.fn()
const deleteHealthNote = vi.fn()
const markWrittenConsent = vi.fn()
const revokeWrittenConsent = vi.fn()

vi.mock('../../api/clientConsents', () => ({
  clientConsentsApi: {
    getHealthNote: (...args: unknown[]) => getHealthNote(...args),
    updateHealthNote: (...args: unknown[]) => updateHealthNote(...args),
    deleteHealthNote: (...args: unknown[]) => deleteHealthNote(...args),
    markWrittenConsent: (...args: unknown[]) => markWrittenConsent(...args),
    revokeWrittenConsent: (...args: unknown[]) => revokeWrittenConsent(...args),
  },
}))

function renderCard() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <HealthNoteCard companyId="c1" clientKey="u1" />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  getHealthNote.mockReset()
  updateHealthNote.mockReset()
  deleteHealthNote.mockReset()
  markWrittenConsent.mockReset()
  revokeWrittenConsent.mockReset()
  clearLastPrintedHealthFormsForTests()
})

// API_CONTRACT_CYCLE20.md §432.1 (LG1, US-20-01) — the field is gated ONLY by writtenConsent.granted.
describe('HealthNoteCard — closed without a written-consent mark (cycle 20)', () => {
  it('shows the print/mark flow, not the old "Заполнить" bare form, when there is no mark', async () => {
    getHealthNote.mockResolvedValue({ value: null, consentRequired: true, writtenConsent: { granted: false, currentFormVersion: 'v1' } })
    renderCard()

    expect(await screen.findByText(/Поле закрыто/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Распечатать бланк' })).toHaveAttribute(
      'href',
      '/companies/c1/clients/u1/health-consent-form',
    )
    expect(screen.getByRole('button', { name: 'Бланк подписан, оригинал у нас' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Заполнить' })).not.toBeInTheDocument()
  })

  it('is closed even when the salon electronic consent would have been enough before cycle 20', async () => {
    // §432.1: "Электронное согласие ... поле НЕ открывает" — value present in the DB doesn't matter,
    // granted stays false without a paper-form mark, and the server would send value: null anyway.
    getHealthNote.mockResolvedValue({ value: null, consentRequired: true, writtenConsent: { granted: false, currentFormVersion: 'v1' } })
    renderCard()
    expect(await screen.findByText(/Поле закрыто/)).toBeInTheDocument()
  })

  it('opens the mark dialog and submits with the current form version, own-form default (no last print)', async () => {
    const user = userEvent.setup()
    getHealthNote.mockResolvedValue({ value: null, consentRequired: true, writtenConsent: { granted: false, currentFormVersion: 'v1' } })
    markWrittenConsent.mockResolvedValueOnce({ granted: true, currentFormVersion: 'v1' })
    renderCard()

    await user.click(await screen.findByRole('button', { name: 'Бланк подписан, оригинал у нас' }))
    expect(await screen.findByRole('heading', { name: 'Бланк подписан, оригинал у нас' })).toBeInTheDocument()
    // No last-printed form in sessionStorage → defaults to "own form" (checkbox pre-checked), so the
    // formId input isn't shown at all.
    expect(screen.queryByPlaceholderText('HD-7K3M9QTX')).not.toBeInTheDocument()

    const confirmCheckbox = screen.getByRole('checkbox', { name: 'Подписанный оригинал у нас' })
    await user.click(confirmCheckbox)
    await user.click(screen.getByRole('button', { name: 'Отметить' }))

    await waitFor(() => expect(markWrittenConsent).toHaveBeenCalledWith('c1', 'u1', { textVersion: 'v1', formId: null, confirmed: true }))
  })

  it('prefills formId from the just-printed form when its version matches the current one', async () => {
    saveLastPrintedHealthForm('c1', 'u1', { formId: 'HD-7K3M9QTX', textVersion: 'v1' })
    const user = userEvent.setup()
    getHealthNote.mockResolvedValue({ value: null, consentRequired: true, writtenConsent: { granted: false, currentFormVersion: 'v1' } })
    markWrittenConsent.mockResolvedValueOnce({ granted: true, currentFormVersion: 'v1' })
    renderCard()

    await user.click(await screen.findByRole('button', { name: 'Бланк подписан, оригинал у нас' }))
    expect(await screen.findByDisplayValue('HD-7K3M9QTX')).toBeInTheDocument()
    await user.click(screen.getByRole('checkbox', { name: 'Подписанный оригинал у нас' }))
    await user.click(screen.getByRole('button', { name: 'Отметить' }))

    await waitFor(() => expect(markWrittenConsent).toHaveBeenCalledWith('c1', 'u1', { textVersion: 'v1', formId: 'HD-7K3M9QTX', confirmed: true }))
  })

  it('does NOT prefill formId when the last-printed form is for an older text version', async () => {
    saveLastPrintedHealthForm('c1', 'u1', { formId: 'HD-7K3M9QTX', textVersion: 'v0-old' })
    const user = userEvent.setup()
    getHealthNote.mockResolvedValue({ value: null, consentRequired: true, writtenConsent: { granted: false, currentFormVersion: 'v1' } })
    renderCard()

    await user.click(await screen.findByRole('button', { name: 'Бланк подписан, оригинал у нас' }))
    expect(screen.queryByDisplayValue('HD-7K3M9QTX')).not.toBeInTheDocument()
  })

  it('does not submit an empty formId as "own form" — unchecking own-form with a blank number blocks the confirm button', async () => {
    const user = userEvent.setup()
    getHealthNote.mockResolvedValue({ value: null, consentRequired: true, writtenConsent: { granted: false, currentFormVersion: 'v1' } })
    renderCard()

    await user.click(await screen.findByRole('button', { name: 'Бланк подписан, оригинал у нас' }))
    // Own-form is checked by default (no last print) — uncheck it, leaving the number blank.
    await user.click(screen.getByRole('checkbox', { name: /собственный бланк салона/ }))
    await user.click(screen.getByRole('checkbox', { name: 'Подписанный оригинал у нас' }))

    expect(screen.getByText(/Укажите номер бланка/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Отметить' })).toBeDisabled()
    expect(markWrittenConsent).not.toHaveBeenCalled()
  })

  it('shows a reprint prompt on 409 (form text was republished)', async () => {
    const user = userEvent.setup()
    getHealthNote.mockResolvedValue({ value: null, consentRequired: true, writtenConsent: { granted: false, currentFormVersion: 'v1' } })
    markWrittenConsent.mockRejectedValueOnce({ isAxiosError: true, response: { status: 409, data: 'Текст бланка обновлён.' } })
    renderCard()

    await user.click(await screen.findByRole('button', { name: 'Бланк подписан, оригинал у нас' }))
    await user.click(screen.getByRole('checkbox', { name: 'Подписанный оригинал у нас' }))
    await user.click(screen.getByRole('button', { name: 'Отметить' }))

    expect(await screen.findByText('Текст бланка обновлён.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Распечатать бланк заново' })).toBeInTheDocument()
  })
})

describe('HealthNoteCard — open with an active written-consent mark', () => {
  const grantedNote = (
    overrides: Record<string, unknown> = {},
    writtenConsentOverrides: Record<string, unknown> = {},
  ) => ({
    value: null,
    consentRequired: false,
    writtenConsent: { granted: true, currentFormVersion: 'v1', formVersion: 'v1', confirmedByName: 'Иванова М.', ...writtenConsentOverrides },
    ...overrides,
  })

  it('shows the empty state and requires an explicit "Заполнить" action to start editing', async () => {
    getHealthNote.mockResolvedValueOnce(grantedNote())
    renderCard()

    expect(await screen.findByText('Поле пусто.')).toBeInTheDocument()
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
  })

  it('shows the saved value with its author/date when present', async () => {
    getHealthNote.mockResolvedValueOnce(grantedNote({ value: 'аллергия на аммиак', updatedAt: '2026-09-20T10:00:00Z', updatedBy: 'Иванова М.' }))
    renderCard()

    expect(await screen.findByText('аллергия на аммиак')).toBeInTheDocument()
    expect(screen.getByText(/Иванова М\./)).toBeInTheDocument()
  })

  it('saves the value directly — no consent modal needed once the mark exists', async () => {
    const user = userEvent.setup()
    getHealthNote.mockResolvedValue(grantedNote())
    updateHealthNote.mockResolvedValueOnce(undefined)
    renderCard()

    await user.click(await screen.findByRole('button', { name: 'Заполнить' }))
    await user.type(screen.getByPlaceholderText(/Аллергия на состав/), 'беременность')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))

    await waitFor(() => expect(updateHealthNote).toHaveBeenCalledWith('c1', 'u1', 'беременность'))
  })

  it('clearing calls delete and returns to the empty state', async () => {
    const user = userEvent.setup()
    getHealthNote
      .mockResolvedValueOnce(grantedNote({ value: 'аллергия', updatedAt: '2026-09-20T10:00:00Z', updatedBy: 'Иванова М.' }))
      .mockResolvedValueOnce(grantedNote())
    deleteHealthNote.mockResolvedValueOnce(undefined)
    renderCard()

    await user.click(await screen.findByRole('button', { name: 'Очистить' }))

    await waitFor(() => expect(deleteHealthNote).toHaveBeenCalledWith('c1', 'u1'))
    expect(await screen.findByText('Поле пусто.')).toBeInTheDocument()
  })

  it('shows a non-blocking hint when the mark was signed under a previous text version', async () => {
    getHealthNote.mockResolvedValue(grantedNote({}, { formVersion: 'v0-old' }))
    renderCard()
    expect(await screen.findByText(/прежней редакции/)).toBeInTheDocument()
    // Still fully usable — "Заполнить" is there, not replaced by the closed-state UI.
    expect(screen.getByRole('button', { name: 'Заполнить' })).toBeInTheDocument()
  })

  it('opens the revoke dialog with a mandatory-deletion warning, and calls the revoke endpoint', async () => {
    const user = userEvent.setup()
    getHealthNote.mockResolvedValue(grantedNote())
    revokeWrittenConsent.mockResolvedValueOnce({ revoked: 1, healthNotesDeleted: 0, writtenConsent: { granted: false, currentFormVersion: 'v1' } })
    renderCard()

    await user.click(await screen.findByRole('button', { name: 'Снять отметку о согласии' }))
    expect(await screen.findByText(/будет удалена без возможности восстановления/)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Снять отметку' }))
    await waitFor(() => expect(revokeWrittenConsent).toHaveBeenCalledWith('c1', 'u1', 'SubjectWithdrew'))
  })
})
