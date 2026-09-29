import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { OrderCard } from './OrderCard'
import { staffCard } from './testData'

const NOW = '2026-10-05T10:12:30Z'

describe('OrderCard', () => {
  it('offers exactly the actions the server lists — the client derives nothing from the status', () => {
    const { rerender } = render(<OrderCard order={staffCard({ availableActions: ['Accept', 'Reject', 'Edit'] })} serverNow={NOW} highlighted={false} busy={false} onAction={() => {}} />)
    expect(screen.getByRole('button', { name: 'Принять' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Отклонить' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Изменить' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Выдать' })).toBeNull()

    // status says «Ready» but the server lists only Cancel: only Cancel (+ the journal) is shown
    rerender(<OrderCard order={staffCard({ status: 'Ready', availableActions: ['Cancel'] })} serverNow={NOW} highlighted={false} busy={false} onAction={() => {}} />)
    expect(screen.getByRole('button', { name: 'Отменить' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Выдать' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Не забран' })).toBeNull()
  })

  it('reports the chosen action with the order', async () => {
    const onAction = vi.fn()
    const order = staffCard()
    render(<OrderCard order={order} serverNow={NOW} highlighted={false} busy={false} onAction={onAction} />)
    await userEvent.setup().click(screen.getByRole('button', { name: 'Принять' }))
    expect(onAction).toHaveBeenCalledWith('accept', order)
  })

  it('shows age by the server clock, a dial link, ≈ for weighed totals and the «изменён» mark', () => {
    render(<OrderCard order={staffCard({ isModified: true })} serverNow={NOW} highlighted={false} busy={false} onAction={() => {}} />)
    expect(screen.getByText('12 мин назад')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /\+7 \(900\) 123-45-67/ })).toHaveAttribute('href', 'tel:+79001234567')
    expect(screen.getByText('≈ 770 ₽')).toBeInTheDocument()
    expect(screen.getByText('изменён')).toBeInTheDocument()
    expect(screen.getByText('500 г')).toBeInTheDocument()
  })

  it('marks a new order with text, not only a frame', () => {
    render(<OrderCard order={staffCard()} serverNow={NOW} highlighted busy={false} onAction={() => {}} />)
    expect(screen.getByText('Новый')).toBeInTheDocument()
    expect(screen.getByTestId('order-card-27')).toHaveAttribute('data-highlighted', 'true')
  })

  it('says the buyer data is erased instead of printing nulls', () => {
    render(<OrderCard order={staffCard({ customerName: null, customerPhone: null })} serverNow={NOW} highlighted={false} busy={false} onAction={() => {}} />)
    expect(screen.getByText('Данные покупателя удалены')).toBeInTheDocument()
  })

  it('has no action buttons in the read-only «завершённые» list', () => {
    render(<OrderCard order={staffCard({ status: 'Issued', statusText: 'Выдан', availableActions: [] })} serverNow={NOW} highlighted={false} busy={false} readOnly onAction={() => {}} />)
    expect(screen.queryByRole('button', { name: 'Принять' })).toBeNull()
    expect(screen.getByText('Выдан')).toBeInTheDocument()
  })
})
