import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { companiesApi, type CreateCompanyPayload } from '../../api/companies'
import { legalApi } from '../../api/legal'
import { PublicAddressNotice } from './PublicAddressNotice'
import { Button } from '../ui/Button'
import { Input } from '../ui/Input'
import { Modal } from '../ui/Modal'
import { CityCombobox } from '../ui/CityCombobox'
import { withOwnerRole } from '../../utils/ownerRole'
import { useAuthStore } from '../../store/authStore'
import { getCreateCompanyErrorMessage } from '../../utils/companyError'
import { formatCityTimeZone } from '../../utils/timezone'
import type { Company, City } from '../../types'

function slugify(str: string) {
  return str
    .toLowerCase()
    .replace(
      /[а-яё]/g,
      (c: string) =>
        (
          ({
            а: 'a',
            б: 'b',
            в: 'v',
            г: 'g',
            д: 'd',
            е: 'e',
            ё: 'yo',
            ж: 'zh',
            з: 'z',
            и: 'i',
            й: 'j',
            к: 'k',
            л: 'l',
            м: 'm',
            н: 'n',
            о: 'o',
            п: 'p',
            р: 'r',
            с: 's',
            т: 't',
            у: 'u',
            ф: 'f',
            х: 'h',
            ц: 'ts',
            ч: 'ch',
            ш: 'sh',
            щ: 'sch',
            ъ: '',
            ы: 'y',
            ь: '',
            э: 'e',
            ю: 'yu',
            я: 'ya',
          }) as Record<string, string>
        )[c] ?? c,
    )
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
}

interface FormData {
  name: string
  slug: string
  description: string
  address: string
  phone: string
  email: string
  allowSelfBooking: boolean
  showInPublicListing: boolean
}

