// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { publicAddress } from './publicAddress'

// API_CONTRACT_CYCLE26.md §565 reference table.
describe('publicAddress', () => {
  it.each<[string | null, string | null, string]>([
    ['Барнаул', 'Ленина, 5', 'Барнаул, Ленина, 5'],
    ['Барнаул', 'Барнаул, Ленина, 5', 'Барнаул, Ленина, 5'],
    ['Барнаул', 'г. Барнаул, ул. Ленина, 5', 'г. Барнаул, ул. Ленина, 5'],
    ['Барнаул', 'барнаул ленина 5', 'барнаул ленина 5'],
    ['Барнаул', 'Барнаульская, 3', 'Барнаул, Барнаульская, 3'],
    ['Барнаул', '', 'Барнаул'],
    ['Барнаул', null, 'Барнаул'],
    [null, 'Ленина, 5', 'Ленина, 5'],
    ['Барнаул', 'г.Барнаул, Ленина 5', 'г.Барнаул, Ленина 5'],
    ['Барнаул', 'город Барнаул', 'город Барнаул'],
    ['Королёв', 'Королев, Мира 1', 'Королев, Мира 1'],
    [null, null, ''],
  ])('publicAddress(%j, %j) = %j', (city, address, expected) => {
    expect(publicAddress(city, address)).toBe(expected)
  })

  it('trims surrounding whitespace', () => {
    expect(publicAddress('  Барнаул ', ' Ленина, 5 ')).toBe('Барнаул, Ленина, 5')
  })
})
