// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { BLOCK_KINDS, blockHouses, toApiEnd, toBlockInput, toLastNight, validateBlock } from './blockForm'

const ok = { houseIds: ['h1'], firstNight: '2027-02-10', lastNight: '2027-02-12', comment: '' }
const TODAY = '2027-02-01'

describe('block form: nights «с … по …» ↔ the API check-out date', () => {
  it('the API end date is the day AFTER the last night, and back', () => {
    expect(toApiEnd('2027-02-12')).toBe('2027-02-13')
    expect(toLastNight('2027-02-13')).toBe('2027-02-12')
    expect(toApiEnd('2027-02-28')).toBe('2027-03-01')
  })

  it('builds the request: one night is a block of [d, d+1)', () => {
    expect(toBlockInput('h1', { firstNight: '2027-02-10', lastNight: '2027-02-10', kind: 'Repair', comment: '  Ремонт крыши ' })).toEqual({
      houseId: 'h1',
      startDate: '2027-02-10',
      endDate: '2027-02-11',
      kind: 'Repair',
      comment: 'Ремонт крыши',
    })
    expect(toBlockInput('h1', { firstNight: '2027-02-10', lastNight: '2027-02-12', kind: 'Other', comment: ' ' }).comment).toBeNull()
  })

  it('validation texts are the server’s', () => {
    expect(validateBlock(ok, TODAY, { editing: false })).toEqual({})
    expect(validateBlock({ ...ok, houseIds: [] }, TODAY, { editing: false }).houses).toBeTruthy()
    expect(validateBlock({ ...ok, lastNight: '' }, TODAY, { editing: false }).dates).toBe('Укажите даты')
    expect(validateBlock({ ...ok, lastNight: '2027-02-09' }, TODAY, { editing: false }).dates).toBeTruthy()
    expect(validateBlock({ ...ok, firstNight: '2027-01-30', lastNight: '2027-02-02' }, TODAY, { editing: false }).dates).toBe('Нельзя блокировать прошедшие даты')
    expect(validateBlock({ ...ok, comment: 'а'.repeat(301) }, TODAY, { editing: false }).comment).toBe('Комментарий — не длиннее 300 символов')
  })

  it('past dates are refused for a new block but an existing one may be edited (only new nights are checked server-side)', () => {
    expect(validateBlock({ ...ok, firstNight: '2027-01-30', lastNight: '2027-02-02' }, TODAY, { editing: true })).toEqual({})
  })

  it('366 nights is the limit', () => {
    expect(validateBlock({ ...ok, firstNight: '2027-02-10', lastNight: '2028-02-10' }, TODAY, { editing: false }).dates).toBeUndefined() // 366 nights
    expect(validateBlock({ ...ok, firstNight: '2027-02-10', lastNight: '2028-02-11' }, TODAY, { editing: false }).dates).toBe('Блокировка — не длиннее 366 ночей')
  })

  it('kinds', () => {
    expect(BLOCK_KINDS.map((k) => k.value)).toEqual(['Repair', 'Personal', 'Other'])
  })
})

describe('blocking several houses (P1) — one request per house, a failure does not stop the rest', () => {
  it('reports each house with its own reason', async () => {
    const sent: string[] = []
    const res = await blockHouses(
      ['a', 'b', 'c'],
      async (id) => {
        sent.push(id)
        if (id === 'b') throw new Error('Даты заняты бронью Анны, 10.02–13.02')
      },
      (e) => (e as Error).message,
    )
    expect(sent).toEqual(['a', 'b', 'c'])
    expect(res).toEqual([
      { houseId: 'a', ok: true },
      { houseId: 'b', ok: false, error: 'Даты заняты бронью Анны, 10.02–13.02' },
      { houseId: 'c', ok: true },
    ])
  })
})
