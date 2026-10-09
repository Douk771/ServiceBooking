// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { chosenItems, clampQuantity, isTimeGone, keepHours, toSelectionInput } from './serviceSelection'

// ЮР39-6 / Т39-05: nothing is pre-selected, positions default to 0 and only chosen ones are sent.
describe('serviceSelection', () => {
  it('sends only the positions with a quantity above zero', () => {
    expect(chosenItems({ a: 0, b: 2, c: 0 }, ['a', 'b', 'c'])).toEqual([{ itemId: 'b', quantity: 2 }])
    expect(chosenItems({ a: 0 })).toEqual([])
  })

  it('builds the selection body without items by default', () => {
    expect(toSelectionInput({ businessDate: '2027-01-15', startMinute: 1320, hours: 3, quantities: { a: 0 } })).toEqual({
      businessDate: '2027-01-15',
      startMinute: 1320,
      hours: 3,
      items: [],
    })
  })

  it('keeps the hours only when the new start can hold them', () => {
    expect(keepHours(3, [{ hours: 2 }, { hours: 3 }])).toBe(3)
    expect(keepHours(4, [{ hours: 2 }, { hours: 3 }])).toBeNull()
    expect(keepHours(null, [{ hours: 2 }])).toBeNull()
  })

  it('clamps the quantity to 0…max and rejects garbage', () => {
    expect(clampQuantity(7, 5)).toBe(5)
    expect(clampQuantity(-1, 5)).toBe(0)
    expect(clampQuantity(Number.NaN, 5)).toBe(0)
    expect(clampQuantity(2.9, 5)).toBe(2)
  })

  it('treats a taken or unavailable time as gone, a changed price as not gone', () => {
    expect(isTimeGone('SlotTaken')).toBe(true)
    expect(isTimeGone('OutsideStay')).toBe(true)
    expect(isTimeGone('PriceChanged')).toBe(false)
    expect(isTimeGone('ItemUnavailable')).toBe(false)
  })
})
