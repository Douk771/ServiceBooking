import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { InlineError } from '@/components/ui/InlineError'
import { Input } from '@/components/ui/Input'
import { staysCompaniesApi } from '../../api/staysCompanies'
import type { StayProviderStatus, StaysCompanyManageDto } from '../../types'
import {
  PROVIDER_STATUSES,
  innLengthFor,
  ogrnLengthFor,
  providerFieldOfError,
  validateProvider,
  type ProviderErrors,
  type ProviderForm,
} from '../../utils/settingsForm'
import { getStayErrorMessage, httpStatus, plainBody } from '../../utils/stayError'
import { SavedNote, SectionCard, SelectField } from './formParts'

/**
 * «Об исполнителе» (ЮР-3, Т37-03): the data of the person who rents the houses out. Required to take bookings with a prepayment.
 * What the guest sees: public pages — status and INN (for a company or a sole proprietor everything); the full name of a private
 * person or a self-employed one — only on the page of THEIR booking.
 */
export function ProviderCard({ company, onSaved }: { company: StaysCompanyManageDto; onSaved: () => void }) {
  const p = company.provider
  const [f, setF] = useState<ProviderForm>({
    status: p?.status ?? '',
    name: p?.name ?? '',
    inn: p?.inn ?? '',
    ogrn: p?.ogrn ?? '',
    claimsAddress: p?.claimsAddress ?? '',
  })
  const [errors, setErrors] = useState<ProviderErrors>({})
  const [formError, setFormError] = useState('')
  const [saved, setSaved] = useState(false)

  const set = <K extends keyof ProviderForm>(k: K, v: ProviderForm[K]) => {
    setF((x) => ({ ...x, [k]: v }))
    setSaved(false)
  }

  const save = useMutation({
    mutationFn: () => {
      const status = f.status as StayProviderStatus
      return staysCompaniesApi.updateProvider(company.id, {
        status,
        name: f.name.trim(),
        inn: f.inn.replace(/\D/g, ''),
        ogrn: ogrnLengthFor(status) ? f.ogrn.replace(/\D/g, '') : null,
        claimsAddress: f.claimsAddress.trim(),
      })
    },
    onSuccess: () => {
      setSaved(true)
      setErrors({})
      setFormError('')
      onSaved()
    },
    onError: (err) => {
      const text = plainBody(err)
      const field = httpStatus(err) === 400 ? providerFieldOfError(text) : null
      if (field) setErrors({ [field]: text })
      else setFormError(getStayErrorMessage(err, 'Не удалось сохранить сведения об исполнителе.'))
    },
  })

  const status = f.status || null
  const needOgrn = status ? ogrnLengthFor(status) : null

  return (
    <SectionCard
      title="Об исполнителе"
      description="Эти сведения гость видит на странице дома и на странице своей брони. Публично показываются статус и ИНН; ФИО физического лица и самозанятого — только на странице брони самого гостя."
    >
      <form
        noValidate
        className="flex flex-col gap-4"
        onSubmit={(e) => {
          e.preventDefault()
          setFormError('')
          const v = validateProvider(f)
          setErrors(v)
          if (Object.keys(v).length === 0) save.mutate()
        }}
      >
        <SelectField
          label="Статус"
          value={f.status}
          onChange={(v) => set('status', v as ProviderForm['status'])}
          options={[{ value: '', label: 'Выберите…' }, ...PROVIDER_STATUSES]}
          error={errors.status}
        />
        <Input
          label={status === 'Organization' ? 'Наименование' : 'ФИО или наименование'}
          maxLength={300}
          value={f.name}
          error={errors.name}
          onChange={(e) => set('name', e.target.value)}
        />
        <div className="grid gap-4 sm:grid-cols-2">
          <Input
            label="ИНН"
            inputMode="numeric"
            maxLength={12}
            value={f.inn}
            error={errors.inn}
            onChange={(e) => set('inn', e.target.value)}
          />
          {needOgrn && (
            <Input
              label={status === 'IndividualEntrepreneur' ? 'ОГРНИП' : 'ОГРН'}
              inputMode="numeric"
              maxLength={15}
              value={f.ogrn}
              error={errors.ogrn}
              onChange={(e) => set('ogrn', e.target.value)}
            />
          )}
        </div>
        {status && <p className="-mt-2 text-xs text-muted">ИНН — {innLengthFor(status)} цифр{needOgrn ? `, ${status === 'IndividualEntrepreneur' ? 'ОГРНИП' : 'ОГРН'} — ${needOgrn}` : ''}.</p>}
        <Input
          label="Адрес для претензий"
          maxLength={500}
          value={f.claimsAddress}
          error={errors.claimsAddress}
          onChange={(e) => set('claimsAddress', e.target.value)}
        />
        {formError && <InlineError>{formError}</InlineError>}
        <div className="flex items-center gap-3">
          <Button type="submit" loading={save.isPending} className="min-h-[44px]">
            Сохранить сведения
          </Button>
          <SavedNote show={saved} />
        </div>
      </form>
    </SectionCard>
  )
}
