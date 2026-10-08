// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { moveHouse } from './houseOrder'

describe('moveHouse', () => {
  it('swaps with the neighbour and returns the full list without touching the input', () => {
    const ids = ['a', 'b', 'c']
    expect(moveHouse(ids, 1, -1)).toEqual(['b', 'a', 'c'])
    expect(moveHouse(ids, 1, 1)).toEqual(['a', 'c', 'b'])
    expect(ids).toEqual(['a', 'b', 'c'])
  })
  it('does nothing at the ends or out of range', () => {
    expect(moveHouse(['a', 'b'], 0, -1)).toBeNull()
    expect(moveHouse(['a', 'b'], 1, 1)).toBeNull()
    expect(moveHouse(['a'], 3, 1)).toBeNull()
    expect(moveHouse([], 0, 1)).toBeNull()
  })
})
