import { describe, it, expect } from 'vitest'
import { guestLine, guestsText, itemsText } from './schedule'

describe('guestsText', () => {
  it.each([
    [1, '1 гость'],
    [2, '2 гостя'],
    [4, '4 гостя'],
    [5, '5 гостей'],
    [11, '11 гостей'],
    [21, '21 гость'],
    [112, '112 гостей'],
  ])('%i -> %s', (n, text) => expect(guestsText(n)).toBe(text))
})

describe('guestLine / itemsText', () => {
  it('shows the name and the count, and tolerates both missing', () => {
    expect(guestLine({ guestName: 'Мария', guestsCount: 4 })).toBe('Мария · 4 гостя')
    expect(guestLine({ guestName: null, guestsCount: null })).toBe('Гость')
    expect(guestLine({ guestName: ' ', guestsCount: 0 })).toBe('Гость')
  })
  it('lists the positions to prepare', () => {
    expect(itemsText([{ name: 'Веник', quantity: 2 }, { name: 'Чай', quantity: 1 }])).toBe('Веник × 2, Чай × 1')
    expect(itemsText([])).toBe('')
  })
})
