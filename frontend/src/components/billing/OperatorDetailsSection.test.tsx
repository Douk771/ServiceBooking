import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { OperatorDetailsSection } from './OperatorDetailsSection'

const getOperatorDetails = vi.fn()
const updateOperatorDetails = vi.fn()

vi.mock('../../api/billing', () => ({
  billingApi: {
    getOperatorDetails: (...args: unknown[]) => getOperatorDetails(...args),
    updateOperatorDetails: (...args: unknown[]) => updateOperatorDetails(...args),
  },
}))

function renderSection() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <OperatorDetailsSection />
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  getOperatorDetails.mockReset()
  updateOperatorDetails.mockReset()
})

// API_CONTRACT_CYCLE20.md §432.8 (Т20-04 п. 3, US-20-01).
describe('OperatorDetailsSection', () => {
  it('renders nothing when the caller has no billing account (404)', async () => {
    getOperatorDetails.mockRejectedValueOnce({ isAxiosError: true, response: { status: 404 } })
    const { container } = renderSection()
    await waitFor(() => expect(container).toBeEmptyDOMElement())
  })

  it('shows a missing-details hint, never blocking, when nothing is filled in', async () => {
    getOperatorDetails.mockResolvedValue({ fullName: null, address: null, inn: null, missing: true })
    renderSection()
    expect(await screen.findByText(/Реквизиты не заполнены/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Заполнить' })).toBeInTheDocument()
  })

  it('shows the saved details read-only when present', async () => {
    getOperatorDetails.mockResolvedValue({ fullName: 'Иванова Мария Сергеевна', address: 'г. Барнаул, ул. Ленина, 1', inn: '222500000000', missing: false })
    renderSection()
    expect(await screen.findByText('Иванова Мария Сергеевна')).toBeInTheDocument()
    expect(screen.getByText('г. Барнаул, ул. Ленина, 1')).toBeInTheDocument()
    expect(screen.getByText('ИНН 222500000000')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Изменить' })).toBeInTheDocument()
  })

  it('saves edited details, converting blank fields to null', async () => {
    const user = userEvent.setup()
    getOperatorDetails.mockResolvedValue({ fullName: null, address: null, inn: null, missing: true })
    updateOperatorDetails.mockResolvedValueOnce({ fullName: 'Иванова Мария Сергеевна', address: null, inn: null, missing: true })
    renderSection()

    await user.click(await screen.findByRole('button', { name: 'Заполнить' }))
    await user.type(screen.getByLabelText(/ФИО оператора/), 'Иванова Мария Сергеевна')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))

    await waitFor(() =>
      expect(updateOperatorDetails).toHaveBeenCalledWith({ fullName: 'Иванова Мария Сергеевна', address: null, inn: null }),
    )
  })

  it('shows the server validation message verbatim on a failed save (e.g. initials rejected)', async () => {
    const user = userEvent.setup()
    getOperatorDetails.mockResolvedValue({ fullName: null, address: null, inn: null, missing: true })
    updateOperatorDetails.mockRejectedValueOnce({ isAxiosError: true, response: { status: 400, data: 'Укажите фамилию, имя и отчество полностью' } })
    renderSection()

    await user.click(await screen.findByRole('button', { name: 'Заполнить' }))
    await user.type(screen.getByLabelText(/ФИО оператора/), 'Иванов И. И.')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))

    expect(await screen.findByText('Укажите фамилию, имя и отчество полностью')).toBeInTheDocument()
  })
})
