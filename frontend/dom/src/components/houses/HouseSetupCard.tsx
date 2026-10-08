import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { InlineError } from '@/components/ui/InlineError'
import { Input } from '@/components/ui/Input'
import { staysHousesApi } from '../../api/staysHouses'
import type { HouseSetupInput } from '../../types'
import { setupFieldOfError, validateSetup, type Errors, type SetupField } from '../../utils/houseForms'
import { normalizeSlugInput } from '../../utils/slug'
import { getStayErrorMessage, httpStatus, plainBody, readConflict } from '../../utils/stayError'
import type { StaysConflictDto } from '../../types'
import { NumberField, SavedNote, SectionCard, SwitchRow } from '../cabinet/formParts'
import { useHouseTab } from './houseContext'

/** Name, address of the page, capacity, extra beds, dogs, cot (`ManageHouses`). A manager sees the facts, but cannot change them. */
export function HouseSetupCard() {
  const { companyId, house, setHouse, canManage } = useHouseTab()
  const [s, setS] = useState<HouseSetupInput>({
    name: house.name,
    slug: house.slug,
    capacity: house.capacity,
    extraBedsEnabled: house.extraBeds.enabled,
    extraBedsMax: house.extraBeds.max,
    extraBedPriceRub: house.extraBeds.priceRub,
    dogsForbidden: house.dogsForbidden,
    hasCot: house.hasCot,
  })
  const [errors, setErrors] = useState<Errors<SetupField>>({})
  const [formError, setFormError] = useState('')
  const [saved, setSaved] = useState(false)

  const set = <K extends keyof HouseSetupInput>(k: K, v: HouseSetupInput[K]) => {
    setS((x) => ({ ...x, [k]: v }))
    setSaved(false)
  }

  const save = useMutation({
    mutationFn: () => staysHousesApi.updateSetup(companyId, house.id, { ...s, name: s.name.trim(), slug: s.slug.trim() }),
    onSuccess: (h) => {
      setHouse(h)
      setSaved(true)
      setErrors({})
      setFormError('')
    },
    onError: (err) => {
      const conflict = readConflict<StaysConflictDto>(err)
      if (conflict?.code === 'SlugTaken') return setErrors({ slug: conflict.message })
      const text = plainBody(err)
      const field = httpStatus(err) === 400 ? setupFieldOfError(text) : null
      if (field) setErrors({ [field]: text })
      else setFormError(conflict?.message ?? getStayErrorMessage(err, 'Не удалось сохранить дом.'))
    },
  })

  return (
    <SectionCard title="Основное" description={canManage ? undefined : 'Эти сведения меняет владелец.'}>
      <form
        noValidate
        className="flex flex-col gap-5"
        onSubmit={(e) => {
          e.preventDefault()
          if (!canManage) return
          const v = validateSetup(s)
          setErrors(v)
          if (Object.keys(v).length === 0) save.mutate()
        }}
      >
        <fieldset disabled={!canManage} className="flex flex-col gap-5 disabled:opacity-80">
          <Input label="Название дома *" maxLength={100} value={s.name} error={errors.name} onChange={(e) => set('name', e.target.value)} />
          <div>
            <Input
              label="Адрес страницы дома"
              maxLength={50}
              autoCapitalize="none"
              spellCheck={false}
              value={s.slug}
              error={errors.slug}
              onChange={(e) => set('slug', normalizeSlugInput(e.target.value))}
            />
            <p className="mt-1.5 text-xs text-muted">
              Если сменить адрес, старые ссылки и QR-код дома перестанут открываться — переадресации нет.
            </p>
          </div>
          <NumberField label="Вместимость" suffix="гостей" value={s.capacity} onChange={(v) => set('capacity', v)} error={errors.capacity} min={1} max={50} />
          <SwitchRow
            label="Доп. места"
            hint="Гости сверх вместимости — на дополнительных местах за отдельную плату"
            checked={s.extraBedsEnabled}
            onChange={(v) => set('extraBedsEnabled', v)}
            disabled={!canManage}
          />
          {s.extraBedsEnabled && (
            <div className="grid gap-4 sm:grid-cols-2">
              <NumberField label="Сколько доп. мест" value={s.extraBedsMax} onChange={(v) => set('extraBedsMax', v)} error={errors.extraBedsMax} min={1} max={10} />
              <NumberField label="Цена одного места" suffix="₽ в ночь" value={s.extraBedPriceRub} onChange={(v) => set('extraBedPriceRub', v)} error={errors.extraBedPriceRub} min={0} max={100000} />
            </div>
          )}
          <SwitchRow label="Собаки нельзя" hint="Гость не сможет указать собак в брони" checked={s.dogsForbidden} onChange={(v) => set('dogsForbidden', v)} disabled={!canManage} />
          <SwitchRow label="Есть детская кроватка" hint="Цена кроватки задаётся в настройках компании" checked={s.hasCot} onChange={(v) => set('hasCot', v)} disabled={!canManage} />
        </fieldset>
        {formError && <InlineError>{formError}</InlineError>}
        {canManage && (
          <div className="flex items-center gap-3">
            <Button type="submit" loading={save.isPending} className="min-h-[44px]">
              Сохранить
            </Button>
            <SavedNote show={saved} />
          </div>
        )}
      </form>
    </SectionCard>
  )
}
