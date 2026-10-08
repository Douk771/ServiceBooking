import type { CompanyPhoto } from '@/types'
import type { HousePhotoDto } from '../types'

/**
 * The shared gallery and the shared photo section work with `CompanyPhoto`; a house photo has the fields they read. The first photo
 * is the cover (API_CONTRACT_CYCLE37.md §37.28: «первое — обложка»).
 */
export function housePhotoToCompanyPhoto(p: HousePhotoDto): CompanyPhoto {
  return {
    id: p.id,
    url: p.url,
    thumbnailUrl: p.thumbnailUrl ?? p.url,
    width: 0,
    height: 0,
    position: p.position,
    isCover: p.position === 0,
  }
}

export function housePhotosToCompanyPhotos(photos: HousePhotoDto[]): CompanyPhoto[] {
  return [...photos].sort((a, b) => a.position - b.position).map(housePhotoToCompanyPhoto)
}
