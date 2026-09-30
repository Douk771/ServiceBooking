import { useEffect, useId, useRef, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { companiesApi } from '../../api/companies'
import { companyAddressApi } from '../../api/companyAddress'
import { Button } from '../ui/Button'
import { Card } from '../ui/Card'
import { CityCombobox } from '../ui/CityCombobox'
import { InlineError } from '../ui/InlineError'
import { Input } from '../ui/Input'
import { Modal } from '../ui/Modal'
import { PhoneInput } from '../ui/PhoneInput'
import { PublicAddressNotice } from './PublicAddressNotice'
import { CARD_TITLE_CLASS } from './cardTitle'
import { planProfileZone, type ZonePlan } from './profileZone'
import { getLogoErrorMessage } from '../../utils/companyManageError'
import { httpStatusOf, plainErrorBody } from '../../utils/httpError'
import { mapLinksFieldError } from '../../utils/mapLinksFieldError'
import { formatCityTimeZone, utcOffsetMinutesOf } from '../../utils/timezone'
import type { City } from '../../types'
import type { components as C32 } from '../../types/api-cycle32.generated'

/** The address step failed; the rest of the profile is already saved (ARCHITECTURE_CYCLE26.md §552.2). */
const ADDRESS_FAILED = 'Не удалось сохранить адрес. Попробуйте ещё раз.'
const CANONICAL_PHONE = /^7\d{10}$/

/** Neutral data the card needs — built by a thin adapter per product (ShopManageDto / Company). */
export interface CompanyProfileSnapshot {
  id: string
  name: string
  description: string | null
  phone: string | null
  email: string | null
  logoUrl: string | null
  address: string | null
  yandexMapsUrl: string | null
  twoGisUrl: string | null
  /** CityCombobox value; the adapter builds `label` ("Город, Регион"). */
  city: City | null
  /** The company's EFFECTIVE zone now (manual if manual). offsetMinutes null = unknown. */
  zone: { id: string | null; offsetMinutes: number | null; isManual: boolean }
}

export interface CompanyProfileTexts {
  title: string
  logoAlt: string
  nameRequired: string
  phoneLabel: string
  emailLabel: string
  emailHint: string
  cityHint: string
  addressHint: string
  saveFailed: string
  timeZoneChange: (from: string, to: string) => string
}

/** Shop only (API_CONTRACT_CYCLE29.md §29.24): the flag forbids only a DIFFERENT UTC offset. */
export interface TimeZoneLock {
  allowed: boolean
  lockedText: string | null
  fallbackText: string
}

export interface CompanyProfileCardProps {
  /** Header (logo, initial) reads it live; form fields read it ONCE at mount — mount with key={company.id}. */
  company: CompanyProfileSnapshot
  texts: CompanyProfileTexts
  /** Form-level error text: goods getGoodsErrorMessage, ezbook getCompanyManageErrorMessage. */
  errorMessage: (error: unknown, fallback: string) => string
  /** Cache invalidation after logo upload, full success, and address failure (the profile PUT did land). */
  onChanged: () => void
  /** Shop only. When set, a 409 of the profile PUT goes to the city field. */
  timeZoneLock?: TimeZoneLock
  /** Salon only: «Указать часовой пояс вручную» + IANA field (§32.6). */
  manualTimeZone?: boolean
  /** Salon only: hint when the stored phone is not canonical 7XXXXXXXXXX (§32.7). */
  legacyPhoneHint?: boolean
}

type FieldKey = 'name' | 'city' | 'address' | 'yandexMapsUrl' | 'twoGisUrl' | 'timeZone'
type FieldErrors = Partial<Record<FieldKey, string>>

interface Values {
  name: string
  description: string
  phone: string
  email: string
  address: string
  yandexMapsUrl: string
  twoGisUrl: string
}

function initialValues(c: CompanyProfileSnapshot): Values {
  return {
    name: c.name,
    description: c.description ?? '',
    phone: c.phone ?? '',
    email: c.email ?? '',
    address: c.address ?? '',
    yandexMapsUrl: c.yandexMapsUrl ?? '',
    twoGisUrl: c.twoGisUrl ?? '',
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
 * ARCHITECTURE_CYCLE32.md §32.4 — the shared «Профиль компании» block of both cabinets: one form, one «Сохранить».
 * State is local and initialised from `company` once (mount with `key={company.id}`): a re-read DTO never rebuilds
 * the form, so typing is never lost (cycle-13 lesson). `baseline` holds the values of the last successful save.
 */
export function CompanyProfileCard({
  company,
  texts,
  errorMessage,
  onChanged,
  timeZoneLock,
  manualTimeZone = false,
  legacyPhoneHint = false,
}: CompanyProfileCardProps) {
  const ids = useId()
  const [values, setValues] = useState<Values>(() => initialValues(company))
  const [city, setCity] = useState<City | null>(() => company.city)
  const [manual, setManual] = useState(() => company.zone.isManual)
  const [manualZoneId, setManualZoneId] = useState(() => (company.zone.isManual ? (company.zone.id ?? '') : ''))
  const baseline = useRef({
    values: initialValues(company),
    cityId: company.city?.id ?? null,
    zone: { ...company.zone },
  })
  const [errors, setErrors] = useState<FieldErrors>({})
  const [formError, setFormError] = useState('')
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const savedTimer = useRef<ReturnType<typeof setTimeout> | null>(null)
  useEffect(
    () => () => {
      if (savedTimer.current) clearTimeout(savedTimer.current)
    },
    [],
  )
  const [step, setStep] = useState<'tz' | 'address' | null>(null)
  const [plan, setPlan] = useState<ZonePlan | null>(null)
  const [logoError, setLogoError] = useState('')
  const logoInput = useRef<HTMLInputElement>(null)

  const set = <K extends keyof Values>(key: K, value: Values[K]) => {
    setValues((v) => ({ ...v, [key]: value }))
    setSaved(false)
  }

  const logo = useMutation({
    mutationFn: (file: File) => companiesApi.uploadLogo(company.id, file),
    onMutate: () => setLogoError(''),
    onSuccess: onChanged,
    onError: (e) => setLogoError(getLogoErrorMessage(e)),
  })

  const addressChanged = values.address.trim() !== baseline.current.values.address.trim()

  function makePlan(): ZonePlan {
    const b = baseline.current
    return planProfileZone(
      { cityId: b.cityId, zoneId: b.zone.id, offsetMinutes: b.zone.offsetMinutes, isManual: b.zone.isManual },
      { city, manual, manualZoneId },
      manualTimeZone,
      utcOffsetMinutesOf,
    )
  }

  /** §32.4.5 steps 1–3: local checks and the two confirmations. */
  function begin() {
    setFormError('')
    setSaved(false)
    if (!values.name.trim()) {
      setErrors({ name: texts.nameRequired })
      return
    }
    const p = makePlan()
    setPlan(p)
    if (p.confirm) {
      if (timeZoneLock && !timeZoneLock.allowed) {
        setErrors({ city: timeZoneLock.lockedText ?? timeZoneLock.fallbackText })
        return
      }
      setErrors({})
      setStep('tz')
      return
    }
    setErrors({})
    afterTimeZone(p)
  }

  function afterTimeZone(p: ZonePlan) {
    if (addressChanged) setStep('address')
    else {
      setStep(null)
      void run(p)
    }
  }

  /** §32.4.5 steps 5–8: PUT the profile, then the address route if the address changed. */
  async function run(p: ZonePlan) {
    setStep(null)
    setSaving(true)
    const base = baseline.current
    const sent: Values = { ...values, name: values.name.trim(), description: values.description.trim(), email: values.email.trim() }
    // Untouched links are not sent at all; a cleared one goes as "" (= remove).
    const yandexChanged = sent.yandexMapsUrl.trim() !== base.values.yandexMapsUrl.trim()
    const twoGisChanged = sent.twoGisUrl.trim() !== base.values.twoGisUrl.trim()
    const body = {
      name: sent.name,
      description: sent.description,
      phone: sent.phone,
      email: sent.email,
      ...(p.cityId !== undefined ? { cityId: p.cityId } : {}),
      ...(p.timeZoneId !== undefined ? { timeZoneId: p.timeZoneId } : {}),
      ...(yandexChanged ? { yandexMapsUrl: sent.yandexMapsUrl.trim() } : {}),
      ...(twoGisChanged ? { twoGisUrl: sent.twoGisUrl.trim() } : {}),
    } satisfies C32['schemas']['CompanyProfileUpdateInput']

    try {
      await companiesApi.update(company.id, body)
    } catch (err) {
      setSaving(false)
      setErrors(mainPutErrors(err, !yandexChanged))
      return
    }

    // The zone the company has now: a manual one, or (after a reset / city change) the city's — offset known
    // only when the city was picked in this session.
    const prevZone = base.zone
    let zone = prevZone
    if (p.timeZoneId === null) {
      zone = { id: null, offsetMinutes: p.cityId !== undefined ? (city?.utcOffsetMinutes ?? null) : null, isManual: false }
    } else if (typeof p.timeZoneId === 'string') {
      zone = { id: p.timeZoneId, offsetMinutes: utcOffsetMinutesOf(p.timeZoneId), isManual: true }
    } else if (p.cityId !== undefined && !prevZone.isManual && city) {
      zone = { id: city.timeZoneId, offsetMinutes: city.utcOffsetMinutes, isManual: false }
    }
    baseline.current = {
      values: {
        ...base.values,
        name: sent.name,
        description: sent.description,
        phone: sent.phone,
        email: sent.email,
        yandexMapsUrl: sent.yandexMapsUrl.trim(),
        twoGisUrl: sent.twoGisUrl.trim(),
      },
      cityId: p.cityId ?? base.cityId,
      zone,
    }

    if (addressChanged) {
      try {
        await companyAddressApi.saveAddress(company.id, values.address)
        baseline.current = { ...baseline.current, values: { ...baseline.current.values, address: values.address } }
      } catch {
        setSaving(false)
        setErrors({ address: ADDRESS_FAILED })
        setFormError('Остальные изменения сохранены')
        onChanged()
        return
      }
    }

    setSaving(false)
    setErrors({})
    setSaved(true)
    if (savedTimer.current) clearTimeout(savedTimer.current)
    savedTimer.current = setTimeout(() => setSaved(false), 2500)
    onChanged()
  }

  /** §32.4.6 — where an error of the main PUT goes. Routed by the raw server text, not by the mapper's wording. */
  function mainPutErrors(err: unknown, yandexNotSent: boolean): FieldErrors {
    const status = httpStatusOf(err)
    const raw = plainErrorBody(err)
    const text = () => errorMessage(err, texts.saveFailed)
    if (status === 409 && timeZoneLock) return { city: text() }
    if (status === 400) {
      if (raw.includes('Город не найден')) return { city: raw }
      if (manualTimeZone && raw === 'Неизвестный часовой пояс') return { timeZone: raw }
      let field = mapLinksFieldError(raw)
      // §567: the generic «Ссылка …» text belongs to Яндекс Карты only if that field was in the request.
      if (field === 'yandexMapsUrl' && !raw.includes('Яндекс') && yandexNotSent) field = 'twoGisUrl'
      if (field === 'yandexMapsUrl' || field === 'twoGisUrl') return { [field]: raw }
    }
    setFormError(text())
    return {}
  }

  const hintId = (name: string) => `${ids}-${name}-hint`
  const storedPhone = baseline.current.values.phone
  const showLegacyPhone =
    legacyPhoneHint && storedPhone !== '' && !CANONICAL_PHONE.test(storedPhone) && values.phone === storedPhone
  const zoneLine = zoneLineText()

  function zoneLineText(): string | null {
    const z = manual ? manualZoneId.trim() : ''
    if (manualTimeZone && z !== '') return `Часовой пояс: ${z} — указан вручную`
    if (!city) return null
    const b = baseline.current
    const fresh = city.id !== b.cityId
    if (!fresh && b.zone.isManual) return `Часовой пояс: как у города ${city.label}`
    return `Часовой пояс: ${formatCityTimeZone(city.label, city.utcOffsetMinutes, city.timeZoneId)}`
  }

  return (
    <Card className="p-6">
      <div className="flex items-center gap-4 mb-6 flex-wrap">
        {company.logoUrl ? (
          <img src={company.logoUrl} alt={texts.logoAlt} className="w-16 h-16 rounded-[18px] object-cover" />
        ) : (
          <span
            aria-hidden="true"
            className="w-16 h-16 rounded-[18px] bg-cream-deep flex items-center justify-center text-gold-dark font-serif text-2xl"
          >
            {company.name.trim()[0]?.toUpperCase()}
          </span>
        )}
        <div className="min-w-0">
          <h2 className={`${CARD_TITLE_CLASS} mb-1.5`}>{texts.title}</h2>
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
              {company.logoUrl ? 'Заменить логотип' : 'Загрузить логотип'}
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
          {showLegacyPhone ? (
            <div className="flex flex-col gap-1">
              <PhoneInput
                label={texts.phoneLabel}
                value={values.phone}
                aria-describedby={hintId('phone')}
                onChange={(v) => set('phone', v)}
              />
              <p id={hintId('phone')} className="text-xs text-muted">
                Сейчас сохранён номер «{storedPhone}». Он не изменится, пока вы не исправите поле; новый номер вводится в
                формате +7.
              </p>
            </div>
          ) : (
            <PhoneInput label={texts.phoneLabel} value={values.phone} onChange={(v) => set('phone', v)} />
          )}
          <div className="flex flex-col gap-1">
            <Input
              label={texts.emailLabel}
              type="email"
              value={values.email}
              aria-describedby={hintId('email')}
              onChange={(e) => set('email', e.target.value)}
            />
            <p id={hintId('email')} className="text-xs text-muted">
              {texts.emailHint}
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
                describedBy={hintId('city')}
                onChange={(c) => {
                  setCity(c)
                  setSaved(false)
                  setErrors((e) => ({ ...e, city: undefined }))
                }}
                error={errors.city}
              />
              <p id={hintId('city')} className="text-xs text-muted">
                {texts.cityHint}
              </p>
              {zoneLine && <p className="text-xs text-muted">{zoneLine}</p>}
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
          {manualTimeZone && (
            <>
              <label className="flex items-center gap-2 text-sm text-ink cursor-pointer has-[:disabled]:opacity-60 has-[:disabled]:cursor-not-allowed">
                <input
                  type="checkbox"
                  className="w-4 h-4 accent-gold"
                  checked={manual}
                  disabled={!city}
                  onChange={(e) => {
                    setManual(e.target.checked)
                    setSaved(false)
                    setErrors((er) => ({ ...er, timeZone: undefined }))
                  }}
                />
                Указать часовой пояс вручную (IANA, например Asia/Barnaul)
              </label>
              {manual && (
                <Input
                  label="Часовой пояс (IANA)"
                  placeholder="Asia/Barnaul"
                  className="font-mono"
                  value={manualZoneId}
                  error={errors.timeZone}
                  onChange={(e) => {
                    setManualZoneId(e.target.value)
                    setSaved(false)
                  }}
                />
              )}
            </>
          )}
          <p className="text-xs text-muted">{texts.addressHint}</p>
        </fieldset>

        {formError && <InlineError>{formError}</InlineError>}
        <div className="flex items-center gap-3">
          <Button type="submit" loading={saving}>
            Сохранить
          </Button>
          <SavedNote show={saved} />
        </div>
      </form>

      {step === 'tz' && plan?.confirm && (
        <Modal title="Сменить город?" onClose={() => setStep(null)}>
          <div className="flex flex-col gap-4">
            <p className="text-sm text-ink-soft">{texts.timeZoneChange(plan.confirm.from, plan.confirm.to)}</p>
            <div className="flex gap-3">
              <Button variant="secondary" className="flex-1" onClick={() => setStep(null)}>
                Отмена
              </Button>
              <Button className="flex-1" onClick={() => plan && afterTimeZone(plan)}>
                Сменить город
              </Button>
            </div>
          </div>
        </Modal>
      )}
      {step === 'address' && plan && (
        <PublicAddressNotice onConfirmed={() => void run(plan)} onCancel={() => setStep(null)} />
      )}
    </Card>
  )
}
