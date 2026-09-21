import { describe, it, expect } from 'vitest'
import { findHitMarkers } from './templateMarkers'

const MARKERS = ['скидк', 'акци', 'промо', '%', 'бесплатн']

describe('findHitMarkers', () => {
  it('matches case-insensitively', () => {
    expect(findHitMarkers('У нас СКИДКА 20%!', MARKERS)).toEqual(expect.arrayContaining(['скидк', '%']))
  })

  it('returns an empty array for an ordinary service message', () => {
    expect(findHitMarkers('Здравствуйте, Мария! Напоминаем о записи завтра в 14:00.', MARKERS)).toEqual([])
  })

  it('preserves the order of the marker list, not the order found in the text', () => {
    expect(findHitMarkers('промо-акция со скидкой', MARKERS)).toEqual(['скидк', 'акци', 'промо'])
  })

  it('does not duplicate a marker that appears twice in the text', () => {
    expect(findHitMarkers('скидка на скидку', ['скидк'])).toEqual(['скидк'])
  })
})
