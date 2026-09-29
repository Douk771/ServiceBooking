import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ProductModal } from './ProductModal'

const createProduct = vi.fn()
vi.mock('../../api/catalog', () => ({ catalogApi: { createProduct: (...a: unknown[]) => createProduct(...a), updateProduct: vi.fn() } }))

function renderModal() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const onClose = vi.fn()
  render(
    <QueryClientProvider client={qc}>
      <ProductModal shopId="s1" categories={[]} onClose={onClose} onSaved={() => {}} />
    </QueryClientProvider>,
  )
  return onClose
}

beforeEach(() => createProduct.mockReset().mockResolvedValue({ id: 'p1' }))

describe('ProductModal — weekdays (cycle 24)', () => {
  it('sells every day by default and sends the chosen days', async () => {
    const user = userEvent.setup()
    renderModal()
    await user.type(screen.getByLabelText('Название *'), 'Суп дня')
    await user.type(screen.getByLabelText('Цена за штуку, ₽ *'), '250')
    expect(screen.getAllByRole('checkbox', { name: /^(Понедельник|Вторник|Среда|Четверг|Пятница|Суббота|Воскресенье)$/ }).every((c) => (c as HTMLInputElement).checked)).toBe(true)
    await user.click(screen.getByRole('checkbox', { name: 'Суббота' }))
    await user.click(screen.getByRole('checkbox', { name: 'Воскресенье' }))
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    expect(createProduct.mock.calls[0][1].availableWeekdays).toEqual(['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'])
  })

  it('allows «только по меню» — an empty selection is sent as an empty list, not as all days', async () => {
    const user = userEvent.setup()
    renderModal()
    await user.type(screen.getByLabelText('Название *'), 'Суп дня')
    await user.type(screen.getByLabelText('Цена за штуку, ₽ *'), '250')
    for (const d of ['Понедельник', 'Вторник', 'Среда', 'Четверг', 'Пятница', 'Суббота', 'Воскресенье']) await user.click(screen.getByRole('checkbox', { name: d }))
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    expect(createProduct.mock.calls[0][1].availableWeekdays).toEqual([])
  })
})
