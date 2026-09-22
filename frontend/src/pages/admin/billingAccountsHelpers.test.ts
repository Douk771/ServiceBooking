import { describe, it, expect } from 'vitest'
import {
  optionRowsToPayload,
  computeExpectedTotal,
  buildAssignInput,
  isPaidUntilMissing,
  formatRub,
  type AssignOptionRow,
} from './billingAccountsHelpers'

const toggleRow: AssignOptionRow = {
  optionId: 'opt-toggle',
  name: 'Аналитика',
  kind: 'Toggle',
  pricePerMonth: 300,
  selected: true,
  quantity: '1',
}

const qtyRow: AssignOptionRow = {
  optionId: 'opt-qty',
  name: 'Номер WhatsApp',
  kind: 'Quantity',
  unitName: 'номер',
  pricePerMonth: 500,
  selected: true,
  quantity: '3',
}

describe('optionRowsToPayload', () => {
  it('sends only selected rows', () => {
    const rows = [toggleRow, { ...qtyRow, selected: false }]
    expect(optionRowsToPayload(rows)).toEqual([{ optionId: 'opt-toggle', quantity: 1 }])
  })

  it('forces quantity 1 for Toggle options regardless of the stored quantity field', () => {
    const rows = [{ ...toggleRow, quantity: '5' }]
    expect(optionRowsToPayload(rows)).toEqual([{ optionId: 'opt-toggle', quantity: 1 }])
  })

  it('parses the quantity for Quantity options, flooring invalid input at 1', () => {
    expect(optionRowsToPayload([qtyRow])).toEqual([{ optionId: 'opt-qty', quantity: 3 }])
    expect(optionRowsToPayload([{ ...qtyRow, quantity: '' }])).toEqual([{ optionId: 'opt-qty', quantity: 1 }])
    expect(optionRowsToPayload([{ ...qtyRow, quantity: '0' }])).toEqual([{ optionId: 'opt-qty', quantity: 1 }])
  })
})

describe('computeExpectedTotal', () => {
  it('sums plan price plus unit price times quantity for every selected option', () => {
    const total = computeExpectedTotal(1000, [toggleRow, qtyRow], { 'opt-toggle': 300, 'opt-qty': 500 })
    expect(total).toBe(1000 + 300 * 1 + 500 * 3)
  })

  it('ignores unselected rows entirely', () => {
    const total = computeExpectedTotal(1000, [{ ...qtyRow, selected: false }], { 'opt-qty': 500 })
    expect(total).toBe(1000)
  })
})

describe('buildAssignInput', () => {
  it('turns blank amount/comment into null rather than empty strings', () => {
    const input = buildAssignInput({
      planId: 'plan-1',
      isActive: true,
      paidUntil: '2026-01-01',
      rows: [toggleRow],
      amount: '',
      comment: '   ',
      confirmLimitOverflow: false,
    })
    expect(input.amount).toBeNull()
    expect(input.comment).toBeNull()
    expect(input.options).toEqual([{ optionId: 'opt-toggle', quantity: 1 }])
  })

  it('passes requestId through so approving a request closes it in the same call', () => {
    const input = buildAssignInput({
      planId: null,
      isActive: false,
      paidUntil: null,
      rows: [],
      amount: '1500',
      comment: 'Счёт 42',
      requestId: 'req-1',
      confirmLimitOverflow: true,
    })
    expect(input).toEqual({
      planId: null,
      isActive: false,
      paidUntil: null,
      options: [],
      amount: 1500,
      comment: 'Счёт 42',
      requestId: 'req-1',
      confirmLimitOverflow: true,
    })
  })
})

describe('isPaidUntilMissing', () => {
  it('requires a date whenever a paid plan is selected', () => {
    expect(isPaidUntilMissing('plan-1', '')).toBe(true)
  })

  it('is satisfied once a date is present for a paid plan', () => {
    expect(isPaidUntilMissing('plan-1', '2026-01-01')).toBe(false)
  })

  it('never requires a date for the free plan (empty planId)', () => {
    expect(isPaidUntilMissing('', '')).toBe(false)
  })
})

describe('buildAssignInput — free plan never carries an expiry', () => {
  it('forces paidUntil to null when planId is null (free plan), even if a stale date is still in state', () => {
    const input = buildAssignInput({
      planId: null,
      isActive: true,
      paidUntil: '2026-01-01',
      rows: [],
      amount: '',
      comment: '',
      confirmLimitOverflow: false,
    })
    expect(input.planId).toBeNull()
    expect(input.paidUntil).toBeNull()
  })

  it('keeps the given date for a paid plan', () => {
    const input = buildAssignInput({
      planId: 'plan-1',
      isActive: true,
      paidUntil: '2026-01-01',
      rows: [],
      amount: '',
      comment: '',
      confirmLimitOverflow: false,
    })
    expect(input.paidUntil).toBe('2026-01-01')
  })
})

describe('formatRub', () => {
  it('formats whole rubles with a ru-RU thousands separator and no kopecks', () => {
    expect(formatRub(12345)).toBe(`${(12345).toLocaleString('ru-RU')} ₽`)
  })

  it('rounds fractional values to the nearest ruble', () => {
    expect(formatRub(999.6)).toBe(`${(1000).toLocaleString('ru-RU')} ₽`)
  })
})
