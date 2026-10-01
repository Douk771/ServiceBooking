// @vitest-environment node
import { describe, it, expect } from 'vitest'
import {
  acceptPrices,
  cartStorageKey,
  decrementQuantity,
  incrementQuantity,
  MAX_CART_LINES,
  normalizeQuantity,
  parseCart,
  priceChanges,
  quantityRule,
  removeItem,
  upsertItem,
} from './cart'

const weight = quantityRule({ unit: 'Weight', weightStepGrams: 100, minQuantity: 200, maxQuantity: 10000 })
const piece = quantityRule({ unit: 'Piece', weightStepGrams: null, minQuantity: 1, maxQuantity: 99 })

describe('parseCart', () => {
  it('reads a valid cart', () => {
    expect(parseCart(JSON.stringify([{ productId: 'a', quantity: 2, unitPriceSeen: 150 }]))).toEqual([{ productId: 'a', quantity: 2, unitPriceSeen: 150 }])
  })
  it('survives garbage, wrong shapes and hand-edited values', () => {
    expect(parseCart(null)).toEqual([])
    expect(parseCart('not json')).toEqual([])
    expect(parseCart('{"a":1}')).toEqual([])
    const raw = JSON.stringify([
      { productId: 'a', quantity: 1.5, unitPriceSeen: 10 },
      { productId: 'b', quantity: 0, unitPriceSeen: 10 },
      { productId: 'c', quantity: 1, unitPriceSeen: -5 },
      { productId: '', quantity: 1, unitPriceSeen: 5 },
      { productId: 'd', quantity: 3, unitPriceSeen: 5 },
      { productId: 'd', quantity: 4, unitPriceSeen: 5 },
      null,
    ])
    expect(parseCart(raw)).toEqual([{ productId: 'd', quantity: 3, unitPriceSeen: 5 }])
  })
  it('never keeps more lines than the server accepts', () => {
    const many = Array.from({ length: 80 }, (_, i) => ({ productId: `p${i}`, quantity: 1, unitPriceSeen: 1 }))
    expect(parseCart(JSON.stringify(many))).toHaveLength(MAX_CART_LINES)
  })
  it('keys the cart per shop', () => {
    expect(cartStorageKey('shaurma')).toBe('goods-cart:shaurma')
  })
})

describe('quantity rules', () => {
  it('pieces run 1…99 by one', () => {
    expect(incrementQuantity(piece, undefined)).toBe(1)
    expect(incrementQuantity(piece, 99)).toBe(99)
    expect(decrementQuantity(piece, 2)).toBe(1)
    expect(decrementQuantity(piece, 1)).toBeNull()
  })
  it('weight starts at the minimum and moves by the step', () => {
    expect(incrementQuantity(weight, undefined)).toBe(200)
    expect(incrementQuantity(weight, 200)).toBe(300)
    expect(incrementQuantity(weight, 10000)).toBe(10000)
    expect(decrementQuantity(weight, 300)).toBe(200)
    expect(decrementQuantity(weight, 200)).toBeNull()
  })
  it('normalizes typed values onto the step grid within bounds', () => {
    expect(normalizeQuantity(weight, 340)).toBe(300)
    expect(normalizeQuantity(weight, 50)).toBe(200)
    expect(normalizeQuantity(weight, 99999)).toBe(10000)
  })
  it('falls back to defaults when the DTO omits a step', () => {
    expect(quantityRule({ unit: 'Weight', weightStepGrams: null, minQuantity: 0, maxQuantity: 0 })).toEqual({ unit: 'Weight', step: 100, min: 100, max: 10000 })
  })
})

describe('cart mutations', () => {
  const a = { productId: 'a', quantity: 1, unitPriceSeen: 100 }
  it('upserts and removes without mutating', () => {
    const one = upsertItem([], a)
    expect(upsertItem(one, { ...a, quantity: 3 })).toEqual([{ ...a, quantity: 3 }])
    expect(one).toEqual([a])
    expect(removeItem(one, 'a')).toEqual([])
  })
  it('detects and accepts price changes', () => {
    const items = [a, { productId: 'b', quantity: 1, unitPriceSeen: 50 }]
    const changes = priceChanges(items, [
      { productId: 'a', unitPrice: 120 },
      { productId: 'b', unitPrice: 50 },
      { productId: 'zzz', unitPrice: 1 },
    ])
    expect(changes).toEqual([{ productId: 'a', was: 100, now: 120 }])
    const accepted = acceptPrices(items, { a: 120 })
    expect(accepted[0].unitPriceSeen).toBe(120)
    expect(accepted[1].unitPriceSeen).toBe(50)
    expect(priceChanges(accepted, [{ productId: 'a', unitPrice: 120 }])).toEqual([])
  })
})
