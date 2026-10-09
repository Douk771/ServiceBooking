import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { InlineError } from '@/components/ui/InlineError'
import { NumberField, SavedNote, SectionCard, SwitchRow } from '@/components/slots/ui/formParts'
import { getStayErrorMessage, httpStatus, plainBody } from '@/utils/slots/slotError'
import { bathsCabinetApi } from '../../api/bathsCabinet'
import {
  HOLD_MAX,
  HOLD_MIN,
  HORIZON_MAX,
  HORIZON_MIN,
  REMINDER_HOURS_MAX,
  REMINDER_HOURS_MIN,
  formToSettingsInput,
  settingsFieldOfError,
  settingsToForm,
  validateSettings,
  type SettingsErrors,
  type SettingsForm,
} from '../../cabinet/settingsForm'
import type { BathsCompanyManageDto, BathsSettingsDto } from '../../cabinet/types'

/**
 * «Бронирование и напоминание»: horizon, time to pay, the bather's access to the guest's comment (ЮР-5), the catalogue and the reminder before
 * a session. One `PUT …/settings` with the whole object. A change applies to NEW bookings only. While the reminder is off the field of hours is not
 * shown: the server keeps no number then, so there is nothing true to show.
 */
export function BookingRulesCard({ company, settings, onSaved }: { company: BathsCompanyManageDto; settings: BathsSettingsDto; onSaved: () => void }) {
  const [s, setS] = useState<SettingsForm>(() => settingsToForm(settings))
  const [errors, setErrors] = useState<SettingsErrors>({})
  const [formError, setFormError] = useState('')
  const [saved, setSaved] = useState(false)

  const set = <K extends keyof SettingsForm>(key: K, value: SettingsForm[K]) => {
    setS((v) => ({ ...v, [key]: value }))
    setSaved(false)
  }

  const save = useMutation({
    mutationFn: () => bathsCabinetApi.updateSettings(company.id, formToSettingsInput(s)),
    onSuccess: () => {
      setSaved(true)
      setErrors({})
      setFormError('')
      onSaved()
    },
    onError: (err) => {
      const text = plainBody(err)
      const field = httpStatus(err) === 400 ? settingsFieldOfError(text) : null
      // The hours field exists only while the reminder is on; an error about it must not vanish into a hidden field.
      if (field && !(field === 'sessionReminderHours' && !s.sessionReminderEnabled)) setErrors({ [field]: text })
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
      aria-label="Бронирование и напоминание"
      onSubmit={(e) => {
        e.preventDefault()
        submit()
      }}
      className="flex flex-col gap-5"
    >
      <SectionCard id="booking-rules" title="Бронирование" description="Правила применяются к новым броням; уже оформленные остаются такими, как их видел гость.">
        <div className="grid gap-4 sm:grid-cols-2">
          <NumberField
            label="Горизонт бронирования"
            suffix="дней"
            hint="На сколько дней вперёд открыты даты"
            value={s.horizonDays}
            onChange={(v) => set('horizonDays', v)}
            error={errors.horizonDays}
            min={HORIZON_MIN}
            max={HORIZON_MAX}
          />
          <NumberField
            label="Время на оплату"
            suffix="минут"
            hint="Столько минут сеанс держится за гостем, пока он платит предоплату"
            value={s.holdMinutes}
            onChange={(v) => set('holdMinutes', v)}
            error={errors.holdMinutes}
            min={HOLD_MIN}
            max={HOLD_MAX}
          />
        </div>
        <SwitchRow
          label="Банщик видит комментарий гостя"
          hint="Выключено по умолчанию: комментарий может содержать лишнее о госте. Включайте, только если он нужен для подготовки сеанса"
          checked={s.housekeeperSeesGuestComment}
          onChange={(v) => set('housekeeperSeesGuestComment', v)}
        />
        <SwitchRow
          label="Показывать комплекс в каталоге"
          hint="Страница комплекса по ссылке работает и при выключенном каталоге"
          checked={s.showInCatalog}
          onChange={(v) => set('showInCatalog', v)}
        />
      </SectionCard>

      <SectionCard
        id="session-reminder"
        title="Напоминание гостю"
        description="Один раз перед сеансом гостю уходит короткое напоминание фиксированного текста, без рекламы. Оно не приходит раньше 08:00 и позже 22:00 по времени комплекса."
      >
        <SwitchRow
          label="Напоминать о сеансе"
          hint="Напоминание видно на странице брони. В мессенджер оно уходит только гостю, который отметил это при брони, push — если он подписан"
          checked={s.sessionReminderEnabled}
          onChange={(v) => set('sessionReminderEnabled', v)}
        />
        {s.sessionReminderEnabled && (
          <NumberField
            label="За сколько часов до начала"
            suffix="ч"
            hint={`От ${REMINDER_HOURS_MIN} до ${REMINDER_HOURS_MAX}`}
            value={s.sessionReminderHours}
            onChange={(v) => set('sessionReminderHours', v)}
            error={errors.sessionReminderHours}
            min={REMINDER_HOURS_MIN}
            max={REMINDER_HOURS_MAX}
          />
        )}
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
