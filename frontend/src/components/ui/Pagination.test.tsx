import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Pagination } from './Pagination'

describe('Pagination', () => {
  it('renders nothing when everything fits on page 1', () => {
    const { container } = render(
      <Pagination page={1} pageSize={20} total={12} hasNext={false} onPageChange={vi.fn()} />,
    )
    expect(container).toBeEmptyDOMElement()
  })

  it('shows the range and disables "previous" on page 1', () => {
    render(<Pagination page={1} pageSize={20} total={45} hasNext onPageChange={vi.fn()} />)
    expect(screen.getByText('1–20 из 45')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Предыдущая страница' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Следующая страница' })).toBeEnabled()
  })

  it('disables "next" when hasNext is false, even mid-range', () => {
    render(<Pagination page={3} pageSize={20} total={45} hasNext={false} onPageChange={vi.fn()} />)
    expect(screen.getByText('41–45 из 45')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Следующая страница' })).toBeDisabled()
  })

  it('calls onPageChange with page ± 1', async () => {
    const user = userEvent.setup()
    const onPageChange = vi.fn()
    render(<Pagination page={2} pageSize={20} total={45} hasNext onPageChange={onPageChange} />)

    await user.click(screen.getByRole('button', { name: 'Следующая страница' }))
    expect(onPageChange).toHaveBeenCalledWith(3)

    await user.click(screen.getByRole('button', { name: 'Предыдущая страница' }))
    expect(onPageChange).toHaveBeenCalledWith(1)
  })
})
