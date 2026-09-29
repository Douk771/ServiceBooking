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

describe('OrderCard — pick-up time (cycle 24)', () => {
  const overduePickup = { kind: 'Asap', date: '2026-10-05', startUtc: '2026-10-05T10:00:00Z', dueUtc: '2026-10-05T10:10:00Z', text: 'К 13:10', isPreorder: false, isOverdue: false } as const

  it('prints the pick-up text large and «Просрочен» as a word once the server clock passes dueUtc', () => {
    const { rerender } = render(<OrderCard order={staffCard({ status: 'Accepted', pickup: overduePickup })} serverNow={NOW} nowMs={new Date('2026-10-05T10:09:00Z').getTime()} highlighted={false} busy={false} onAction={() => {}} />)
    expect(screen.getByTestId('pickup-text')).toHaveTextContent('К 13:10')
    expect(screen.queryByText('Просрочен')).toBeNull()
    rerender(<OrderCard order={staffCard({ status: 'Accepted', pickup: overduePickup })} serverNow={NOW} nowMs={new Date('2026-10-05T10:11:00Z').getTime()} highlighted={false} busy={false} onAction={() => {}} />)
    expect(screen.getByText('Просрочен')).toBeInTheDocument()
  })

  it('never marks a finished order overdue', () => {
    render(<OrderCard order={staffCard({ status: 'Issued', availableActions: [], pickup: overduePickup })} serverNow={NOW} nowMs={new Date('2026-10-05T12:00:00Z').getTime()} highlighted={false} busy={false} readOnly onAction={() => {}} />)
    expect(screen.queryByText('Просрочен')).toBeNull()
  })

  it('offers «Изменить время» only when the server lists ChangePickup, and reports it', async () => {
    const onAction = vi.fn()
    const order = staffCard({ availableActions: ['Accept', 'ChangePickup'] })
    const { rerender } = render(<OrderCard order={order} serverNow={NOW} highlighted={false} busy={false} onAction={onAction} />)
    await userEvent.setup().click(screen.getByRole('button', { name: 'Изменить время' }))
    expect(onAction).toHaveBeenCalledWith('changePickup', order)
    rerender(<OrderCard order={staffCard({ availableActions: ['Accept'] })} serverNow={NOW} highlighted={false} busy={false} onAction={onAction} />)
    expect(screen.queryByRole('button', { name: 'Изменить время' })).toBeNull()
  })

  it('shows the messenger delivery status only when a message was requested', () => {
    const { rerender } = render(<OrderCard order={staffCard({ notifyByMessenger: true, messenger: { requested: true, status: 'Failed', statusText: 'Не доставлено' } })} serverNow={NOW} highlighted={false} busy={false} onAction={() => {}} />)
    expect(screen.getByTestId('messenger-status')).toHaveTextContent('Не доставлено')
    rerender(<OrderCard order={staffCard({ messenger: { requested: false } })} serverNow={NOW} highlighted={false} busy={false} onAction={() => {}} />)
    expect(screen.queryByTestId('messenger-status')).toBeNull()
  })
})
