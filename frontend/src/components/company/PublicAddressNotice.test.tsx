import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { PublicAddressNotice } from './PublicAddressNotice'

const getText = vi.fn()
const confirmNotice = vi.fn()

vi.mock('../../api/legal', () => ({
  legalApi: { getText: (...args: unknown[]) => getText(...args) },
}))
vi.mock('../../api/companyAddress', () => ({
  companyAddressApi: { confirmNotice: (...args: unknown[]) => confirmNotice(...args) },
}))

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>
}

const html =
  '<p>Meta.</p><h2>Текст для владельца</h2><p>Этот адрес увидит любой человек в интернете.</p><h2>Подтверждение</h2>' +
  '<p>[ Понятно, сохранить адрес ] [ Отмена ]</p><h2>Служебное приложение А</h2><p>Только для команды.</p>'

beforeEach(() => {
  getText.mockReset()
  confirmNotice.mockReset()
  getText.mockResolvedValue({ key: 'PublicAddressNotice', version: '2026-09-24-draft', isDraft: true, contentHtml: html })
})

describe('PublicAddressNotice — ARCHITECTURE_CYCLE13.md §220.2', () => {
  it('shows only the owner-facing section, not the service appendix', async () => {
    render(<PublicAddressNotice onConfirmed={vi.fn()} onCancel={vi.fn()} />, { wrapper })
    expect(await screen.findByText(/увидит любой человек в интернете/)).toBeInTheDocument()
    expect(screen.queryByText(/Только для команды/)).not.toBeInTheDocument()
  })

  it('confirming calls confirmNotice with the version actually shown, then onConfirmed', async () => {
    confirmNotice.mockResolvedValue({ version: '2026-09-24-draft', acknowledgedAt: '2026-09-24T10:00:00Z' })
    const onConfirmed = vi.fn()
    const user = userEvent.setup()
    render(<PublicAddressNotice onConfirmed={onConfirmed} onCancel={vi.fn()} />, { wrapper })

    await user.click(await screen.findByRole('button', { name: 'Понятно, сохранить адрес' }))

    await waitFor(() => expect(confirmNotice).toHaveBeenCalledWith('2026-09-24-draft'))
    await waitFor(() => expect(onConfirmed).toHaveBeenCalled())
  })

  it('Cancel calls onCancel and never calls confirmNotice', async () => {
    const onCancel = vi.fn()
    const user = userEvent.setup()
    render(<PublicAddressNotice onConfirmed={vi.fn()} onCancel={onCancel} />, { wrapper })

    await user.click(await screen.findByRole('button', { name: 'Отмена' }))

    expect(onCancel).toHaveBeenCalled()
    expect(confirmNotice).not.toHaveBeenCalled()
  })

  it('disables confirmation and shows an error when the owner-facing anchor is missing (R24)', async () => {
    getText.mockResolvedValue({
      key: 'PublicAddressNotice',
      version: '2026-09-24-draft',
      isDraft: true,
      contentHtml: '<h2>Другой заголовок</h2><p>Что-то</p>',
    })
    render(<PublicAddressNotice onConfirmed={vi.fn()} onCancel={vi.fn()} />, { wrapper })

    expect(await screen.findByText('Текст предупреждения временно недоступен.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Понятно, сохранить адрес' })).toBeDisabled()
  })

  it('a 409 asks to re-read and confirm again, and re-fetches the text', async () => {
    confirmNotice.mockRejectedValue({ response: { status: 409 } })
    const onConfirmed = vi.fn()
    const user = userEvent.setup()
    render(<PublicAddressNotice onConfirmed={onConfirmed} onCancel={vi.fn()} />, { wrapper })

    await user.click(await screen.findByRole('button', { name: 'Понятно, сохранить адрес' }))

    expect(await screen.findByText(/Прочитайте заново и подтвердите ещё раз/)).toBeInTheDocument()
    expect(onConfirmed).not.toHaveBeenCalled()
    expect(getText).toHaveBeenCalledTimes(2)
  })
})
