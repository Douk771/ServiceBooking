import type { CompanyPhoto } from '../types'

/**
 * ARCHITECTURE_CYCLE13.md §211 — display order for the gallery carousel: the cover photo first,
 * everything else by `position`. The Lightbox is handed the exact same ordered array, so the
 * carousel's slide index and the Lightbox's photo index can never point at different photos.
 */
export function orderPhotosForDisplay(photos: CompanyPhoto[]): CompanyPhoto[] {
  return [...photos].sort((a, b) => {
    if (a.isCover !== b.isCover) return a.isCover ? -1 : 1
    return a.position - b.position
  })
}
