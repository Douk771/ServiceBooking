import { CompanyPhotosSection, type PhotosAdapter } from '@/components/company/CompanyPhotosSection'
import { staysHousesApi } from '../../api/staysHouses'
import { housePhotosToCompanyPhotos, housePhotoToCompanyPhoto } from '../../utils/housePhotos'
import { getStayErrorMessage, readConflict } from '../../utils/stayError'
import type { StaysConflictDto } from '../../types'
import { houseKey } from './houseKey'
import { useHouseTab } from './houseContext'

/** Photos of a house (`EditHouseContent`): the shared photo section over the house routes — up to 15, the first is the cover. */
export function PhotosTab() {
  const { companyId, house, canEditContent } = useHouseTab()
  if (!canEditContent) return <p className="text-sm text-ink-soft">Фото дома меняет владелец или управляющий.</p>

  const adapter: PhotosAdapter = {
    queryKey: ['stays-house-photos', house.id],
    list: () => staysHousesApi.get(companyId, house.id).then((h) => housePhotosToCompanyPhotos(h.photos)),
    upload: (file, onProgress) => staysHousesApi.uploadPhoto(companyId, house.id, file, onProgress).then(housePhotoToCompanyPhoto),
    remove: (photoId) => staysHousesApi.deletePhoto(companyId, house.id, photoId),
    reorder: (ids) => staysHousesApi.orderPhotos(companyId, house.id, ids).then((list) => housePhotosToCompanyPhotos(list)),
    title: 'Фотографии дома',
    emptyText: 'У дома пока нет фотографий',
    maxPhotos: 15,
    errorMessage: (err) => readConflict<StaysConflictDto>(err)?.message ?? getStayErrorMessage(err, 'Не удалось выполнить действие с фото.'),
    invalidateKeys: [houseKey(companyId, house.id), ['stays-houses', companyId], ['stays-house']],
    removalReason: false,
  }
  return <CompanyPhotosSection companyId={companyId} adapter={adapter} headingAs="h2" headingClassName="font-serif text-[22px] text-ink" />
}
