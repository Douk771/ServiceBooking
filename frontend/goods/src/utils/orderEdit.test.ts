import { describe, it, expect } from 'vitest'
import { isEdited, linesFromOrder, previewEditTotal, toEditPayload, validateEdit, type EditLine } from './orderEdit'
import { parseActualGrams } from './issue'
import type { StaffOrderItemDto } from '../types'

const items = [
  { id: 'i1', name: 'Шаурма', unit: 'Piece', unitPrice: 250, quantityOrdered: 2, lineTotal: 500, isApproximate: false },
  { id: 'i2', name: 'Сыр', unit: 'Weight', unitPrice: 540, quantityOrdered: 500, weightStepGrams: 50, lineTotal: 270, isApproximate: true },
] as unknown as StaffOrderItemDto[]

describe('order edit helpers', () => {
  it('builds lines with the snapshot step for weighed items', () => {
    const lines = linesFromOrder(items)
    expect(lines.map((l) => [l.step, l.max])).toEqual([[1, 99], [50, 10000]])
  })
  it('sends the full composition: itemId for old lines, productId for new ones', () => {
    const lines: EditLine[] = [
      ...linesFromOrder(items),
      { key: 'n', productId: 'p9', name: 'Кола', unit: 'Piece', unitPrice: 100, quantity: 1, step: 1, max: 99 },
    ]
    expect(toEditPayload(lines)).toEqual([
      { itemId: 'i1', quantity: 2 },
      { itemId: 'i2', quantity: 500 },
      { productId: 'p9', quantity: 1 },
    ])
  })
  it('previews the total by the shared money rule and flags weighed lines as approximate', () => {
    expect(previewEditTotal(linesFromOrder(items))).toEqual({ total: 770, isApproximate: true })
  })
  it('detects an untouched dialog', () => {
    const lines = linesFromOrder(items)
    expect(isEdited(items, lines, '')).toBe(false)
    expect(isEdited(items, lines, 'нет сыра')).toBe(true)
    expect(isEdited(items, lines.slice(0, 1), '')).toBe(true)
    expect(isEdited(items, [{ ...lines[0], quantity: 3 }, lines[1]], '')).toBe(true)
  })
  it('refuses an empty order and non-positive quantities', () => {
    expect(validateEdit([])).toMatch(/Пустой заказ/)
    const lines = linesFromOrder(items)
    expect(validateEdit([{ ...lines[0], quantity: 0 }])).toMatch(/Укажите количество/)
    expect(validateEdit([{ ...lines[0], quantity: 100 }])).toMatch(/Слишком много/)
    expect(validateEdit(lines)).toBeNull()
  })
})

describe('parseActualGrams', () => {
  it('accepts integers 1…100000 without a step requirement', () => {
    expect(parseActualGrams('512')).toBe(512)
    expect(parseActualGrams('100000')).toBe(100000)
    expect(parseActualGrams('1')).toBe(1)
  })
  it('rejects empty, zero, decimals, negatives and too large values', () => {
    for (const bad of ['', '0', '1.5', '-3', '100001', 'abc']) expect(parseActualGrams(bad)).toBeNull()
  })
})
