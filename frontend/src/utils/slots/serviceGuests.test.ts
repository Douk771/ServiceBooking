import { describe, it, expect } from 'vitest'
import { guestsCountProblem, toCreateOrderInput } from './serviceOrderForm'
import type { ServiceQuoteDto } from '@/types/slots'

describe('guestsCountProblem (Т42-06: whole number 1…capacity, no default)', () => {
  it.each([['1'], ['6'], [' 4 ']])('accepts %j for a capacity of 6', (raw) => {
    expect(guestsCountProblem(raw, 6)).toBeNull()
  })

  it.each([[''], ['  '], ['0'], ['7'], ['-1'], ['2.5'], ['2,5'], ['abc'], ['1e1']])('rejects %j for a capacity of 6', (raw) => {
    expect(guestsCountProblem(raw, 6)).toBe('Укажите число гостей — от 1 до 6')
  })
})

describe('toCreateOrderInput and guestsCount', () => {
  const base = {
    pick: { businessDate: '2027-01-15', startMinute: 1080, hours: 2, quantities: {} },
    order: [] as string[],
    guest: { name: ' Анна ', phone: '79001112233', comment: '', notifyByMessenger: false },
    anonymous: true,
    quote: { totalRub: 4000 } as ServiceQuoteDto,
    idempotencyKey: 'k1',
    captchaToken: '',
  }

  it('sends the number of guests only when it is given', () => {
    expect(toCreateOrderInput({ ...base, guestsCount: 4 }).guestsCount).toBe(4)
    expect('guestsCount' in toCreateOrderInput(base)).toBe(false)
    expect('guestsCount' in toCreateOrderInput({ ...base, guestsCount: null })).toBe(false)
  })
})
