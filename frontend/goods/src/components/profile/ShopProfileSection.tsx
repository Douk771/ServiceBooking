import { useEffect, useId, useRef, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { companiesApi } from '@/api/companies'
import { companyAddressApi } from '@/api/companyAddress'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { CityCombobox } from '@/components/ui/CityCombobox'
import { Input } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { PublicAddressNotice } from '@/components/company/PublicAddressNotice'
import { getLogoErrorMessage } from '@/utils/companyManageError'
import { mapLinksFieldError } from '@/utils/mapLinksFieldError'
import { formatCityTimeZone, formatUtcOffset } from '@/utils/timezone'
import type { City } from '@/types'
import { InlineError } from '../StatePanels'
import { getGoodsErrorMessage, httpStatus } from '../../utils/orderError'
import type { ShopManageDto } from '../../types'

/** The address step failed; the rest of the profile is already saved (ARCHITECTURE_CYCLE26.md §552.2). */
const ADDRESS_FAILED = 'Не удалось сохранить адрес. Попробуйте ещё раз.'

type FieldKey = 'name' | 'city' | 'address' | 'yandexMapsUrl' | 'twoGisUrl'
type FieldErrors = Partial<Record<FieldKey, string>>

interface Values {
  name: string
  description: string
  phone: string
  email: string
  cityId: number | null
  address: string
  yandexMapsUrl: string
  twoGisUrl: string
}

function cityOf(shop: ShopManageDto): City | null {
  if (shop.cityId == null || !shop.cityName) return null
  return {
    id: shop.cityId,
    name: shop.cityName,
    region: shop.cityRegion ?? '',
    timeZoneId: shop.timeZoneId,
    utcOffsetMinutes: shop.utcOffsetMinutes ?? 0,
    label: shop.cityRegion ? `${shop.cityName}, ${shop.cityRegion}` : shop.cityName,
  }
}

function initialValues(shop: ShopManageDto): Values {
  return {
    name: shop.name,
    description: shop.description ?? '',
    phone: shop.phone ?? '',
    email: shop.email ?? '',
    cityId: shop.cityId ?? null,
    address: shop.address ?? '',
    yandexMapsUrl: shop.yandexMapsUrl ?? '',
    twoGisUrl: shop.twoGisUrl ?? '',
  }
}

function SavedNote({ show }: { show: boolean }) {
  return show ? (
    <span role="status" className="text-sm text-success font-medium">
      Сохранено
    </span>
  ) : null
}

/**
 * ARCHITECTURE_CYCLE26.md §552 — the shop profile as one form with one «Сохранить». State is local and initialised
 * from `shop` once (mount it with `key={shop.id}`): a re-read `ShopManageDto` never rebuilds the form, so typing is
 * never lost (cycle-13 lesson). `baseline` holds the values of the last successful save.
 */
export function ShopProfileSection({ shop }: { shop: ShopManageDto }) {
  const qc = useQueryClient()
  const ids = useId()
  const [values, setValues] = useState<Values>(() => initialValues(shop))
  const [city, setCity] = useState<City | null>(() => cityOf(shop))
  const baseline = useRef<Values>(initialValues(shop))
  const [errors, setErrors] = useState<FieldErrors>({})
  const [formError, setFormError] = useState('')
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const savedTimer = useRef<ReturnType<typeof setTimeout> | null>(null)
  useEffect(() => () => {
    if (savedTimer.current) clearTimeout(savedTimer.current)
  }, [])
  const [step, setStep] = useState<'tz' | 'address' | null>(null)
  const [logoError, setLogoError] = useState('')
  const logoInput = useRef<HTMLInputElement>(null)

  const set = <K extends keyof Values>(key: K, value: Values[K]) => {
    setValues((v) => ({ ...v, [key]: value }))
    setSaved(false)
  }

  const refetchShop = () => {
    void qc.invalidateQueries({ queryKey: ['shop', shop.id] })
    void qc.invalidateQueries({ queryKey: ['my-shops'] })
    void qc.invalidateQueries({ queryKey: ['storefront'] })
  }

  const logo = useMutation({
    mutationFn: (file: File) => companiesApi.uploadLogo(shop.id, file),
    onMutate: () => setLogoError(''),
    onSuccess: refetchShop,
    onError: (e) => setLogoError(getLogoErrorMessage(e)),
  })

  const cityChanged = city != null && city.id !== baseline.current.cityId
  // API_CONTRACT_CYCLE29.md §29.24: compare offsets, never block on `timeZoneChangeAllowed` alone —
  // `false` only forbids a city with a DIFFERENT UTC offset (same rule as ShopTimeZoneChangePolicy).
  const offsetChanged = cityChanged && city.utcOffsetMinutes !== shop.utcOffsetMinutes
  const addressChanged = values.address.trim() !== baseline.current.address.trim()

  /** Steps 1–3 of §552.2: local checks and the two confirmations. Returns which dialog to open, if any. */
  function begin() {
    setFormError('')
    setSaved(false)
    if (!values.name.trim()) {
      setErrors({ name: 'Введите название магазина' })
      return
    }
    if (offsetChanged) {
      if (!shop.timeZoneChangeAllowed) {
        setErrors({ city: shop.timeZoneChangeLockedText ?? 'У магазина уже есть заказы — часовой пояс сменить нельзя.' })
        return
      }
      setErrors({})
      setStep('tz')
      return
    }
    setErrors({})
    afterTimeZone()
  }

  function afterTimeZone() {
    if (addressChanged) setStep('address')
    else {
      setStep(null)
      void run()
    }
  }

  /** Steps 4–6: PUT the profile, then the address route if the address changed. */
  async function run() {
    setStep(null)
    setSaving(true)
    const base = baseline.current
    const sent: Values = { ...values, name: values.name.trim(), description: values.description.trim(), email: values.email.trim() }
    const payload: Parameters<typeof companiesApi.update>[1] = {
      name: sent.name,
      description: sent.description,
      phone: sent.phone,
      email: sent.email,
    }
    if (cityChanged && city) payload.cityId = city.id
    // Untouched links are not sent at all; a cleared one goes as "" (= remove).
    if (sent.yandexMapsUrl.trim() !== base.yandexMapsUrl.trim()) payload.yandexMapsUrl = sent.yandexMapsUrl.trim()
    if (sent.twoGisUrl.trim() !== base.twoGisUrl.trim()) payload.twoGisUrl = sent.twoGisUrl.trim()

    try {
      await companiesApi.update(shop.id, payload)
    } catch (err) {
      setSaving(false)
      const status = httpStatus(err)
      const text = getGoodsErrorMessage(err, 'Не удалось сохранить профиль.')
      if (status === 409) setErrors({ city: text })
      else if (status === 400 && text.includes('Город не найден')) setErrors({ city: text })
      else if (status === 400) {
        let field = mapLinksFieldError(text)
        // §567: the generic «Ссылка …» text belongs to Яндекс Карты only if that field was in the request.
        if (field === 'yandexMapsUrl' && !text.includes('Яндекс') && payload.yandexMapsUrl === undefined) field = 'twoGisUrl'
        if (field === 'yandexMapsUrl' || field === 'twoGisUrl') setErrors({ [field]: text })
        else setFormError(text)
      } else setFormError(text)
      return
    }

    baseline.current = {
      ...base,
      name: sent.name,
      description: sent.description,
      phone: sent.phone,
      email: sent.email,
      cityId: cityChanged && city ? city.id : base.cityId,
      yandexMapsUrl: sent.yandexMapsUrl.trim(),
      twoGisUrl: sent.twoGisUrl.trim(),
    }

    if (addressChanged) {
      try {
        await companyAddressApi.saveAddress(shop.id, values.address)
        baseline.current = { ...baseline.current, address: values.address }
      } catch {
        setSaving(false)
        setErrors({ address: ADDRESS_FAILED })
        setFormError('Остальные изменения сохранены')
        refetchShop()
        return
      }
    }

    setSaving(false)
    setErrors({})
    setSaved(true)
    if (savedTimer.current) clearTimeout(savedTimer.current)
    savedTimer.current = setTimeout(() => setSaved(false), 2500)
    refetchShop()
  }

  const hintId = (name: string) => `${ids}-${name}-hint`
  const cityHint = 'Часы работы и время получения заказов считаются по часовому поясу города'

  return (
    <Card className="p-6">
      <div className="flex items-center gap-4 mb-6 flex-wrap">
        {shop.logoUrl ? (
          <img src={shop.logoUrl} alt="Логотип магазина" className="w-16 h-16 rounded-[18px] object-cover" />
        ) : (
          <span aria-hidden="true" className="w-16 h-16 rounded-[18px] bg-cream-deep flex items-center justify-center text-gold-dark font-serif text-2xl">
            {shop.name.trim()[0]?.toUpperCase()}
          </span>
        )}
        <div className="min-w-0">
          <h2 className="text-[15px] font-semibold text-ink mb-1.5">Профиль магазина</h2>
          <div className="flex items-center gap-x-3 gap-y-1 flex-wrap">
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
            <Button variant="secondary" size="sm" loading={logo.isPending} onClick={() => logoInput.current?.click()}>
              {shop.logoUrl ? 'Заменить логотип' : 'Загрузить логотип'}
            </Button>
            <span className="text-xs text-muted">JPEG, PNG или WEBP, до 5 МБ</span>
          </div>
          {logoError && <p className="text-xs text-danger mt-1">{logoError}</p>}
        </div>
      </div>

      <form
        className="flex flex-col gap-5"
        noValidate
        onSubmit={(e) => {
          e.preventDefault()
          if (!saving) begin()
        }}
      >
        <div className="flex flex-col gap-4">
          <Input
            label="Название *"
            value={values.name}
            maxLength={200}
            error={errors.name}
            onChange={(e) => set('name', e.target.value)}
          />
          <div className="flex flex-col gap-1.5">
            <label htmlFor={`${ids}-description`} className="text-[13px] font-medium text-[#4A4038]">
              Описание
            </label>
            <textarea
              id={`${ids}-description`}
              rows={2}
              value={values.description}
              onChange={(e) => set('description', e.target.value)}
              className="rounded-xl border border-line px-4 py-3 text-sm outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep resize-none bg-white text-ink"
            />
          </div>
        </div>

        <div className="grid sm:grid-cols-2 gap-4">
          <PhoneInput label="Телефон для покупателей" value={values.phone} onChange={(v) => set('phone', v)} />
          <div className="flex flex-col gap-1">
            <Input
              label="Email для покупателей"
              type="email"
              value={values.email}
              aria-describedby={hintId('email')}
              onChange={(e) => set('email', e.target.value)}
            />
            <p id={hintId('email')} className="text-xs text-muted">
              Виден на странице магазина
            </p>
          </div>
        </div>

        <fieldset className="flex flex-col gap-4 border-t border-line pt-5">
          <legend className="text-[13px] font-semibold text-ink pr-2">Адрес и карты</legend>
          <div className="grid sm:grid-cols-2 gap-4">
            <div className="flex flex-col gap-1">
              <CityCombobox
                label="Город"
                value={city}
                onChange={(c) => {
                  setCity(c)
                  setSaved(false)
                  setErrors((e) => ({ ...e, city: undefined }))
                }}
                error={errors.city}
              />
              <p className="text-xs text-muted">{cityHint}</p>
              {city && (
                <p className="text-xs text-muted">
                  Часовой пояс: {formatCityTimeZone(city.label, city.utcOffsetMinutes, city.timeZoneId)}
                </p>
              )}
            </div>
            <Input
              label="Адрес"
              value={values.address}
              maxLength={300}
              error={errors.address}
              onChange={(e) => set('address', e.target.value)}
            />
          </div>
          <div className="grid sm:grid-cols-2 gap-4">
            <Input
              label="Яндекс Карты"
              placeholder="https://yandex.ru/maps/org/..."
              value={values.yandexMapsUrl}
              error={errors.yandexMapsUrl}
              onChange={(e) => set('yandexMapsUrl', e.target.value)}
            />
            <Input
              label="2ГИС"
              placeholder="https://2gis.ru/..."
              value={values.twoGisUrl}
              error={errors.twoGisUrl}
              onChange={(e) => set('twoGisUrl', e.target.value)}
            />
          </div>
          <p className="text-xs text-muted">Адрес виден покупателям на странице магазина</p>
        </fieldset>

        {formError && <InlineError>{formError}</InlineError>}
        <div className="flex items-center gap-3">
          <Button type="submit" loading={saving}>
            Сохранить
          </Button>
          <SavedNote show={saved} />
        </div>
      </form>

      {step === 'tz' && city && (
        <Modal title="Сменить город?" onClose={() => setStep(null)}>
          <div className="flex flex-col gap-4">
            <p className="text-sm text-ink-soft">
              Часовой пояс магазина изменится:{' '}
              {shop.utcOffsetMinutes != null ? formatUtcOffset(shop.utcOffsetMinutes) : shop.timeZoneId} →{' '}
              {formatUtcOffset(city.utcOffsetMinutes)}. Часы работы и время получения заказов будут считаться по новому
              поясу.
            </p>
            <div className="flex gap-3">
              <Button variant="secondary" className="flex-1" onClick={() => setStep(null)}>
                Отмена
              </Button>
              <Button className="flex-1" onClick={afterTimeZone}>
                Сменить город
              </Button>
            </div>
          </div>
        </Modal>
      )}
      {step === 'address' && <PublicAddressNotice onConfirmed={() => void run()} onCancel={() => setStep(null)} />}
    </Card>
  )
}
