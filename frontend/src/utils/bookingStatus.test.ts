import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { createElement } from 'react'
import { BOOKING_STATUS_LABELS, bookingStatusLabel } from './bookingStatus'
import { StatusBadge } from '../components/ui/Badge'

describe('booking status captions (cycle 22 — one table)', () => {
  it('uses the wording masters see on "Мои записи" / "Клиенты"', () => {
    expect(BOOKING_STATUS_LABELS).toEqual({
      Pending: 'Ожидает',
      Confirmed: 'Подтверждена',
      Completed: 'Выполнена',
      Cancelled: 'Отменена',
      NoShow: 'Не пришёл',
    })
  })

  it('bookingStatusLabel falls back to the raw value for an unknown status', () => {
    expect(bookingStatusLabel('Completed')).toBe('Выполнена')
    expect(bookingStatusLabel('SomethingNew')).toBe('SomethingNew')
  })

  it('StatusBadge reads the same table', () => {
    render(createElement(StatusBadge, { status: 'Confirmed' }))
    expect(screen.getByText('Подтверждена')).toBeInTheDocument()
  })
})
