// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { applyDayTo, crossesMidnight, emptyWeek, firstWeekError, intervalFieldError, isValidTime, weekFromDto, weekToInput } from './hours'
import type { WorkingDayDto } from '../types'

describe('working hours form model', () => {
  it('validates HH:mm and the 5-minute grid with the server wording', () => {
    expect(isValidTime('09:00')).toBe(true)
    expect(isValidTime('9:00')).toBe(false)
    expect(isValidTime('24:00')).toBe(false)
    expect(intervalFieldError({ start: '09:00', end: '21:00' })).toBeNull()
    expect(intervalFieldError({ start: '', end: '21:00' })).toBe('Укажите время в формате ЧЧ:ММ')
    expect(intervalFieldError({ start: '09:03', end: '21:00' })).toBe('Время указывается с шагом 5 минут')
    expect(intervalFieldError({ start: '09:00', end: '09:00' })).toBe('Интервал не может быть нулевой длины')
  })

  it('treats end <= start as «через полночь» (also 22:00–00:00)', () => {
    expect(crossesMidnight({ start: '18:00', end: '03:00' })).toBe(true)
    expect(crossesMidnight({ start: '22:00', end: '00:00' })).toBe(true)
    expect(crossesMidnight({ start: '09:00', end: '21:00' })).toBe(false)
  })

  it('round-trips a server week and always sends all seven days, days off as empty lists', () => {
    const days = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'].map((d) => ({
      dayOfWeek: d,
      dayLabel: d,
      text: '',
      intervals: d === 'Monday' ? [{ start: '09:00', end: '14:00', crossesMidnight: false }, { start: '15:00', end: '21:00', crossesMidnight: false }] : [],
    })) as WorkingDayDto[]
    const week = weekFromDto(days)
    const input = weekToInput(week)
    expect(input.days).toHaveLength(7)
    expect(input.days[0]).toEqual({ dayOfWeek: 'Monday', intervals: [{ start: '09:00', end: '14:00' }, { start: '15:00', end: '21:00' }] })
    expect(input.days[6]).toEqual({ dayOfWeek: 'Sunday', intervals: [] })
  })

  it('reports the first field problem in the week and refuses a fourth interval', () => {
    const w = emptyWeek()
    expect(firstWeekError(w)).toBeNull()
    w.Tuesday = [{ start: '09:00', end: '10:00' }, { start: '11:00', end: '12:00' }, { start: '13:00', end: '14:00' }, { start: '15:00', end: '16:00' }]
    expect(firstWeekError(w)).toBe('В дне не больше трёх интервалов')
    w.Tuesday = [{ start: '09:00', end: '10:07' }]
    expect(firstWeekError(w)).toBe('Время указывается с шагом 5 минут')
  })

  it('copies a day onto other days without aliasing the interval objects', () => {
    const w = emptyWeek()
    w.Monday = [{ start: '09:00', end: '21:00' }]
    const next = applyDayTo(w, 'Monday', ['Monday', 'Tuesday', 'Wednesday'])
    expect(next.Tuesday).toEqual([{ start: '09:00', end: '21:00' }])
    next.Tuesday[0].end = '20:00'
    expect(next.Wednesday[0].end).toBe('21:00')
    expect(w.Tuesday).toEqual([])
  })
})
