import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { InlineError } from '@/components/ui/InlineError'
import { staysCompaniesApi } from '../../api/staysCompanies'
import type { StaysCompanyManageWithServices, StaysSettingsWithServices } from '../../types'
import {
  CANCELLATION_TEMPLATES,
  HALF_HOURS,
  PREPAY_HINT,
  PREPAY_ZERO_WARNING,
  settingsFieldOfError,
  validateSettings,
  type SettingsErrors,
} from '../../utils/settingsForm'
import { getStayErrorMessage, httpStatus, plainBody } from '../../utils/stayError'
import { StayNotice } from '../StayNotice'
import { NumberField, SavedNote, SectionCard, SelectField, SwitchRow, TextArea } from './formParts'

const TIME_OPTIONS = HALF_HOURS.map((t) => ({ value: t, label: t }))

/**
 * Rules of the company (§37.2.2 groups: stay, prepayment, cancellation, options, check-in information, catalog). One `PUT …/settings`
 * with the full object. A change applies to NEW bookings only — the snapshot in each booking keeps what the guest saw.
 */
export function RulesCard({ company, onSaved }: { company: StaysCompanyManageWithServices; onSaved: () => void }) {
  const [s, setS] = useState<StaysSettingsWithServices>(() => company.settings!)
  const [errors, setErrors] = useState<SettingsErrors>({})
  const [formError, setFormError] = useState('')
  const [saved, setSaved] = useState(false)

  const set = <K extends keyof StaysSettingsWithServices>(key: K, value: StaysSettingsWithServices[K]) => {
    setS((v) => ({ ...v, [key]: value }))
    setSaved(false)
  }

  const save = useMutation({
    mutationFn: () => staysCompaniesApi.updateSettings(company.id, { ...s, checkInInfoText: (s.checkInInfoText ?? '').trim() || null }),
    onSuccess: () => {
      setSaved(true)
      setErrors({})
      setFormError('')
      onSaved()
    },
    onError: (err) => {
      const text = plainBody(err)
      const field = httpStatus(err) === 400 ? settingsFieldOfError(text) : null
      if (field) setErrors({ [field]: text })
      else setFormError(getStayErrorMessage(err, 'Не удалось сохранить настройки.'))
    },
  })

  const submit = () => {
    setFormError('')
    const e = validateSettings(s)
    setErrors(e)
    if (Object.keys(e).length === 0) save.mutate()
  }

  return (
    <form
      noValidate
      onSubmit={(e) => {
        e.preventDefault()
        submit()
      }}
      className="flex flex-col gap-5"
    >
      <SectionCard title="Проживание" description="Правила применяются к новым броням; уже оформленные остаются как гость их видел.">
        <div className="grid gap-4 sm:grid-cols-2">
          <SelectField label="Время заезда" value={s.checkInTime} options={TIME_OPTIONS} onChange={(v) => set('checkInTime', v)} error={errors.checkInTime} />
          <SelectField label="Время выезда" value={s.checkOutTime} options={TIME_OPTIONS} onChange={(v) => set('checkOutTime', v)} error={errors.checkOutTime} />
          <NumberField label="Минимум ночей" value={s.minNights} onChange={(v) => set('minNights', v)} error={errors.minNights} min={1} max={30} />
          <NumberField label="Максимум ночей" value={s.maxNights} onChange={(v) => set('maxNights', v)} error={errors.maxNights} min={1} max={90} />
          <NumberField
            label="Горизонт бронирования"
            suffix="дней"
            hint="На сколько дней вперёд открыты даты"
            value={s.horizonDays}
            onChange={(v) => set('horizonDays', v)}
            error={errors.horizonDays}
            min={30}
            max={730}
          />
        </div>
        <SwitchRow
          label="Закрывать разрывы"
          hint="Бронь короче минимума допустима, если она точно заполняет промежуток между двумя бронями"
          checked={s.allowGapFill}
          onChange={(v) => set('allowGapFill', v)}
        />
        <SwitchRow
          label="Заезд в день бронирования"
          hint="Гость может выбрать сегодняшнюю дату заезда"
          checked={s.allowSameDayCheckIn}
          onChange={(v) => set('allowSameDayCheckIn', v)}
        />
        <div className="grid gap-4 sm:grid-cols-2">
          <NumberField label="Доплата за собаку" suffix="₽ в ночь" value={s.dogFeeRub} onChange={(v) => set('dogFeeRub', v)} error={errors.dogFeeRub} min={0} max={100000} />
          <NumberField label="Детская кроватка" suffix="₽ в ночь" hint="0 — бесплатно" value={s.cotFeeRub} onChange={(v) => set('cotFeeRub', v)} error={errors.cotFeeRub} min={0} max={100000} />
        </div>
      </SectionCard>

      <SectionCard title="Предоплата" description="Деньги гость переводит напрямую вам по реквизитам; сервис их не принимает.">
        <div className="grid gap-4 sm:grid-cols-2">
          <NumberField label="Размер предоплаты" suffix="%" value={s.prepayPercent} onChange={(v) => set('prepayPercent', v)} error={errors.prepayPercent} min={0} max={100} />
          <NumberField
            label="Время на оплату"
            suffix="минут"
            hint="Столько даты держатся за гостем"
            value={s.holdMinutes}
            onChange={(v) => set('holdMinutes', v)}
            error={errors.holdMinutes}
            min={10}
            max={180}
          />
        </div>
        {s.prepayPercent === 0 && (
          <p role="alert" className="rounded-2xl bg-warning-bg px-4 py-3 text-sm text-warning" data-testid="prepay-zero-warning">
            {PREPAY_ZERO_WARNING}
          </p>
        )}
        {s.prepayPercent > 0 && <p className="text-xs text-muted">{PREPAY_HINT}</p>}
      </SectionCard>

      <SectionCard title="Отмена гостем" description="Выберите один из трёх шаблонов. Гость увидит сумму «к возврату не меньше X ₽» — её считает сервис по этому шаблону.">
        <fieldset className="flex flex-col gap-2.5">
          <legend className="sr-only">Шаблон отмены</legend>
          {CANCELLATION_TEMPLATES.map((t) => (
            <label
              key={t.value}
              className={`flex min-h-[44px] cursor-pointer items-start gap-3 rounded-2xl border p-4 transition-colors ${
                s.cancellationPolicy === t.value ? 'border-ink bg-cream-deep/50' : 'border-line hover:border-line-strong'
              }`}
            >
              <input
                type="radio"
                name="cancellation-policy"
                className="mt-1 h-5 w-5 accent-gold"
                checked={s.cancellationPolicy === t.value}
                onChange={() => set('cancellationPolicy', t.value)}
              />
              <span>
                <span className="block text-sm font-semibold text-ink">{t.title}</span>
                <span className="mt-0.5 block text-xs leading-relaxed text-ink-soft">{t.text}</span>
              </span>
            </label>
          ))}
        </fieldset>
        <p className="text-xs text-muted">
          Если гость отменяет, пока бронь не оплачена, — без последствий. Если бронь отменяете вы — предоплата возвращается полностью.
        </p>
      </SectionCard>

      <SectionCard title="Информация к заселению" description="Коды замков, как найти ключ. Гость увидит её на странице брони в день заезда.">
        <SelectField label="Когда открывать" value={s.checkInInfoSendTime} options={TIME_OPTIONS} onChange={(v) => set('checkInInfoSendTime', v)} error={errors.checkInInfoSendTime} hint="В день заезда, по времени Шерегеша" />
        <TextArea
          label="Общий текст к заселению"
          rows={4}
          maxLength={2000}
          value={s.checkInInfoText ?? ''}
          onChange={(v) => set('checkInInfoText', v)}
          error={errors.checkInInfoText}
          hint={<StayNotice textKey="StayMigrationOwnerNotice" />}
        />
        <div>
          <SwitchRow label="Отправлять текст целиком" hint="Выключено: в мессенджер уходит только ссылка на бронь" checked={s.checkInInfoSendFullText} onChange={(v) => set('checkInInfoSendFullText', v)} />
          <StayNotice textKey="StayCheckInInfoOwnerNotice" className="mt-2" />
        </div>
        <SwitchRow label="Напоминание накануне заезда" hint="Время и текст — в блоке «Напоминание о заезде» ниже" checked={s.arrivalReminderEnabled} onChange={(v) => set('arrivalReminderEnabled', v)} />
      </SectionCard>

      <SectionCard title="Услуги" description="Баня, чан и другие услуги на время. К брони дома их можно добавлять всегда; заказ без проживания — только если вы его разрешите.">
        <SwitchRow
          label="Принимать заказы услуг без проживания"
          hint="Гость сможет заказать услугу отдельно, без брони дома. Для этого нужны полные сведения об исполнителе"
          checked={s.acceptServiceOrdersWithoutStay === true}
          onChange={(v) => set('acceptServiceOrdersWithoutStay', v)}
        />
      </SectionCard>

      <SectionCard title="Сотрудники и каталог">
        <SwitchRow
          label="Горничная видит комментарий гостя"
          hint="Выключено по умолчанию: комментарий может содержать лишнее о госте. Включайте, только если он нужен для уборки и встречи"
          checked={s.housekeeperSeesGuestComment}
          onChange={(v) => set('housekeeperSeesGuestComment', v)}
        />
        <SwitchRow label="Показывать компанию в каталоге" hint="Страница компании по ссылке работает и при выключенном каталоге" checked={s.showInCatalog} onChange={(v) => set('showInCatalog', v)} />
      </SectionCard>

      {formError && <InlineError>{formError}</InlineError>}
      <div className="flex items-center gap-3">
        <Button type="submit" size="lg" loading={save.isPending} className="min-h-[44px]">
          Сохранить настройки
        </Button>
        <SavedNote show={saved} />
      </div>
    </form>
  )
}
