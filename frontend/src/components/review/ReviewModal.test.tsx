import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ReviewModal } from './ReviewModal'

vi.mock('../../api/reviews', () => ({
  reviewsApi: { submit: vi.fn() },
}))

function renderModal(onClose = vi.fn()) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={qc}>
      <ReviewModal bookingId="b1" serviceName="Стрижка" masterName="Анна" onClose={onClose} onSuccess={vi.fn()} />
    </QueryClientProvider>,
  )
  return onClose
}

// Cycle 22 (ARCHITECTURE_CYCLE22.md §377) — ReviewModal is built on the shared <Modal>.
describe('ReviewModal', () => {
  it('is a labelled dialog with the same content', () => {
    renderModal()
    expect(screen.getByRole('dialog', { name: 'Оставить отзыв' })).toBeInTheDocument()
    expect(screen.getByText('Стрижка у мастера Анна')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Отправить отзыв' })).toBeDisabled()
  })

  it('closes on Escape', async () => {
    const onClose = renderModal()
    await userEvent.keyboard('{Escape}')
    expect(onClose).toHaveBeenCalledTimes(1)
  })
})
