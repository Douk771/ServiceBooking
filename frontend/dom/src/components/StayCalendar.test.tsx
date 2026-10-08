import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { StayCalendar } from './StayCalendar'
import type { HouseCalendarDto } from '../types'
import { addDays, nightDates } from '../utils/stayDates'
import type { StayRange } from '../utils/staySelection'

const TODAY = '2027-01-01'

function calendar(taken: Record<string, 'Occupied' | 'MayFreeUp'> = {}): HouseCalendarDto {
  return {
    houseId: 'h',
    today: TODAY,
    from: TODAY,
    to: addDays(TODAY, 200),
    minNights: 2,
    maxNights: 30,
    allowGapFill: false,
    allowSameDayCheckIn: true,
    lastNight: addDays(TODAY, 199),
    days: nightDates(TODAY, addDays(TODAY, 200)).map((date) => ({ date, state: taken[date] ?? ('Free' as const), priceRub: 5000 })),
  }
}

const cell = (name: RegExp) => screen.getByRole('button', { name })

describe('StayCalendar', () => {
  it('every cell says its state in words (never colour alone) and a free cell says the price', () => {
    render(<StayCalendar calendar={calendar({ '2027-01-10': 'Occupied', '2027-01-12': 'MayFreeUp' })} value={{ checkIn: null, checkOut: null }} onChange={() => undefined} />)
    expect(cell(/, 10 января,.*Занято/)).toBeInTheDocument()
    expect(cell(/, 12 января,.*Возможно освободится/)).toBeInTheDocument()
    expect(cell(/, 5 января,.*Свободно.*5\s000 ₽ за ночь/)).toBeInTheDocument()
  })

  it('picks check-in then check-out with the mouse', () => {
    const onChange = vi.fn()
    const { rerender } = render(<StayCalendar calendar={calendar()} value={{ checkIn: null, checkOut: null }} onChange={onChange} />)
    fireEvent.click(cell(/, 5 января,/))
    expect(onChange).toHaveBeenLastCalledWith({ checkIn: '2027-01-05', checkOut: null })
    rerender(<StayCalendar calendar={calendar()} value={{ checkIn: '2027-01-05', checkOut: null }} onChange={onChange} />)
    expect(screen.getByText('Теперь выберите дату выезда')).toBeInTheDocument()
    fireEvent.click(cell(/, 8 января,/))
    expect(onChange).toHaveBeenLastCalledWith({ checkIn: '2027-01-05', checkOut: '2027-01-08' })
  })

  it('says why a range is refused and keeps the selection', () => {
    const onChange = vi.fn()
    const onMessage = vi.fn()
    render(
      <StayCalendar
        calendar={calendar({ '2027-01-10': 'Occupied' })}
        value={{ checkIn: '2027-01-08', checkOut: null }}
        onChange={onChange}
        onMessage={onMessage}
      />,
    )
    fireEvent.click(cell(/, 12 января,/))
    expect(onChange).not.toHaveBeenCalled()
    expect(screen.getByRole('status')).toHaveTextContent('Эти даты уже заняты. Выберите другие')
    expect(onMessage).toHaveBeenCalledWith('Эти даты уже заняты. Выберите другие')
  })

  it('is driven from the keyboard: arrows move the focus, Enter picks, Escape clears', () => {
    const onChange = vi.fn()
    const value: StayRange = { checkIn: null, checkOut: null }
    render(<StayCalendar calendar={calendar()} value={value} onChange={onChange} />)
    const start = cell(/, 1 января,/)
    start.focus()
    fireEvent.keyDown(start, { key: 'ArrowRight' })
    expect(document.activeElement).toBe(cell(/, 2 января,/))
    fireEvent.keyDown(document.activeElement!, { key: 'ArrowDown' })
    expect(document.activeElement).toBe(cell(/, 9 января,/))
    fireEvent.click(document.activeElement!) // Enter/Space on a <button> dispatch a click
    expect(onChange).toHaveBeenLastCalledWith({ checkIn: '2027-01-09', checkOut: null })
    fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
    expect(onChange).toHaveBeenLastCalledWith({ checkIn: null, checkOut: null })
  })

  it('one tab stop per month grid (roving tabindex)', () => {
    render(<StayCalendar calendar={calendar()} value={{ checkIn: null, checkOut: null }} onChange={() => undefined} />)
    const grid = screen.getByRole('grid')
    expect(grid.querySelectorAll('button[tabindex="0"]')).toHaveLength(1)
  })

  it('month arrows stay inside the bookable period', () => {
    render(<StayCalendar calendar={calendar()} value={{ checkIn: null, checkOut: null }} onChange={() => undefined} />)
    expect(screen.getByRole('button', { name: 'Предыдущий месяц' })).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: 'Следующий месяц' }))
    expect(screen.getByRole('grid', { name: 'Февраль 2027' })).toBeInTheDocument()
  })
})
