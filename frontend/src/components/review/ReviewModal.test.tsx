import { describe, it, expect, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ReviewModal } from './ReviewModal'
import { reviewsApi } from '../../api/reviews'

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

  it('cannot be closed while the review is being sent', async () => {
    // Review of cycle 22: the pre-§377 modal could not be closed during submit («Отмена» disabled) —
    // the shared <Modal> must not reopen that door via Esc, the backdrop or the X.
    vi.mocked(reviewsApi.submit).mockReturnValue(new Promise(() => {}))
    const onClose = renderModal()
    const stars = screen.getAllByRole('button').filter((b) => !b.textContent && !b.getAttribute('aria-label'))
    await userEvent.click(stars[4])
    await userEvent.click(screen.getByRole('button', { name: 'Отправить отзыв' }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Отмена' })).toBeDisabled())

    await userEvent.keyboard('{Escape}')
    expect(screen.getByRole('button', { name: 'Закрыть' })).toBeDisabled()
    await userEvent.click(screen.getByRole('dialog').parentElement!)
    expect(onClose).not.toHaveBeenCalled()
    expect(screen.getByRole('dialog', { name: 'Оставить отзыв' })).toBeInTheDocument()
  })
})
