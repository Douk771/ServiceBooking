// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { housePhotosToCompanyPhotos } from './housePhotos'

describe('house photos for the shared gallery', () => {
  it('orders by position and makes the first one the cover', () => {
    const out = housePhotosToCompanyPhotos([
      { id: 'b', url: '/u/b.jpg', thumbnailUrl: '/u/b-t.jpg', position: 1 },
      { id: 'a', url: '/u/a.jpg', thumbnailUrl: null, position: 0 },
    ])
    expect(out.map((p) => p.id)).toEqual(['a', 'b'])
    expect(out.map((p) => p.isCover)).toEqual([true, false])
    expect(out[0].thumbnailUrl).toBe('/u/a.jpg') // no thumbnail → the photo itself
    expect(out[1].thumbnailUrl).toBe('/u/b-t.jpg')
  })
  it('empty list stays empty', () => {
    expect(housePhotosToCompanyPhotos([])).toEqual([])
  })
})
