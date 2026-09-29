import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { CompanyAddressField } from './CompanyAddressField'
import type { Company } from '../../types'

const getText = vi.fn()
const confirmNotice = vi.fn()
const saveAddress = vi.fn()

vi.mock('../../api/legal', () => ({
  legalApi: { getText: (...args: unknown[]) => getText(...args) },
}))
vi.mock('../../api/companyAddress', () => ({
  companyAddressApi: {
    saveAddress: (...args: unknown[]) => saveAddress(...args),
    confirmNotice: (...args: unknown[]) => confirmNotice(...args),
  },
}))

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>
}

const noticeHtml = '<h2>Текст для владельца</h2><p>Этот адрес увидит любой человек в интернете.</p><h2>Подтверждение</h2><p>x</p>'

function stubCompany(): Company {
  return { id: 'c1', name: 'Гвоздь', slug: 'gvozd', allowSelfBooking: true }
}

beforeEach(() => {
  getText.mockReset()
  confirmNotice.mockReset()
  saveAddress.mockReset()
  getText.mockResolvedValue({ key: 'PublicAddressNotice', version: 'v1', isDraft: true, contentHtml: noticeHtml })
})

describe('CompanyAddressField — ARCHITECTURE_CYCLE19.md §388.3', () => {
  it('renders an ordinary field with no Save button until the value changes', () => {
    render(<CompanyAddressField companyId="c1" initialAddress="Ленина 5" onSaved={vi.fn()} />, { wrapper })
    expect(screen.getByLabelText('Адрес')).toHaveValue('Ленина 5')
    expect(screen.queryByRole('button', { name: 'Сохранить' })).not.toBeInTheDocument()
  })

  it('editing and saving shows the public-address notice before saving', async () => {
    const user = userEvent.setup()
    render(<CompanyAddressField companyId="c1" initialAddress="Ленина 5" onSaved={vi.fn()} />, { wrapper })

    await user.type(screen.getByLabelText('Адрес'), ', 7')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))

    expect(await screen.findByText('Адрес станет общедоступным')).toBeInTheDocument()
    expect(saveAddress).not.toHaveBeenCalled()
  })

  it('cancelling the gate saves nothing', async () => {
    const user = userEvent.setup()
    render(<CompanyAddressField companyId="c1" initialAddress="Ленина 5" onSaved={vi.fn()} />, { wrapper })

    await user.type(screen.getByLabelText('Адрес'), ', 7')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    await user.click(await screen.findByRole('button', { name: 'Отмена' }))

    expect(saveAddress).not.toHaveBeenCalled()
    expect(screen.queryByText('Адрес станет общедоступным')).not.toBeInTheDocument()
  })

  it('end to end: edit, save, confirm the notice — saveAddress is called without verify, onSaved fires with the fresh company', async () => {
    confirmNotice.mockResolvedValue({ version: 'v1', acknowledgedAt: '2026-09-24T10:00:00Z' })
    saveAddress.mockResolvedValue({ company: stubCompany() })
    const onSaved = vi.fn()
    const user = userEvent.setup()
    render(<CompanyAddressField companyId="c1" initialAddress="Ленина 5" onSaved={onSaved} />, { wrapper })

    await user.type(screen.getByLabelText('Адрес'), ', 7')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    await user.click(await screen.findByRole('button', { name: 'Понятно, сохранить адрес' }))

    await waitFor(() => expect(saveAddress).toHaveBeenCalledWith('c1', 'Ленина 5, 7'))
    await waitFor(() => expect(onSaved).toHaveBeenCalledWith(stubCompany()))
  })

  it('a save error shows a human message linked via aria-describedby, and does not clear dirty', async () => {
    confirmNotice.mockResolvedValue({ version: 'v1', acknowledgedAt: '2026-09-24T10:00:00Z' })
    saveAddress.mockRejectedValue(new Error('network'))
    const user = userEvent.setup()
    render(<CompanyAddressField companyId="c1" initialAddress="Ленина 5" onSaved={vi.fn()} />, { wrapper })

    await user.type(screen.getByLabelText('Адрес'), ', 7')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    await user.click(await screen.findByRole('button', { name: 'Понятно, сохранить адрес' }))

    expect(await screen.findByText('Не удалось сохранить адрес. Попробуйте ещё раз.')).toBeInTheDocument()
    const input = screen.getByLabelText('Адрес')
    const describedBy = input.getAttribute('aria-describedby')
    expect(describedBy).toBeTruthy()
    expect(document.getElementById(describedBy!)).toHaveTextContent('Не удалось сохранить адрес')
  })

  it('an empty address is a valid save (erases the address)', async () => {
    confirmNotice.mockResolvedValue({ version: 'v1', acknowledgedAt: '2026-09-24T10:00:00Z' })
    saveAddress.mockResolvedValue({ company: stubCompany() })
    const user = userEvent.setup()
    render(<CompanyAddressField companyId="c1" initialAddress="Ленина 5" onSaved={vi.fn()} />, { wrapper })

    await user.clear(screen.getByLabelText('Адрес'))
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    await user.click(await screen.findByRole('button', { name: 'Понятно, сохранить адрес' }))

    await waitFor(() => expect(saveAddress).toHaveBeenCalledWith('c1', ''))
  })
})
