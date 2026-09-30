// @vitest-environment node
import { describe, it, expect } from 'vitest'
import vectors from '../../../../contracts/cycle23/order-money-vectors.json'
import { lineTotal, orderTotal, type PreviewLine } from './orderMoney'

interface Vectors {
  lines: { unit: 'Piece' | 'Weight'; price: number; quantity: number; expected: number }[]
  totals: { lines: PreviewLine[]; expected: number; isApproximate: boolean }[]
}
const v = vectors as unknown as Vectors

describe('orderMoney vs contracts/cycle23/order-money-vectors.json', () => {
  it.each(v.lines.map((l) => [`${l.unit} ${l.price} × ${l.quantity}`, l] as const))('line %s', (_name, l) => {
    expect(lineTotal(l.unit, l.price, l.quantity)).toBeCloseTo(l.expected, 2)
    // exact, not just close: same kopeck integer
    expect(Math.round(lineTotal(l.unit, l.price, l.quantity) * 100)).toBe(Math.round(l.expected * 100))
  })

  it.each(v.totals.map((t, i) => [`#${i + 1}`, t] as const))('total %s', (_name, t) => {
    const r = orderTotal(t.lines)
    expect(Math.round(r.total * 100)).toBe(Math.round(t.expected * 100))
    expect(r.isApproximate).toBe(t.isApproximate)
  })
})
