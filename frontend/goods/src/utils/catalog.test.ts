// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { filterProducts, groupProducts, moveItem } from './catalogGroups'
import { parsePrice, validateProduct } from './productForm'
import { parseStockInput, stockToInput } from './stockInput'
import type { CategoryDto, ProductDto } from '../types'

const cat = (id: string, position: number, name = id): CategoryDto => ({ id, name, position, isHidden: false, productCount: 0 })
const prod = (id: string, categoryId: string | null, position: number, name = id): ProductDto =>
  ({ id, categoryId, position, name, unit: 'Piece', price: 1 }) as ProductDto

describe('groupProducts', () => {
  it('orders categories and products by position and puts uncategorised products last', () => {
    const sections = groupProducts(
      [cat('b', 2), cat('a', 1)],
      [prod('p3', 'a', 2), prod('p1', 'a', 1), prod('p4', null, 1), prod('p2', 'b', 1)],
    )
    expect(sections.map((s) => s.category?.id ?? null)).toEqual(['a', 'b', null])
    expect(sections[0].products.map((p) => p.id)).toEqual(['p1', 'p3'])
  })
  it('keeps empty categories but omits an empty «Другое»', () => {
    const sections = groupProducts([cat('a', 1)], [])
    expect(sections).toHaveLength(1)
    expect(sections[0].products).toEqual([])
  })
  it('treats a product of an unknown category as uncategorised', () => {
    const sections = groupProducts([cat('a', 1)], [prod('p', 'zzz', 1)])
    expect(sections[sections.length - 1].category).toBeNull()
  })
})

describe('moveItem', () => {
  it('swaps neighbours and refuses to leave the list', () => {
    expect(moveItem([1, 2, 3], 1, -1)).toEqual([2, 1, 3])
    expect(moveItem([1, 2, 3], 1, 1)).toEqual([1, 3, 2])
    const same = [1, 2]
    expect(moveItem(same, 0, -1)).toBe(same)
    expect(moveItem(same, 1, 1)).toBe(same)
  })
})

describe('filterProducts', () => {
  it('matches case-insensitively and returns all for an empty query', () => {
    const list = [prod('1', null, 1, 'Шаурма'), prod('2', null, 2, 'Сыр')]
    expect(filterProducts(list, 'шау').map((p) => p.id)).toEqual(['1'])
    expect(filterProducts(list, '  ')).toBe(list)
  })
})

describe('product form rules (API_CONTRACT_CYCLE23.md §410.2)', () => {
  it('parses prices with comma, ≤ 2 decimals, within 0.01…1 000 000', () => {
    expect(parsePrice('250')).toBe(250)
    expect(parsePrice('99,9')).toBe(99.9)
    expect(parsePrice('1 000 000')).toBe(1_000_000)
    expect(parsePrice('0')).toBeNull()
    expect(parsePrice('1.234')).toBeNull()
    expect(parsePrice('1000000.01')).toBeNull()
    expect(parsePrice('abc')).toBeNull()
  })
  it('validates weight step and minimum', () => {
    const ok = { name: 'Сыр', price: '540', unit: 'Weight' as const, step: '100', min: '' }
    expect(validateProduct(ok)).toBeNull()
    expect(validateProduct({ ...ok, step: '5' })).toBe('Шаг — от 10 до 5000 г')
    expect(validateProduct({ ...ok, min: '150' })).toBe('Минимальный вес — не меньше шага и кратен ему')
    expect(validateProduct({ ...ok, min: '20000' })).toBe('Минимальный вес — не меньше шага и кратен ему')
    expect(validateProduct({ ...ok, name: ' ' })).toBe('Укажите название товара')
  })
  it('ignores step/min for piece products', () => {
    expect(validateProduct({ name: 'x', price: '1', unit: 'Piece', step: 'garbage', min: 'garbage' })).toBeNull()
  })
})

describe('stock input', () => {
  it('pieces are integers ≥ 0, empty means «не учитывать»', () => {
    expect(parseStockInput('5', 'Piece')).toEqual({ value: 5 })
    expect(parseStockInput('', 'Piece')).toEqual({ value: null })
    expect(parseStockInput('1.5', 'Piece')).toEqual({ error: 'Остаток — целое число от 0' })
    expect(parseStockInput('-1', 'Piece')).toEqual({ error: 'Остаток — целое число от 0' })
  })
  it('weight is typed in kilograms and sent in grams', () => {
    expect(parseStockInput('1,5', 'Weight')).toEqual({ value: 1500 })
    expect(parseStockInput('0,001', 'Weight')).toEqual({ value: 1 })
    expect(parseStockInput('0', 'Weight')).toEqual({ value: 0 })
    expect(parseStockInput('x', 'Weight')).toEqual({ error: 'Остаток — целое число от 0' })
  })
  it('renders stock back into the input', () => {
    expect(stockToInput(null, 'Piece')).toBe('')
    expect(stockToInput(1500, 'Weight')).toBe('1,5')
    expect(stockToInput(7, 'Piece')).toBe('7')
  })
})
