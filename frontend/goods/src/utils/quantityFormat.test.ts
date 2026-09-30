// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { clampToStep, formatGrams, formatMoney, formatQuantity, formatUnitPrice, parseKgToGrams } from './quantityFormat'

describe('quantityFormat', () => {
  it('formats grams below and above a kilogram', () => {
    expect(formatGrams(540)).toBe('540 г')
    expect(formatGrams(800)).toBe('800 г')
    expect(formatGrams(1000)).toBe('1 кг')
    expect(formatGrams(1500)).toBe('1,5 кг')
  })
  it('formats quantity per unit', () => {
    expect(formatQuantity('Piece', 2)).toBe('2 шт')
    expect(formatQuantity('Weight', 500)).toBe('500 г')
  })
  it('formats unit price', () => {
    expect(formatUnitPrice('Piece', 250)).toBe('250 ₽')
    expect(formatUnitPrice('Weight', 540)).toBe('540 ₽/кг')
    expect(formatUnitPrice('Piece', 99.9)).toMatch(/^99,90 ₽$/)
  })
  it('puts ≈ before an approximate sum only', () => {
    expect(formatMoney(540, true)).toBe('≈ 540 ₽')
    expect(formatMoney(540)).toBe('540 ₽')
  })
  it('parses kilograms with comma or dot to integer grams', () => {
    expect(parseKgToGrams('1,5')).toBe(1500)
    expect(parseKgToGrams('0.25')).toBe(250)
    expect(parseKgToGrams('abc')).toBeNull()
    expect(parseKgToGrams('0')).toBeNull()
    expect(parseKgToGrams('-1')).toBeNull()
  })
  it('snaps grams to the step within bounds', () => {
    expect(clampToStep(430, 100, 100, 10000)).toBe(400)
    expect(clampToStep(20, 100, 200, 10000)).toBe(200)
    expect(clampToStep(99999, 100, 100, 10000)).toBe(10000)
  })
})
