import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { CompanyPhotosSection, type PhotosAdapter } from '@/components/company/CompanyPhotosSection'
import type { CompanyPhoto } from '@/types'
import { Button } from '@/components/ui/Button'
import { InlineError } from '@/components/ui/InlineError'
import type { ServicePhotoDto, StaysConflictDto } from '@/types/slots'
import { getStayErrorMessage, readConflict } from '@/utils/slots/slotError'
import { SavedNote, SectionCard, TextArea } from '@/components/slots/ui/formParts'
import { ContentWarnings } from '@/components/slots/ui/ContentWarnings'
import { SlotNotice } from '@/components/slots/ui/SlotNotice'
import { useServiceTab } from '@/components/slots/services/cabinet/serviceContext'
import { useSlotVertical } from '@/components/slots/SlotVerticalContext'

const toCompanyPhoto = (p: ServicePhotoDto): CompanyPhoto => ({
  id: p.id,
  url: p.url,
  thumbnailUrl: p.thumbnailUrl ?? p.url,
  width: 0,
  height: 0,
  position: p.position,
  isCover: p.position === 0,
})
const toCompanyPhotos = (list: ServicePhotoDto[]) => [...list].sort((a, b) => a.position - b.position).map(toCompanyPhoto)

/** «Описание»: the text of the page (with the visiting rules, `serviceSafetyOwnerNotice` above it) and the photos (≤ 10, the first is the cover). */
export function ServiceContentTab() {
  const { api, legal, features } = useSlotVertical()
  const staysServicesApi = api.cabinet
  const { companyId, service, setService, canEditContent } = useServiceTab()
  const [text, setText] = useState(service.description ?? '')
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState('')

  const save = useMutation({
    mutationFn: () => staysServicesApi.content(companyId, service.id, text.trim() || null),
    onSuccess: (s) => {
      setService(s)
      setSaved(true)
      setError('')
    },
    onError: (err) => setError(getStayErrorMessage(err, 'Не удалось сохранить описание.')),
  })

  if (!canEditContent) return <p className="text-sm text-ink-soft">Описание и фото услуги меняет владелец или управляющий.</p>

  const adapter: PhotosAdapter = {
    queryKey: ['stays-service-photos', service.id],
    list: () => staysServicesApi.get(companyId, service.id).then((s) => toCompanyPhotos(s.photos)),
    upload: (file, onProgress) => staysServicesApi.uploadPhoto(companyId, service.id, file, onProgress).then(toCompanyPhoto),
    remove: (photoId) => staysServicesApi.deletePhoto(companyId, service.id, photoId),
    reorder: (ids) => staysServicesApi.reorderPhotos(companyId, service.id, ids).then(toCompanyPhotos),
    title: 'Фотографии услуги',
    emptyText: 'У услуги пока нет фотографий',
    maxPhotos: 10,
    errorMessage: (err) => readConflict<StaysConflictDto>(err)?.message ?? getStayErrorMessage(err, 'Не удалось выполнить действие с фото.'),
    invalidateKeys: [['stays-service', companyId, service.id], ['stays-services', companyId], ['stays-service-page']],
    removalReason: false,
  }

  return (
    <div className="flex flex-col gap-6">
      <SectionCard title="Описание услуги" description="Это увидят гости на странице услуги. Правила посещения впишите сюда же.">
        <form
          noValidate
          className="flex flex-col gap-4"
          onSubmit={(e) => {
            e.preventDefault()
            save.mutate()
          }}
        >
          <TextArea
            label="Описание"
            rows={8}
            maxLength={2000}
            value={text}
            onChange={(v) => {
              setText(v)
              setSaved(false)
            }}
            hint={<SlotNotice textKey={legal.keys.serviceSafetyOwnerNotice} />}
          />
          <p className="text-xs text-muted">{text.length} из 2000</p>
          <ContentWarnings codes={service.contentWarnings} />
          {error && <InlineError>{error}</InlineError>}
          <div className="flex items-center gap-3">
            <Button type="submit" size="lg" loading={save.isPending} className="min-h-[44px]">
              Сохранить описание
            </Button>
            <SavedNote show={saved} />
          </div>
        </form>
      </SectionCard>
      {features.photoPeopleNotice && legal.keys.photoPeopleNotice && <SlotNotice textKey={legal.keys.photoPeopleNotice} />}
      <CompanyPhotosSection companyId={companyId} adapter={adapter} headingAs="h2" headingClassName="font-serif text-[22px] text-ink" />
    </div>
  )
}