/** Диалог «Создать компанию» (кабинет и маршрут /cabinet/new). Общий для обоих мест. */
export function CreateCompanyDialog({ onClose, onCreated }: { onClose: () => void; onCreated?: (company: Company) => void }) {
  const [city, setCity] = useState<City | null>(null)
  const [cityError, setCityError] = useState('')
  const [termsError, setTermsError] = useState('')
  const [ownerTermsAccepted, setOwnerTermsAccepted] = useState(false)
  // ARCHITECTURE_CYCLE13.md §220.2/§220.4 — the public-address notice gates the address text ITSELF:
  // it must reappear if the address is edited again after being confirmed once.
  const [addressNoticeConfirmedFor, setAddressNoticeConfirmedFor] = useState<string | null>(null)
  const [showAddressNotice, setShowAddressNotice] = useState(false)
  const [pendingSubmit, setPendingSubmit] = useState<FormData | null>(null)
  const qc = useQueryClient()
  const { user, token, setAuth } = useAuthStore()
  // §42.1 — `ownerTerms.version` is pinned to the version shown at the moment of submission.
  const { data: manifest } = useQuery({ queryKey: ['legal-documents'], queryFn: legalApi.getManifest })
  const ownerTerms = manifest?.documents.find((d) => d.type === 'TermsOwner')

  const {
    register,
    handleSubmit,
    setValue,
    formState: { errors },
  } = useForm<FormData>({ defaultValues: { allowSelfBooking: true, showInPublicListing: true } })

  const create = useMutation({
    mutationFn: (data: CreateCompanyPayload) => companiesApi.create(data),
    onSuccess: (res) => {
      // §42.1 (BREAKING № 3) — fresh token (claim `lco`) must replace the stored one; roles are
      // stored client-side, so CompanyOwner is added for a user who had none (no re-login needed).
      if (user && token) setAuth({ ...user, roles: withOwnerRole(user.roles) }, res.token)
      qc.invalidateQueries({ queryKey: ['my-companies'] })
      if (onCreated) onCreated(res.company)
      else onClose()
    },
  })

  function submitCompany(d: FormData) {
    if (!city) {
      setCityError('Укажите город салона')
      return
    }
    if (!ownerTerms) return
    setCityError('')
    create.mutate({
      name: d.name,
      slug: d.slug || slugify(d.name),
      description: d.description || undefined,
      address: d.address || undefined,
      phone: d.phone || undefined,
      email: d.email || undefined,
      allowSelfBooking: d.allowSelfBooking,
      showInPublicListing: d.showInPublicListing,
      cityId: city.id,
      ownerTerms: { version: ownerTerms.version },
    })
  }

  return (
    <>
    <Modal
      title="Создать компанию"
      onClose={onClose}
    >
      <form
        onSubmit={handleSubmit((d) => {
          // US-30 п. 5 — validated before the notice gate below, not just inside `submitCompany`:
          // the notice writes an entry to the consent ledger the moment it's confirmed (review
          // finding, cycle 13), so a missing city must fail BEFORE that write, not after it.
          if (!city) {
            setCityError('Укажите город салона')
            return
          }
          // Same reasoning for `ownerTerms` (review finding, cycle 13): if `GET
          // /api/legal/documents` hasn't produced a `TermsOwner` entry by submit time, the
          // owner must see WHY nothing happened rather than have the notice write a consent
          // ledger entry for a submission that then silently no-ops in `submitCompany`.
          if (!ownerTerms) {
            setTermsError('Не удалось загрузить текст соглашения. Обновите страницу и попробуйте снова.')
            return
          }
          setTermsError('')
          // ARCHITECTURE_CYCLE13.md §220.2 — shown on first fill AND on any later edit, before
          // saving. An address that's already been confirmed once at this exact text can submit
          // straight through; anything else routes through the notice first.
          if (d.address && addressNoticeConfirmedFor !== d.address) {
            setPendingSubmit(d)
            setShowAddressNotice(true)
            return
          }
          submitCompany(d)
        })}
        className="flex flex-col gap-4"
      >
        <Input
          label="Название *"
          placeholder="Салон красоты «Розы»"
          error={errors.name?.message}
          {...register('name', {
            required: 'Введите название',
            onChange: (e) => setValue('slug', slugify(e.target.value)),
          })}
        />
        <Input
          label="URL-адрес (slug) *"
          placeholder="rozy-salon"
          error={errors.slug?.message}
          {...register('slug', { required: true })}
        />
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
          <p className="text-xs text-muted -mt-2.5">
            Часовой пояс: {formatCityTimeZone(city.label, city.utcOffsetMinutes, city.timeZoneId)}
          </p>
        )}
        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-medium text-[#4A4038]">Описание</label>
          <textarea
            rows={2}
            className="rounded-xl border border-line px-4 py-3 text-sm outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep resize-none bg-white text-ink"
            {...register('description')}
          />
        </div>
        <Input label="Адрес" {...register('address')} />
        <div className="grid grid-cols-2 gap-3">
          <Input label="Телефон" {...register('phone')} />
          <Input label="Email" type="email" {...register('email')} />
        </div>
        <label className="flex items-center gap-3 cursor-pointer">
          <input type="checkbox" className="w-4 h-4 accent-gold rounded" {...register('allowSelfBooking')} />
          <span className="text-sm text-ink-soft">Разрешить клиентам записываться самостоятельно</span>
        </label>
        <label className="flex items-center gap-3 cursor-pointer">
          <input type="checkbox" className="w-4 h-4 accent-gold rounded" {...register('showInPublicListing')} />
          <span className="text-sm text-ink-soft">Показывать компанию в общем списке</span>
        </label>

        {/* API_CONTRACT_CYCLE5.md §42.1 (BREAKING № 3) — creating a company now requires
            accepting TermsOwner (D3), separate from the client TermsClient accepted at
            registration (US-65 п. 2: a different document, a different moment). */}
        <label className="flex items-start gap-2.5 cursor-pointer rounded-2xl border border-line bg-cream-deep/40 p-3.5">
          <input
            type="checkbox"
            className="w-4 h-4 mt-0.5 rounded accent-gold"
            checked={ownerTermsAccepted}
            onChange={(e) => setOwnerTermsAccepted(e.target.checked)}
          />
          <span className="text-[13px] text-ink-soft leading-snug">
            Я принимаю{' '}
            <Link to="/terms-owner" target="_blank" className="text-gold hover:text-gold-dark">
              Соглашение с компанией и поручение на обработку персональных данных
            </Link>
          </span>
        </label>

        {termsError && <p className="text-sm text-danger">{termsError}</p>}
        {create.isError && <p className="text-sm text-danger">{getCreateCompanyErrorMessage(create.error)}</p>}
        <div className="flex gap-3 pt-1">
          <Button
            type="button"
            variant="secondary"
            className="flex-1"
            onClick={onClose}
          >
            Отмена
          </Button>
          <Button type="submit" className="flex-1" loading={create.isPending} disabled={!ownerTermsAccepted || !ownerTerms}>
            Создать
          </Button>
        </div>
      </form>
    </Modal>

      {showAddressNotice && pendingSubmit && (
        <PublicAddressNotice
          onConfirmed={() => {
            setAddressNoticeConfirmedFor(pendingSubmit.address)
            setShowAddressNotice(false)
            submitCompany(pendingSubmit)
            setPendingSubmit(null)
          }}
          onCancel={() => {
            setShowAddressNotice(false)
            setPendingSubmit(null)
          }}
        />
      )}
    </>
  )
}
