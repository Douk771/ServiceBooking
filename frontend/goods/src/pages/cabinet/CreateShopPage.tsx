import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Controller, useForm } from 'react-hook-form'
import { legalApi } from '@/api/legal'
import { useAuthStore } from '@/store/authStore'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { CityCombobox } from '@/components/ui/CityCombobox'
import { PublicAddressNotice } from '@/components/company/PublicAddressNotice'
import { useDebouncedValue } from '@/hooks/useDebouncedValue'
import { formatCityTimeZone } from '@/utils/timezone'
import type { City } from '@/types'
import { shopsApi } from '../../api/shops'
import { InlineError } from '../../components/StatePanels'
import { getCatalogErrorMessage } from '../../utils/catalogError'
import { isSlugFormatValid, normalizeSlugInput } from '../../utils/slug'

interface FormData {
  name: string
  slug: string
  description: string
  address: string
  phone: string
  email: string
}

/**
 * US-23-09 — same rules as creating a salon on ezbook: TermsOwner accepted first, city required, address
 * optional with the public-address notice before it is sent, account company limit explained (402).
 * The response carries a fresh token (claim `lco`) that MUST replace the stored one (§409.1).
 */
export function CreateShopPage() {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const { user, token, setAuth } = useAuthStore()
  const [city, setCity] = useState<City | null>(null)
  const [cityError, setCityError] = useState('')
  const [termsAccepted, setTermsAccepted] = useState(false)
  const [termsError, setTermsError] = useState('')
  const [slugTouched, setSlugTouched] = useState(false)
  const [noticeConfirmedFor, setNoticeConfirmedFor] = useState<string | null>(null)
  const [pending, setPending] = useState<FormData | null>(null)

  const { data: manifest } = useQuery({ queryKey: ['legal-documents'], queryFn: legalApi.getManifest })
  const ownerTerms = manifest?.documents.find((d) => d.type === 'TermsOwner')

  const {
    register,
    handleSubmit,
    control,
    setValue,
    watch,
    formState: { errors },
  } = useForm<FormData>({ defaultValues: { name: '', slug: '', description: '', address: '', phone: '', email: '' } })
  const name = watch('name')
  const slug = watch('slug')
  const debouncedName = useDebouncedValue(name.trim(), 500)
  const debouncedSlug = useDebouncedValue(slug.trim(), 500)

  // Suggest an address from the name until the owner edits the field themselves (§409.3).
  const suggestion = useQuery({
    queryKey: ['slug-suggest', debouncedName],
    queryFn: () => shopsApi.slugCheck({ name: debouncedName }),
    enabled: !slugTouched && debouncedName.length > 0,
    retry: false,
  })
  useEffect(() => {
    if (!slugTouched && suggestion.data?.slug) setValue('slug', suggestion.data.slug, { shouldValidate: false })
  }, [suggestion.data, slugTouched, setValue])

  const formatOk = debouncedSlug === '' || isSlugFormatValid(debouncedSlug)
  const availability = useQuery({
    queryKey: ['slug-check', debouncedSlug],
    queryFn: () => shopsApi.slugCheck({ slug: debouncedSlug }),
    enabled: debouncedSlug.length > 0 && formatOk,
    retry: false,
  })

  const create = useMutation({
    mutationFn: (d: FormData) =>
      shopsApi.create({
        name: d.name.trim(),
        slug: d.slug.trim(),
        cityId: city!.id,
        timeZoneId: null,
        description: d.description.trim() || undefined,
        address: d.address.trim() || undefined,
        phone: d.phone || undefined,
        email: d.email.trim() || undefined,
        ownerTerms: { version: ownerTerms!.version },
      }),
    onSuccess: (res) => {
      // A new token (claim `lco`): without it the owner gets an owner-gate 451 on their own new shop.
      if (user && token) setAuth(user, res.token)
      void qc.invalidateQueries({ queryKey: ['my-shops'] })
      void qc.invalidateQueries({ queryKey: ['kinds-summary'] })
      navigate(`/cabinet/${res.shop.id}/catalog`, { replace: true })
    },
  })

  function submit(d: FormData) {
    if (!city) {
      setCityError('Укажите город магазина')
      return
    }
    if (!ownerTerms) {
      setTermsError('Не удалось загрузить текст соглашения. Обновите страницу и попробуйте снова.')
      return
    }
    setCityError('')
    setTermsError('')
    // The notice is shown on first fill and on every edit of the address text, before it is sent (§409.1).
    if (d.address.trim() && noticeConfirmedFor !== d.address.trim()) {
      setPending(d)
      return
    }
    create.mutate(d)
  }

  const slugHint = !formatOk
    ? { ok: false, text: 'Адрес — латиница, цифры и дефис, от 3 до 50 символов' }
    : availability.data
      ? availability.data.available
        ? { ok: true, text: 'Адрес свободен' }
        : { ok: false, text: availability.data.reason ?? 'Этот адрес недоступен' }
      : null

  return (
    <main className="max-w-[640px] mx-auto px-4 sm:px-8 pt-10">
      <h1 className="font-serif text-[32px] text-ink mb-1">Открыть магазин</h1>
      <p className="text-sm text-ink-soft mb-8">Несколько минут — и покупатели смогут заказывать по вашей ссылке.</p>

      <form onSubmit={handleSubmit(submit)} className="flex flex-col gap-5 bg-white border border-line rounded-3xl p-6 sm:p-8" noValidate>
        <Input
          label="Название *"
          placeholder="Шаурма на Ленина"
          maxLength={200}
          error={errors.name?.message}
          {...register('name', { required: 'Укажите название магазина', validate: (v) => v.trim().length > 0 || 'Укажите название магазина' })}
        />

        <div>
          <Input
            label="Адрес магазина на goods *"
            placeholder="shaurma-na-lenina"
            maxLength={50}
            autoCapitalize="none"
            autoCorrect="off"
            spellCheck={false}
            error={errors.slug?.message}
            {...register('slug', {
              required: 'Укажите адрес магазина',
              onChange: (e) => {
                setSlugTouched(true)
                setValue('slug', normalizeSlugInput(e.target.value))
              },
            })}
          />
          <p className="text-xs text-muted mt-1.5">
            Ссылка будет такой: goods.ezbook.ru/<span className="text-ink-soft">{slug || 'адрес'}</span>
          </p>
          {slugHint && (
            <p role="status" className={`text-xs mt-1 font-medium ${slugHint.ok ? 'text-success' : 'text-danger'}`}>
              {slugHint.text}
            </p>
          )}
        </div>

        <CityCombobox
          label="Город *"
          value={city}
          onChange={(c) => {
            setCity(c)
            if (c) setCityError('')
          }}
          error={cityError}
        />
        {city && (
          <p className="text-xs text-muted -mt-3">Часовой пояс: {formatCityTimeZone(city.label, city.utcOffsetMinutes, city.timeZoneId)}</p>
        )}

        <div className="flex flex-col gap-1.5">
          <label htmlFor="shop-description" className="text-[13px] font-medium text-[#4A4038]">
            Описание
          </label>
          <textarea
            id="shop-description"
            rows={2}
            className="rounded-xl border border-line px-4 py-3 text-sm outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep resize-none bg-white text-ink"
            {...register('description')}
          />
        </div>

        <Input label="Адрес" placeholder="ул. Ленина, 12" {...register('address')} />
        <p className="text-xs text-muted -mt-3">
          Необязательно. Адрес будет виден покупателям на странице магазина — перед созданием мы это подтвердим.
        </p>

        <div className="grid sm:grid-cols-2 gap-4">
          <Controller
            name="phone"
            control={control}
            render={({ field }) => <PhoneInput label="Телефон для покупателей" value={field.value} onChange={field.onChange} />}
          />
          <Input label="Email" type="email" {...register('email')} />
        </div>

        <label className="flex items-start gap-2.5 cursor-pointer rounded-2xl border border-line bg-cream-deep/40 p-3.5">
          <input
            type="checkbox"
            className="w-4 h-4 mt-0.5 rounded accent-gold"
            checked={termsAccepted}
            onChange={(e) => setTermsAccepted(e.target.checked)}
          />
          <span className="text-[13px] text-ink-soft leading-snug">
            Я принимаю{' '}
            <Link to="/terms-owner" target="_blank" className="text-gold hover:text-gold-dark">
              Соглашение с компанией и поручение на обработку персональных данных
            </Link>
          </span>
        </label>

        {termsError && <InlineError>{termsError}</InlineError>}
        {create.isError && <InlineError>{getCatalogErrorMessage(create.error, 'Не удалось создать магазин.')}</InlineError>}

        <div className="flex gap-3 pt-1">
          <Button type="button" variant="secondary" className="flex-1" onClick={() => navigate('/cabinet')}>
            Отмена
          </Button>
          <Button type="submit" className="flex-1" loading={create.isPending} disabled={!termsAccepted || !ownerTerms}>
            Создать магазин
          </Button>
        </div>
      </form>

      {pending && (
        <PublicAddressNotice
          onConfirmed={() => {
            setNoticeConfirmedFor(pending.address.trim())
            const d = pending
            setPending(null)
            create.mutate(d)
          }}
          onCancel={() => setPending(null)}
        />
      )}
    </main>
  )
}
