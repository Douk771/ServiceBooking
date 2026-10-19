import { describe, it, expect } from 'vitest'
import { buildPreviewBody, buildSaveBody, excludeMessengerOptions, historyLine, parseMonths, parsePriceInput, priceToInput, validateComment } from './messengerOptionsHelpers'
import type { AdminMessengerOption, MessengerPriceChange } from '../../types'

const option = (o: Partial<AdminMessengerOption> = {}): AdminMessengerOption => ({
  transport: 'Max', label: 'MAX', optionId: 'o1', open: true, isActive: true, pricePerMonth: 490, priceText: '490 ₽/мес',
  sale: { sellable: true, reason: null, text: 'Продаётся' }, activePaidAccounts: 2, history: [], ...o,
})

describe('parsePriceInput', () => {
  it('empty means «снять цену»', () => expect(parsePriceInput('  ')).toEqual({ ok: true, value: null }))
  it.each([['490', 490], ['490,5', 490.5], ['490.50', 490.5], ['1 490', 1490], ['0', 0]])('%s', (raw, v) =>
    expect(parsePriceInput(raw)).toEqual({ ok: true, value: v }))
  it('rejects letters, a negative sign and three decimals', () => {
    expect(parsePriceInput('abc').ok).toBe(false)
    expect(parsePriceInput('-5').ok).toBe(false)
    expect(parsePriceInput('1,234')).toEqual({ ok: false, error: 'Цена — не больше двух знаков после запятой' })
  })
  it('rejects above the ceiling', () => expect(parsePriceInput('100000000').ok).toBe(false))
})

describe('priceToInput', () => {
  it('uses a decimal comma and an empty string for «не задана»', () => {
    expect(priceToInput(490.5)).toBe('490,5')
    expect(priceToInput(null)).toBe('')
  })
})

describe('request bodies', () => {
  it('sends isActive only when it was flipped', () => {
    expect(buildPreviewBody(option(), 590, true)).toEqual({ pricePerMonth: 590, isActive: null })
    expect(buildPreviewBody(option(), 590, false)).toEqual({ pricePerMonth: 590, isActive: false })
  })
  it('the save body carries the values the consequences were computed from and a trimmed reason', () => {
    expect(buildSaveBody(option({ pricePerMonth: null, isActive: false }), 300, true, '  запуск ')).toEqual({
      pricePerMonth: 300, isActive: true, comment: 'запуск', expectedPricePerMonth: null, expectedIsActive: false,
    })
  })
})

describe('validateComment', () => {
  it('requires 1..500 characters after trim', () => {
    expect(validateComment('   ')).toBe('Укажите причину изменения цены')
    expect(validateComment('x'.repeat(501))).toBe('Причина — не длиннее 500 символов')
    expect(validateComment('ok')).toBeNull()
  })
})

describe('historyLine', () => {
  const h: MessengerPriceChange = {
    changedAt: '2026-10-19T10:00:00Z', changedByName: 'Иван', oldPricePerMonth: 490, newPricePerMonth: 590, oldPriceText: '490 ₽/мес',
    newPriceText: '590 ₽/мес', oldIsActive: true, newIsActive: false, comment: 'индексация', source: 'MessengerPriceScreen', activePaidAccounts: 3,
  }
  it('names the author, the price move, the flipped flag and the reason', () =>
    expect(historyLine(h, () => '19.10.2026')).toBe('19.10.2026 · Иван: 490 ₽/мес → 590 ₽/мес; опция выключена · индексация'))
  it('works without author and reason', () =>
    expect(historyLine({ ...h, changedByName: null, comment: null, newIsActive: true }, () => 'd')).toBe('d: 490 ₽/мес → 590 ₽/мес'))
})

describe('excludeMessengerOptions', () => {
  const opts = [{ id: 'a', code: 'notifications.whatsapp' }, { id: 'b', code: 'x.y' }, { id: 'c', code: 'Notifications.MAX' }, { id: 'd', code: 'extra.seat' }]
  it('drops the options by id from the messenger answer and by code as a fallback', () => {
    expect(excludeMessengerOptions(opts, ['b']).map((o) => o.id)).toEqual(['d'])
    expect(excludeMessengerOptions(opts, []).map((o) => o.id)).toEqual(['b', 'd'])
  })
  it('keeps everything that is not a messenger and ignores null ids', () => {
    expect(excludeMessengerOptions([{ id: 'z', code: 'extra' }], [null])).toHaveLength(1)
  })
})

describe('parseMonths', () => {
  it('accepts a whole number within 1..24 only', () => {
    expect(parseMonths('3')).toBe(3)
    expect(parseMonths(' 24 ')).toBe(24)
    for (const bad of ['', '0', '25', '1.5', '-1', 'abc']) expect(parseMonths(bad)).toBeNull()
  })
  it('honours another ceiling', () => expect(parseMonths('13', 12)).toBeNull())
})
