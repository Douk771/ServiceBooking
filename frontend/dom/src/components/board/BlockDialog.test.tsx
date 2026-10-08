import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BlockDialog } from './BlockDialog'
import { httpError } from '../../test/fixtures'
import type { BoardHouseDto } from '../../types'

const api = vi.hoisted(() => ({ updateBlock: vi.fn(), createBlock: vi.fn(), deleteBlock: vi.fn() }))
vi.mock('../../api/staysBoard', () => ({ staysBoardApi: api }))
vi.mock('../../api/legalTexts', () => ({ stayLegalTextsApi: { get: () => Promise.reject(httpError(404, '')) } }))

const houses = [{ id: 'h1', name: 'Дом у леса', isArchived: false }] as unknown as BoardHouseDto[]

beforeEach(() => Object.values(api).forEach((f) => f.mockReset()))

function renderDialog(comment: string | null) {
  const onChanged = vi.fn()
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <BlockDialog
          companyId="co-1"
          houses={houses}
          today="2027-01-01"
          onClose={vi.fn()}
          onChanged={onChanged}
          target={{ block: { id: 'b1', houseId: 'h1', startDate: '2027-02-10', endDate: '2027-02-13', kind: 'Repair', comment } }}
        />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { onChanged }
}

describe('BlockDialog — editing a block', () => {
  it('shows the existing comment and sends it back unchanged on save', async () => {
    api.updateBlock.mockResolvedValue({})
    renderDialog('Меняем крышу')
    expect(screen.getByLabelText(/Комментарий/)).toHaveValue('Меняем крышу')
    fireEvent.click(screen.getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(api.updateBlock).toHaveBeenCalled())
    expect(api.updateBlock.mock.calls[0][2]).toMatchObject({ comment: 'Меняем крышу', startDate: '2027-02-10', endDate: '2027-02-13' })
  })

  it('a block without a comment saves with comment null', async () => {
    api.updateBlock.mockResolvedValue({})
    renderDialog(null)
    expect(screen.getByLabelText(/Комментарий/)).toHaveValue('')
    fireEvent.click(screen.getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(api.updateBlock).toHaveBeenCalled())
    expect(api.updateBlock.mock.calls[0][2].comment).toBeNull()
  })
})
