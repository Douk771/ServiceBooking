import { describe, it, expect } from 'vitest'
import { orderPhotosForDisplay } from './companyPhotos'
import type { CompanyPhoto } from '../types'

function photo(overrides: Partial<CompanyPhoto>): CompanyPhoto {
  return {
    id: 'p',
    url: '/u.jpg',
    thumbnailUrl: '/u-thumb.jpg',
    width: 100,
    height: 100,
    position: 0,
    isCover: false,
    ...overrides,
  }
}

describe('orderPhotosForDisplay — ARCHITECTURE_CYCLE13.md §211', () => {
  it('puts the cover photo first regardless of its position', () => {
    const photos = [
      photo({ id: 'a', position: 0, isCover: false }),
      photo({ id: 'b', position: 1, isCover: true }),
      photo({ id: 'c', position: 2, isCover: false }),
    ]
    expect(orderPhotosForDisplay(photos).map((p) => p.id)).toEqual(['b', 'a', 'c'])
  })

  it('orders the rest by position when there is no cover flagged', () => {
    const photos = [photo({ id: 'c', position: 2 }), photo({ id: 'a', position: 0 }), photo({ id: 'b', position: 1 })]
    expect(orderPhotosForDisplay(photos).map((p) => p.id)).toEqual(['a', 'b', 'c'])
  })

  it('does not mutate the input array', () => {
    const photos = [photo({ id: 'b', position: 1 }), photo({ id: 'a', position: 0 })]
    const original = [...photos]
    orderPhotosForDisplay(photos)
    expect(photos).toEqual(original)
  })

  it('returns an empty array unchanged', () => {
    expect(orderPhotosForDisplay([])).toEqual([])
  })
})
