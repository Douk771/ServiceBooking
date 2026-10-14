import { CompanyPhotosSection, type PhotosAdapter } from '@/components/company/CompanyPhotosSection'
import { SlotNotice } from '@/components/slots/ui/SlotNotice'
import { companyPhotosApi } from '@/api/companyPhotos'
import { useLegalText } from '@/hooks/useLegalText'
import { PHOTO_MAX_PHOTOS } from '@/utils/photoBatch'

/**
 * «Фото комплекса» — the shared gallery of the company (`/api/companies/{id}/photos*`, allowed for «Бани», API_CONTRACT_CYCLE42.md §42.21) with the
 * words of a bath complex. The section shows the server text `CompanyPhotoPeopleNotice` itself before the picker (Т42-13); only when that text
 * is missing do we put the fallback of the cabinet (`cabinetTexts`) in its place, so the line «люди в кадре» is never absent.
 */
export function PhotosCard({ companyId, onChanged }: { companyId: string; onChanged: () => void }) {
  const adapter: PhotosAdapter = {
    queryKey: ['company-photos', companyId],
    list: () => companyPhotosApi.list(companyId),
    upload: (file, onProgress) => companyPhotosApi.upload(companyId, file, onProgress),
    remove: (photoId, reason) => companyPhotosApi.remove(companyId, photoId, reason),
    reorder: (photoIds) => companyPhotosApi.reorder(companyId, photoIds),
    title: 'Фото комплекса',
    emptyText: 'Фото комплекса пока нет. Первое фото станет обложкой на странице комплекса.',
    maxPhotos: PHOTO_MAX_PHOTOS,
    invalidateKeys: [],
  }
  return (
    <div className="flex flex-col gap-3">
      <CompanyPhotosSection companyId={companyId} adapter={adapter} onChanged={onChanged} headingAs="h2" headingClassName="font-serif text-[22px] text-ink" />
      <PhotoPeopleFallback />
    </div>
  )
}

function PhotoPeopleFallback() {
  const server = useLegalText('CompanyPhotoPeopleNotice')
  if (server.isLoading || server.data?.contentHtml) return null
  return <SlotNotice textKey="CompanyPhotoPeopleNotice" />
}
