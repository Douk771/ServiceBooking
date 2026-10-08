import { useRef, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { companiesApi } from '@/api/companies'
import { companyAddressApi } from '@/api/companyAddress'
import { Button } from '@/components/ui/Button'
import { InlineError } from '@/components/ui/InlineError'
import { Input } from '@/components/ui/Input'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { PublicAddressNotice } from '@/components/company/PublicAddressNotice'
import { mapLinksFieldError } from '@/utils/mapLinksFieldError'
import { getLogoErrorMessage } from '@/utils/companyManageError'
import { isRussianPhone } from '@/utils/phone'
import type { StaysCompanyManageDto } from '../../types'
import { getStayErrorMessage, httpStatus, plainBody } from '../../utils/stayError'
import { StayNotice } from '../StayNotice'
import { SavedNote, SectionCard, TextArea } from './formParts'

type FieldKey = 'name' | 'phone' | 'address' | 'yandexMapsUrl' | 'twoGisUrl'

/**
 * Profile of the company: name, description, phone and e-mail for guests, address and map links, logo. The city is Sheregesh and the
 * time zone is the city's (ARCHITECTURE_CYCLE37.md §37.3.2): neither is offered. The address goes through the shared public-address
 * notice before it is sent, like everywhere else in the platform; the phone and the address are shown to everyone (StayPublicContactsNotice).
 */
export function StaysProfileCard({ company, onSaved }: { company: StaysCompanyManageDto; onSaved: () => void }) {
  const base = useRef({ address: company.address ?? '', yandex: company.yandexMapsUrl ?? '', twoGis: company.twoGisUrl ?? '' })
  const [name, setName] = useState(company.name)
  const [description, setDescription] = useState(company.description ?? '')
  const [phone, setPhone] = useState(company.phone ?? '')
  const [email, setEmail] = useState(company.email ?? '')
  const [address, setAddress] = useState(company.address ?? '')
  const [yandex, setYandex] = useState(company.yandexMapsUrl ?? '')
  const [twoGis, setTwoGis] = useState(company.twoGisUrl ?? '')
  const [errors, setErrors] = useState<Partial<Record<FieldKey, string>>>({})
  const [formError, setFormError] = useState('')
  const [saved, setSaved] = useState(false)
  const [saving, setSaving] = useState(false)
  const [notice, setNotice] = useState(false)
  const [logoError, setLogoError] = useState('')
  const logoInput = useRef<HTMLInputElement>(null)

  const touch = () => setSaved(false)
  const addressChanged = address.trim() !== base.current.address.trim()

  const logo = useMutation({
    mutationFn: (file: File) => companiesApi.uploadLogo(company.id, file),
    onMutate: () => setLogoError(''),
    onSuccess: onSaved,
    onError: (e) => setLogoError(getLogoErrorMessage(e)),
  })

  function begin() {
    setFormError('')
    setSaved(false)
    const e: Partial<Record<FieldKey, string>> = {}
    if (!name.trim()) e.name = 'Укажите название'
    if (!isRussianPhone(phone)) e.phone = 'Введите номер телефона в формате +7 (900) 000-00-00'
    setErrors(e)
    if (Object.keys(e).length > 0) return
    if (addressChanged && address.trim()) setNotice(true)
    else void run()
  }

  async function run() {
    setNotice(false)
    setSaving(true)
    const yandexChanged = yandex.trim() !== base.current.yandex.trim()
    const twoGisChanged = twoGis.trim() !== base.current.twoGis.trim()
    try {
      await companiesApi.update(company.id, {
        name: name.trim(),
        description: description.trim(),
        phone,
        email: email.trim(),
        ...(yandexChanged ? { yandexMapsUrl: yandex.trim() } : {}),
        ...(twoGisChanged ? { twoGisUrl: twoGis.trim() } : {}),
      })
    } catch (err) {
      setSaving(false)
      const raw = plainBody(err)
      if (httpStatus(err) === 400) {
        let field = mapLinksFieldError(raw)
        if (field === 'yandexMapsUrl' && !raw.includes('Яндекс') && !yandexChanged) field = 'twoGisUrl'
        if (field === 'yandexMapsUrl' || field === 'twoGisUrl') {
          setErrors({ [field]: raw })
          return
        }
      }
      setFormError(getStayErrorMessage(err, 'Не удалось сохранить профиль.'))
      return
    }
    base.current = { ...base.current, yandex: yandex.trim(), twoGis: twoGis.trim() }
    if (addressChanged) {
      try {
        await companyAddressApi.saveAddress(company.id, address)
        base.current = { ...base.current, address }
      } catch {
        setSaving(false)
        setErrors({ address: 'Не удалось сохранить адрес. Попробуйте ещё раз.' })
        setFormError('Остальные изменения сохранены')
        onSaved()
        return
      }
    }
    setSaving(false)
    setErrors({})
    setSaved(true)
    onSaved()
  }

  return (
    <SectionCard title="Профиль компании" description="Это видят гости на странице компании и в каталоге.">
      <div className="flex flex-wrap items-center gap-4">
        {company.logoUrl ? (
          <img src={company.logoUrl} alt="Логотип компании" className="h-16 w-16 rounded-[18px] object-cover" />
        ) : (
          <span aria-hidden="true" className="flex h-16 w-16 items-center justify-center rounded-[18px] bg-cream-deep font-serif text-2xl text-gold-dark">
            {company.name.trim()[0]?.toUpperCase()}
          </span>
        )}
        <div>
          <input
            ref={logoInput}
            type="file"
            accept="image/jpeg,image/png,image/webp"
            className="hidden"
            aria-label="Файл логотипа"
            onChange={(e) => {
              const f = e.target.files?.[0]
              if (f) logo.mutate(f)
              e.target.value = ''
            }}
          />
          <Button type="button" variant="secondary" size="sm" className="min-h-[44px]" loading={logo.isPending} onClick={() => logoInput.current?.click()}>
            {company.logoUrl ? 'Заменить логотип' : 'Загрузить логотип'}
          </Button>
          <p className="mt-1 text-xs text-muted">JPEG, PNG или WEBP, до 5 МБ</p>
          {logoError && <p className="mt-1 text-xs text-danger">{logoError}</p>}
        </div>
      </div>

      <form
        noValidate
        className="flex flex-col gap-4"
        onSubmit={(e) => {
          e.preventDefault()
          if (!saving) begin()
        }}
      >
        <Input label="Название *" maxLength={200} value={name} error={errors.name} onChange={(e) => { setName(e.target.value); touch() }} />
        <TextArea label="Описание" rows={3} maxLength={2000} value={description} onChange={(v) => { setDescription(v); touch() }} />
        <div className="grid gap-4 sm:grid-cols-2">
          <PhoneInput label="Телефон для гостей *" value={phone} error={errors.phone} onChange={(v) => { setPhone(v); touch() }} />
          <Input label="Email" type="email" value={email} onChange={(e) => { setEmail(e.target.value); touch() }} />
        </div>
        <StayNotice textKey="StayPublicContactsNotice" />
        <Input label="Адрес компании" maxLength={300} value={address} error={errors.address} onChange={(e) => { setAddress(e.target.value); touch() }} />
        <div className="grid gap-4 sm:grid-cols-2">
          <Input label="Яндекс Карты" placeholder="https://yandex.ru/maps/org/..." value={yandex} error={errors.yandexMapsUrl} onChange={(e) => { setYandex(e.target.value); touch() }} />
          <Input label="2ГИС" placeholder="https://2gis.ru/..." value={twoGis} error={errors.twoGisUrl} onChange={(e) => { setTwoGis(e.target.value); touch() }} />
        </div>
        <p className="text-xs text-muted">Город — Шерегеш, часовой пояс — {company.timeZoneId}. Адрес у каждого дома свой; здесь — общий адрес компании.</p>
        {formError && <InlineError>{formError}</InlineError>}
        <div className="flex items-center gap-3">
          <Button type="submit" loading={saving} className="min-h-[44px]">
            Сохранить профиль
          </Button>
          <SavedNote show={saved} />
        </div>
      </form>

      {notice && <PublicAddressNotice onConfirmed={() => void run()} onCancel={() => setNotice(false)} />}
    </SectionCard>
  )
}
