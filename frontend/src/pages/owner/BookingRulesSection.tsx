import { useEffect, useId, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { companiesApi } from '../../api/companies'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { InlineError } from '../../components/ui/InlineError'
import { Input } from '../../components/ui/Input'
import { CARD_TITLE_CLASS } from '../../components/company/cardTitle'
import { CANCEL_WINDOW_FIELD_CAPTION } from '../../legal/staffNotices'
import { parseBookingHorizonInput } from '../../utils/bookingHorizon'
import { getCompanyManageErrorMessage } from '../../utils/companyManageError'
import { httpStatusOf, plainErrorBody } from '../../utils/httpError'
import { mapLinksFieldError } from '../../utils/mapLinksFieldError'
import type { Company } from '../../types'
import type { components as C32 } from '../../types/api-cycle32.generated'

export const RULES_NOTE =
  'Изменения действуют сразу — и для новых записей, и для переноса или отмены уже созданных. Предоплата запрашивается только в новых записях.'

/**
 * ARCHITECTURE_CYCLE32.md §32.8 — «Правила записи»: its own card, its own «Сохранить правила», a body with the rule
 * fields only. State is local and read once (mount with `key={company.id}`), so a re-read of `['my-companies']` or a
 * save of the profile card never resets what is typed here (cycle-13 lesson).
 */
export function BookingRulesSection({ company }: { company: Company }) {
  const qc = useQueryClient()
  const ids = useId()
  const [horizon, setHorizon] = useState(() => (company.bookingHorizonDays ? String(company.bookingHorizonDays) : ''))
  const [windowHours, setWindowHours] = useState(() =>
    company.clientRescheduleMinHours != null ? String(company.clientRescheduleMinHours) : '',
  )
  const [allowSelfBooking, setAllowSelfBooking] = useState(() => company.allowSelfBooking)
  const [requirePrepayment, setRequirePrepayment] = useState(() => company.requirePrepayment ?? false)
  const [errors, setErrors] = useState<{ horizon?: string; window?: string }>({})
  const [formError, setFormError] = useState('')
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)
  useEffect(
    () => () => {
      if (timer.current) clearTimeout(timer.current)
    },
    [],
  )

  const bookingLocked = !company.planAllowsOnlineBooking
  const paymentLocked = !company.planAllowsOnlinePayment
  const captionId = `${ids}-window-caption`

  async function save() {
    setSaved(false)
    setFormError('')
    const parsed = parseBookingHorizonInput(horizon)
    if (parsed.error) {
      setErrors({ horizon: parsed.error })
      return
    }
    setErrors({})
    const rawHours = windowHours.trim()
    const body = {
      bookingHorizonDays: parsed.value,
      ...(rawHours !== '' ? { clientRescheduleMinHours: parseInt(rawHours, 10) } : {}),
      ...(!bookingLocked ? { allowSelfBooking } : {}),
      ...(!paymentLocked ? { requirePrepayment } : {}),
    } satisfies C32['schemas']['BookingRulesUpdateInput']

    setSaving(true)
    try {
      await companiesApi.update(company.id, body)
    } catch (err) {
      setSaving(false)
      const raw = httpStatusOf(err) === 400 ? plainErrorBody(err) : ''
      if (raw && mapLinksFieldError(raw) === 'clientRescheduleMinHours') setErrors({ window: raw })
      else if (raw.startsWith('Горизонт записи')) setErrors({ horizon: raw })
      else setFormError(getCompanyManageErrorMessage(err, 'Не удалось сохранить правила записи.'))
      return
    }
    setSaving(false)
    setSaved(true)
    if (timer.current) clearTimeout(timer.current)
    timer.current = setTimeout(() => setSaved(false), 2500)
    void qc.invalidateQueries({ queryKey: ['my-companies'] })
    void qc.invalidateQueries({ queryKey: ['company'] })
  }

  return (
    <Card className="p-6">
      <h2 className={`${CARD_TITLE_CLASS} mb-1`}>Правила записи</h2>
      <p className="text-sm text-ink-soft mb-5">{RULES_NOTE}</p>
      <form
        noValidate
        className="flex flex-col gap-5"
        onSubmit={(e) => {
          e.preventDefault()
          if (!saving) void save()
        }}
      >
        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-1">
            <Input
              label="На сколько дней вперёд клиент может записаться"
              type="number"
              min={0}
              max={365}
              placeholder="90"
              value={horizon}
              error={errors.horizon}
              aria-describedby={`${ids}-horizon-hint`}
              onChange={(e) => {
                setHorizon(e.target.value)
                setSaved(false)
              }}
            />
            <p id={`${ids}-horizon-hint`} className="text-xs text-muted">
              Пусто или 0 — 90 дней по умолчанию
            </p>
          </div>
          <div className="flex flex-col gap-1">
            <Input
              label="За сколько часов клиент может перенести или отменить запись"
              type="number"
              min={0}
              max={168}
              placeholder="2"
              value={windowHours}
              error={errors.window}
              aria-describedby={captionId}
              onChange={(e) => {
                setWindowHours(e.target.value)
                setSaved(false)
              }}
            />
            {/* Т20-05 п. 3 — the lawyer's caption, verbatim (`staffNotices.ts`); the error id is added by Input. */}
            <p id={captionId} className="text-xs text-muted">
              {CANCEL_WINDOW_FIELD_CAPTION}
            </p>
          </div>
          <div>
            <label className="flex items-center gap-3 cursor-pointer has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-50">
              <input
                type="checkbox"
                className="w-4 h-4 rounded accent-gold"
                checked={allowSelfBooking}
                disabled={bookingLocked}
                onChange={(e) => {
                  setAllowSelfBooking(e.target.checked)
                  setSaved(false)
                }}
              />
              <span className="text-sm text-ink-soft">Разрешить клиентам записываться самостоятельно</span>
            </label>
            {bookingLocked && (
              <p className="text-xs text-warning mt-1 ml-7">
                Онлайн-запись не входит в текущий тариф — повысьте тариф, чтобы включить
              </p>
            )}
          </div>
          <div>
            <label className="flex items-center gap-3 cursor-pointer has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-50">
              <input
                type="checkbox"
                className="w-4 h-4 rounded accent-gold"
                checked={requirePrepayment}
                disabled={paymentLocked}
                onChange={(e) => {
                  setRequirePrepayment(e.target.checked)
                  setSaved(false)
                }}
              />
              <span className="text-sm text-ink-soft">Требовать предоплату при онлайн-записи</span>
            </label>
            {paymentLocked && (
              <p className="text-xs text-warning mt-1 ml-7">
                Онлайн-оплата не входит в текущий тариф — повысьте тариф, чтобы включить
              </p>
            )}
          </div>
        </div>
        {formError && <InlineError>{formError}</InlineError>}
        <div className="flex items-center gap-3">
          <Button type="submit" loading={saving}>
            Сохранить правила
          </Button>
          {saved && (
            <span role="status" className="text-sm text-success font-medium">
              Сохранено
            </span>
          )}
        </div>
      </form>
    </Card>
  )
}
