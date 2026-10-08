import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { InlineError } from '@/components/ui/InlineError'
import { Input } from '@/components/ui/Input'
import { mapLinksFieldError } from '@/utils/mapLinksFieldError'
import { publicStaysApi } from '../../api/publicStays'
import { staysHousesApi } from '../../api/staysHouses'
import type { HouseAmenity } from '../../types'
import { AMENITY_ORDER_FALLBACK, validateContent, type ContentField, type Errors } from '../../utils/houseForms'
import { getStayErrorMessage, httpStatus, plainBody } from '../../utils/stayError'
import { SavedNote, SectionCard, TextArea } from '../cabinet/formParts'
import { StayNotice } from '../StayNotice'
import { useHouseTab } from './houseContext'

type Part = 'description' | 'checkin'

/**
 * The content of a house (`EditHouseContent`): description, amenities, address, map links — and the check-in text. One route saves all of
 * it (`PUT …/content`), so each tab sends the whole content: the fields of the other tab come from the house as it is now.
 */
export function HouseContentCard({ part }: { part: Part }) {
  const { companyId, house, setHouse, canEditContent } = useHouseTab()
  const [description, setDescription] = useState(house.description ?? '')
  const [amenities, setAmenities] = useState<HouseAmenity[]>(house.amenities)
  const [address, setAddress] = useState(house.address ?? '')
  const [yandex, setYandex] = useState(house.yandexMapsUrl ?? '')
  const [twoGis, setTwoGis] = useState(house.twoGisUrl ?? '')
  const [checkIn, setCheckIn] = useState(house.checkInInfoText ?? '')
  const [errors, setErrors] = useState<Errors<ContentField>>({})
  const [formError, setFormError] = useState('')
  const [saved, setSaved] = useState(false)

  const dict = useQuery({ queryKey: ['stays-amenities'], queryFn: publicStaysApi.amenities, staleTime: 60 * 60 * 1000 })
  const options = dict.data ?? AMENITY_ORDER_FALLBACK.map((code) => ({ code, label: code }))

  const touch = () => setSaved(false)
  const toggle = (code: HouseAmenity) => {
    setAmenities((a) => (a.includes(code) ? a.filter((x) => x !== code) : [...a, code]))
    touch()
  }

  const save = useMutation({
    mutationFn: () =>
      staysHousesApi.updateContent(companyId, house.id, {
        description: description.trim() || null,
        amenities,
        address: address.trim() || null,
        yandexMapsUrl: yandex.trim() || null,
        twoGisUrl: twoGis.trim() || null,
        checkInInfoText: checkIn.trim() || null,
      }),
    onSuccess: (h) => {
      setHouse(h)
      setSaved(true)
      setErrors({})
      setFormError('')
    },
    onError: (err) => {
      const raw = plainBody(err)
      if (httpStatus(err) === 400) {
        const field = mapLinksFieldError(raw)
        if (field === 'yandexMapsUrl' || field === 'twoGisUrl') return setErrors({ [field]: raw })
      }
      setFormError(getStayErrorMessage(err, 'Не удалось сохранить.'))
    },
  })

  const submit = () => {
    const v = validateContent({ description, address, checkInInfoText: checkIn })
    setErrors(v)
    if (Object.keys(v).length === 0) save.mutate()
  }

  const ro = !canEditContent
  return (
    <form
      noValidate
      onSubmit={(e) => {
        e.preventDefault()
        if (!ro) submit()
      }}
    >
      {part === 'description' ? (
        <SectionCard title="Описание, удобства, адрес">
          <fieldset disabled={ro} className="flex flex-col gap-5">
            <TextArea label="Описание" rows={6} maxLength={4000} value={description} error={errors.description} onChange={(v) => { setDescription(v); touch() }} />
            <fieldset className="flex flex-col gap-2">
              <legend className="mb-1 text-[13px] font-medium text-[#4A4038]">Удобства</legend>
              <div className="grid gap-x-4 sm:grid-cols-2">
                {options.map((o) => (
                  <label key={o.code} className="flex min-h-[44px] cursor-pointer items-center gap-3 text-sm text-ink">
                    <input type="checkbox" className="h-5 w-5 accent-gold" checked={amenities.includes(o.code)} onChange={() => toggle(o.code)} />
                    {o.label}
                  </label>
                ))}
              </div>
            </fieldset>
            <Input label="Адрес дома" maxLength={500} value={address} error={errors.address} onChange={(e) => { setAddress(e.target.value); touch() }} />
            <StayNotice textKey="StayPublicContactsNotice" />
            <div className="grid gap-4 sm:grid-cols-2">
              <Input label="Яндекс Карты" placeholder="https://yandex.ru/maps/..." value={yandex} error={errors.yandexMapsUrl} onChange={(e) => { setYandex(e.target.value); touch() }} />
              <Input label="2ГИС" placeholder="https://2gis.ru/..." value={twoGis} error={errors.twoGisUrl} onChange={(e) => { setTwoGis(e.target.value); touch() }} />
            </div>
          </fieldset>
        </SectionCard>
      ) : (
        <SectionCard title="Информация к заселению" description="Текст именно этого дома: как найти, коды, ключи. Гость увидит его вместе с общим текстом компании на странице брони в день заезда.">
          <fieldset disabled={ro} className="flex flex-col gap-4">
            <TextArea
              label="Текст к заселению"
              rows={6}
              maxLength={2000}
              value={checkIn}
              error={errors.checkInInfoText}
              onChange={(v) => { setCheckIn(v); touch() }}
              hint={<StayNotice textKey="StayMigrationOwnerNotice" />}
            />
            <StayNotice textKey="StayCheckInInfoOwnerNotice" />
          </fieldset>
        </SectionCard>
      )}
      {formError && (
        <div className="mt-4">
          <InlineError>{formError}</InlineError>
        </div>
      )}
      {!ro && (
        <div className="mt-4 flex items-center gap-3">
          <Button type="submit" loading={save.isPending} className="min-h-[44px]">
            Сохранить
          </Button>
          <SavedNote show={saved} />
        </div>
      )}
      {ro && <p className="mt-3 text-xs text-muted">Содержание дома у вас доступно только для просмотра.</p>}
    </form>
  )
}
