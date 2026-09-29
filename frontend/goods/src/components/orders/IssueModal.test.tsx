import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { IssueModal } from './IssueModal'
import { staffCard, staffOrder } from './testData'

const issueQuote = vi.fn()
const issue = vi.fn()
vi.mock('../../api/orders', () => ({ ordersApi: { issueQuote: (...a: unknown[]) => issueQuote(...a), issue: (...a: unknown[]) => issue(...a) } }))

const order = staffCard({ status: 'Ready', version: 7, availableActions: ['Issue'] })

function renderModal(props: Partial<Parameters<typeof IssueModal>[0]> = {}) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const onDone = vi.fn()
  const onConflict = vi.fn()
  render(
    <QueryClientProvider client={qc}>
      <IssueModal shopId="s1" order={order} onClose={() => {}} onDone={onDone} onConflict={onConflict} {...props} />
    </QueryClientProvider>,
  )
  return { onDone, onConflict }
}

beforeEach(() => {
  issueQuote.mockReset().mockImplementation((_s, _o, actual: { itemId: string; quantity: number }[]) => {
    const g = actual.find((a) => a.itemId === 'i2')?.quantity ?? 500
    return Promise.resolve({ lines: [], finalTotal: 500 + Math.floor((54000 * g + 500) / 1000) / 100 })
  })
  issue.mockReset()
})

describe('IssueModal', () => {
  it('pre-fills the ordered weight and shows the exact sum from issue-quote', async () => {
    renderModal()
    expect(screen.getByLabelText(/Фактический вес, г: Сыр твёрдый/)).toHaveValue('500')
    expect(await screen.findByText('770 ₽')).toBeInTheDocument()
    expect(issueQuote).toHaveBeenCalledWith('s1', 'o1', [{ itemId: 'i2', quantity: 500 }])
  })

  it('recomputes the sum when the actual weight changes and issues with the same quantities and the loaded version', async () => {
    issue.mockResolvedValue(staffOrder({ status: 'Issued' }))
    const user = userEvent.setup()
    const { onDone } = renderModal()
    const input = screen.getByLabelText(/Фактический вес, г: Сыр твёрдый/)
    await user.clear(input)
    await user.type(input, '512')
    expect(await screen.findByText('776,48 ₽')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Выдать заказ' }))
    await waitFor(() => expect(onDone).toHaveBeenCalled())
    expect(issue).toHaveBeenCalledWith('s1', 'o1', 7, [{ itemId: 'i2', quantity: 512 }])
  })

  it('does not allow issuing without a valid weight for every weighed item', async () => {
    const user = userEvent.setup()
    renderModal()
    const input = screen.getByLabelText(/Фактический вес, г: Сыр твёрдый/)
    await user.clear(input)
    expect(screen.getByRole('alert')).toHaveTextContent('Укажите фактический вес каждой весовой позиции')
    expect(screen.getByRole('button', { name: 'Выдать заказ' })).toBeDisabled()
    await user.type(input, '0')
    expect(screen.getByRole('button', { name: 'Выдать заказ' })).toBeDisabled()
  })

  it('hands a 409 conflict to the screen instead of retrying', async () => {
    const conflict = { code: 'VersionMismatch', message: 'Заказ уже изменён — вот актуальное состояние', order: staffOrder({ version: 8 }) }
    issue.mockRejectedValue({ response: { status: 409, data: conflict } })
    const user = userEvent.setup()
    const { onConflict } = renderModal()
    await screen.findByText('770 ₽')
    await user.click(screen.getByRole('button', { name: 'Выдать заказ' }))
    await waitFor(() => expect(onConflict).toHaveBeenCalledWith(conflict))
    expect(issue).toHaveBeenCalledTimes(1)
  })
})
