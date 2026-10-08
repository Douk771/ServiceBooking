import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Controller, useForm } from 'react-hook-form'
import { legalApi } from '@/api/legal'
import { useAuthStore } from '@/store/authStore'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { useDebouncedValue } from '@/hooks/useDebouncedValue'
import { isRussianPhone } from '@/utils/phone'
import { staysCompaniesApi } from '../../api/staysCompanies'
import { InlineError } from '../../components/StatePanels'
import { StayNotice } from '../../components/StayNotice'
import { COMPANY_SLUG_FORMAT_TEXT, isCompanySlugValid, normalizeSlugInput } from '../../utils/slug'
import { getStayErrorMessage, readConflict } from '../../utils/stayError'
import type { StaysConflictDto } from '../../types'

interface FormData {
  name: string
  slug: string
  description: string
  phone: string
}

/**
 * `/cabinet/new` (US-37-01…03) — a «Дома» company. The city is always Sheregesh (the server sets it, `cityId` is not sent). The owner
 * accepts the agreement with the company (`TermsOwner`) and may start the trial in the same step (conditions shown first). The answer
 * carries a fresh token that MUST replace the stored one, otherwise the owner meets an owner-gate 451 on their own new company.
 */
export function CreateCompanyPage() {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const { user, token, setAuth } = useAuthStore()
  const [termsAccepted, setTermsAccepted] = useState(false)
  const [termsError, setTermsError] = useState('')
  const [slugTouched, setSlugTouched] = useState(false)
  const [startTrial, setStartTrial] = useState(true)

  const { data: manifest } = useQuery({ queryKey: ['legal-documents'], queryFn: legalApi.getManifest })
  const ownerTerms = manifest?.documents.find((d) => d.type === 'TermsOwner')
  const trial = useQuery({ queryKey: ['stays-trial'], queryFn: staysCompaniesApi.trialState, retry: false })
  const trialOffered = trial.data?.offered === true && trial.data.eligible === true && !!trial.data.termsVersion

  const {
    register,
    handleSubmit,
    control,
    setValue,
    watch,
    formState: { errors },
  } = useForm<FormData>({ defaultValues: { name: '', slug: '', description: '', phone: '' } })
  const name = watch('name')
  const slug = watch('slug')
  const debouncedName = useDebouncedValue(name.trim(), 500)
  const debouncedSlug = useDebouncedValue(slug.trim(), 500)

  // Suggest an address from the name until the owner types in the field themselves.
  const suggestion = useQuery({
    queryKey: ['stays-slug-suggest', debouncedName],
    queryFn: () => staysCompaniesApi.slugCheck({ name: debouncedName }),
    enabled: !slugTouched && debouncedName.length > 0,
    retry: false,
  })
  useEffect(() => {
    if (!slugTouched && suggestion.data?.suggested) setValue('slug', suggestion.data.suggested, { shouldValidate: false })
  }, [suggestion.data, slugTouched, setValue])

  const formatOk = debouncedSlug === '' || isCompanySlugValid(debouncedSlug)
  const availability = useQuery({
    queryKey: ['stays-slug-check', debouncedSlug],
    queryFn: () => staysCompaniesApi.slugCheck({ slug: debouncedSlug }),
    enabled: debouncedSlug.length > 0 && formatOk,
    retry: false,
  })

  const create = useMutation({
    mutationFn: (d: FormData) =>
      staysCompaniesApi.create({
        name: d.name.trim(),
        slug: d.slug.trim() || null,
        description: d.description.trim() || null,
        phone: d.phone,
        ownerTermsVersion: ownerTerms!.version,
        trialTermsVersion: trialOffered && startTrial ? trial.data!.termsVersion! : null,
      }),
    onSuccess: (res) => {
      // A new token (claim `lco`): without it the owner gets an owner-gate 451 on their own new company.
      if (user && token) setAuth(user, res.token)
      void qc.invalidateQueries({ queryKey: ['stays-my-companies'] })
      void qc.invalidateQueries({ queryKey: ['kinds-summary'] })
      void qc.invalidateQueries({ queryKey: ['stays-trial'] })
      navigate(`/cabinet/${res.company.id}/settings`, { replace: true, state: { trial: res.trial ?? null } })
    },
  })

  function submit(d: FormData) {
    if (!ownerTerms) {
      setTermsError('Не удалось загрузить текст соглашения. Обновите страницу и попробуйте снова.')
      return
    }
    setTermsError('')
    create.mutate(d)
  }

  const conflict = create.isError ? readConflict<StaysConflictDto>(create.error) : null
  const slugHint = !formatOk
    ? { ok: false, text: COMPANY_SLUG_FORMAT_TEXT }
    : availability.data
      ? availability.data.available
        ? { ok: true, text: 'Адрес свободен' }
        : { ok: false, text: availability.data.conflict?.message ?? 'Этот адрес недоступен' }
      : null

  return (
    <main className="mx-auto max-w-[640px] px-4 pt-10 sm:px-8">
      <h1 className="mb-1 font-serif text-[32px] text-ink">Подключить дома</h1>
      <p className="mb-8 text-sm text-ink-soft">
        Несколько минут — и у вас будет страница на dom.ezbook.ru. Дома, цены и реквизиты для оплаты добавите следующими шагами.
      </p>

      <form onSubmit={handleSubmit(submit)} className="flex flex-col gap-5 rounded-3xl border border-line bg-white p-6 sm:p-8" noValidate>
        <Input
          label="Название компании *"
          placeholder="Лесные дома"
          maxLength={200}
          error={errors.name?.message}
          {...register('name', { required: 'Укажите название', validate: (v) => v.trim().length > 0 || 'Укажите название' })}
        />

        <div>
          <Input
            label="Адрес на dom.ezbook.ru"
            placeholder="lesnye-doma"
            maxLength={50}
            autoCapitalize="none"
            autoCorrect="off"
            spellCheck={false}
            {...register('slug', {
              onChange: (e) => {
                setSlugTouched(true)
                setValue('slug', normalizeSlugInput(e.target.value))
              },
            })}
          />
          <p className="mt-1.5 text-xs text-muted">
            Ссылка будет такой: dom.ezbook.ru/<span className="text-ink-soft">{slug || 'адрес'}</span>
          </p>
          {slugHint && (
            <p role="status" className={`mt-1 text-xs font-medium ${slugHint.ok ? 'text-success' : 'text-danger'}`}>
              {slugHint.text}
            </p>
          )}
        </div>

        <div className="flex flex-col gap-1.5">
          <label htmlFor="company-description" className="text-[13px] font-medium text-[#4A4038]">
            Описание
          </label>
          <textarea
            id="company-description"
            rows={3}
            maxLength={2000}
            className="resize-none rounded-xl border border-line bg-white px-4 py-3 text-sm text-ink outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep"
            {...register('description')}
          />
        </div>

        <div>
          <Controller
            name="phone"
            control={control}
            rules={{ validate: (v) => isRussianPhone(v) || 'Введите номер телефона в формате +7 (900) 000-00-00' }}
            render={({ field }) => <PhoneInput label="Телефон для гостей *" value={field.value} onChange={field.onChange} error={errors.phone?.message} />}
          />
          <StayNotice textKey="StayPublicContactsNotice" className="mt-2" />
        </div>

        <p className="rounded-xl bg-cream-deep px-4 py-3 text-sm text-ink-soft">
          Город — <span className="font-semibold text-ink">Шерегеш</span>. Часовой пояс — по городу: Asia/Novokuznetsk.
        </p>

        {trial.data?.offered && (
          <fieldset className="rounded-2xl border border-line bg-cream-deep/40 p-4" data-testid="trial-block">
            <legend className="px-1 text-[13px] font-semibold text-ink">Пробный период</legend>
            {trialOffered ? (
              <>
                <label className="flex cursor-pointer items-start gap-2.5">
                  <input type="checkbox" className="mt-0.5 h-5 w-5 accent-gold" checked={startTrial} onChange={(e) => setStartTrial(e.target.checked)} />
                  <span className="text-[13px] leading-snug text-ink-soft">
                    Начать пробный период — {trial.data.durationDays} дн. без оплаты тарифа. Условия:
                    {trial.data.termsText && <span className="mt-1 block whitespace-pre-line text-xs text-muted">{trial.data.termsText}</span>}
                  </span>
                </label>
              </>
            ) : (
              <p className="text-[13px] text-ink-soft">{trial.data.message ?? 'Пробный период для вашего аккаунта недоступен.'}</p>
            )}
          </fieldset>
        )}

        <label className="flex cursor-pointer items-start gap-2.5 rounded-2xl border border-line bg-cream-deep/40 p-3.5">
          <input type="checkbox" className="mt-0.5 h-5 w-5 accent-gold" checked={termsAccepted} onChange={(e) => setTermsAccepted(e.target.checked)} />
          <span className="text-[13px] leading-snug text-ink-soft">
            Я принимаю{' '}
            <Link to="/terms-owner" target="_blank" className="text-gold hover:text-gold-dark">
              Соглашение с компанией и поручение на обработку персональных данных
            </Link>
          </span>
        </label>

        {termsError && <InlineError>{termsError}</InlineError>}
        {create.isError && <InlineError>{conflict?.message ?? getStayErrorMessage(create.error, 'Не удалось создать компанию.')}</InlineError>}

        <div className="flex gap-3 pt-1">
          <Button type="button" variant="secondary" className="min-h-[44px] flex-1" onClick={() => navigate('/cabinet')}>
            Отмена
          </Button>
          <Button type="submit" className="min-h-[44px] flex-1" loading={create.isPending} disabled={!termsAccepted || !ownerTerms}>
            Создать компанию
          </Button>
        </div>
      </form>
    </main>
  )
}
