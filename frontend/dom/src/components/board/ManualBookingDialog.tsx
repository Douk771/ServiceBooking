import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { InlineError } from '@/components/ui/InlineError'
import { Input } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { staysBoardApi } from '../../api/staysBoard'
import type { BoardHouseDto, StayRefusalDto } from '../../types'
import { MANUAL_TOTAL_MAX, manualFieldOfError, toManualInput, toManualQuote, validateManual, type ManualField, type ManualForm } from '../../utils/manualBooking'
import { addDays } from '../../utils/stayDates'
import { getStayErrorMessage, httpStatus, plainBody, readConflict } from '../../utils/stayError'
import { PriceBreakdown } from '../PriceBreakdown'
import { Skeleton } from '../StatePanels'
import { Stepper } from '../Stepper'

/**
 * «Новая бронь» by staff (P1, `ManageBookings`): calls and acquaintances. Confirmed at once, no prepayment. The price is the server's
 * `quote` (minimum/maximum/horizon do not apply); the owner may replace the total. Dates taken by anyone are refused by the server.
 */
export function ManualBookingDialog({
  companyId,
  houses,
  initialHouseId,
  initialCheckIn,
  onClose,
  onCreated,
}: {
  companyId: string
  houses: BoardHouseDto[]
  initialHouseId?: string
  initialCheckIn?: string
  onClose: () => void
  onCreated: () => void
}) {
  const navigate = useNavigate()
  const choices = houses.filter((h) => !h.isArchived)
  const [f, setF] = useState<ManualForm>({
    houseId: initialHouseId ?? choices[0]?.id ?? '',
    checkIn: initialCheckIn ?? '',
    checkOut: initialCheckIn ? addDays(initialCheckIn, 1) : '',
    adults: 2,
    children: 0,
    dogs: 0,
    needCot: false,
    guestName: '',
    guestPhone: '',
    notifyGuest: false,
    totalOverrideRub: NaN,
    comment: '',
  })
  const [errors, setErrors] = useState<Partial<Record<ManualField, string>>>({})
  const [formError, setFormError] = useState('')

  const set = <K extends keyof ManualForm>(k: K, v: ManualForm[K]) => setF((x) => ({ ...x, [k]: v }))
  const ready = !!f.houseId && !!f.checkIn && !!f.checkOut && f.checkOut > f.checkIn

  const quote = useQuery({
    queryKey: ['stays-manual-quote', companyId, toManualQuote(f)],
    queryFn: () => staysBoardApi.manualQuote(companyId, toManualQuote(f)),
    enabled: ready,
    retry: false,
  })
  const q = quote.data

  const create = useMutation({
    mutationFn: () => staysBoardApi.createManual(companyId, toManualInput(f)),
    onSuccess: (card) => {
      onCreated()
      onClose()
      navigate(`/cabinet/${companyId}/bookings/${card.id}`)
    },
    onError: (err) => {
      const refusal = readConflict<StayRefusalDto>(err)
      if (refusal) return setFormError(refusal.message)
      const text = plainBody(err)
      const field = httpStatus(err) === 400 ? manualFieldOfError(text) : null
      if (field) return setErrors({ [field]: text })
      setFormError(getStayErrorMessage(err, 'Не удалось создать бронь.'))
    },
  })

  const submit = () => {
    setFormError('')
    const v = validateManual(f)
    setErrors(v)
    if (Object.keys(v).length === 0) create.mutate()
  }

  return (
    <Modal title="Новая бронь" onClose={onClose} dismissible={!create.isPending}>
      <form
        noValidate
        className="flex flex-col gap-4"
        onSubmit={(e) => {
          e.preventDefault()
          submit()
        }}
      >
        <div className="flex flex-col gap-1.5">
          <label htmlFor="mb-house" className="text-[13px] font-medium text-[#4A4038]">
            Дом
          </label>
          <select id="mb-house" value={f.houseId} onChange={(e) => set('houseId', e.target.value)} className="min-h-[44px] rounded-xl border border-line bg-white px-3 text-sm text-ink">
            {choices.map((h) => (
              <option key={h.id} value={h.id}>
                {h.name}
              </option>
            ))}
          </select>
          {errors.houseId && <p className="text-xs text-danger">{errors.houseId}</p>}
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Input label="Заезд" type="date" value={f.checkIn} onChange={(e) => { set('checkIn', e.target.value); if (!f.checkOut || f.checkOut <= e.target.value) set('checkOut', e.target.value ? addDays(e.target.value, 1) : '') }} />
          <Input label="Выезд" type="date" value={f.checkOut} min={f.checkIn ? addDays(f.checkIn, 1) : undefined} onChange={(e) => set('checkOut', e.target.value)} />
        </div>
        {errors.dates && <p role="alert" className="-mt-2 text-xs text-danger">{errors.dates}</p>}

        <div>
          <Stepper label="Взрослые" value={f.adults} min={1} max={30} onChange={(v) => set('adults', v)} />
          <Stepper label="Дети" value={f.children} min={0} max={30} onChange={(v) => set('children', v)} />
          <Stepper label="Собаки" value={f.dogs} min={0} max={20} onChange={(v) => set('dogs', v)} />
          <label className="flex min-h-[44px] cursor-pointer items-center gap-3 text-sm text-ink">
            <input type="checkbox" className="h-5 w-5 accent-gold" checked={f.needCot} onChange={(e) => set('needCot', e.target.checked)} />
            Нужна детская кроватка
          </label>
        </div>

        {ready && (
          <div aria-live="polite" data-testid="manual-quote">
            {quote.isLoading ? (
              <Skeleton className="h-28" />
            ) : quote.isError ? (
              <p role="alert" className="text-sm text-danger">
                {getStayErrorMessage(quote.error, 'Не удалось рассчитать стоимость.')}
              </p>
            ) : q && !q.ok ? (
              <ul role="alert" className="rounded-xl bg-danger-bg px-3 py-2 text-sm text-danger">
                {q.problems.map((p) => (
                  <li key={p.code}>{p.message}</li>
                ))}
              </ul>
            ) : q ? (
              <PriceBreakdown lines={q.lines} totalRub={q.totalRub} prepayPercent={0} prepayRub={0} dueAtCheckInRub={q.totalRub} />
            ) : null}
          </div>
        )}

        <Input label="Имя гостя *" maxLength={100} value={f.guestName} error={errors.guestName} onChange={(e) => set('guestName', e.target.value)} />
        <PhoneInput label="Телефон гостя" value={f.guestPhone} error={errors.guestPhone} onChange={(v) => set('guestPhone', v)} />
        <label className="flex min-h-[44px] cursor-pointer items-start gap-3 text-sm text-ink">
          <input type="checkbox" className="mt-0.5 h-5 w-5 accent-gold" checked={f.notifyGuest} onChange={(e) => set('notifyGuest', e.target.checked)} />
          <span>
            Отправить гостю ссылку на бронь
            <span className="block text-xs text-muted">Нужен номер телефона; сообщение уйдёт по подключённому каналу.</span>
          </span>
        </label>
        <Input
          label="Свой итог, ₽ (необязательно)"
          type="number"
          inputMode="numeric"
          min={0}
          max={MANUAL_TOTAL_MAX}
          placeholder="по расчёту"
          value={Number.isNaN(f.totalOverrideRub) ? '' : String(f.totalOverrideRub)}
          error={errors.totalOverrideRub}
          onChange={(e) => set('totalOverrideRub', e.target.value === '' ? NaN : Number(e.target.value))}
        />
        <Input label="Комментарий" maxLength={500} value={f.comment} error={errors.comment} onChange={(e) => set('comment', e.target.value)} />

        {formError && <InlineError>{formError}</InlineError>}
        <div className="flex gap-3">
          <Button type="button" variant="secondary" className="min-h-[44px] flex-1" onClick={onClose} disabled={create.isPending}>
            Отмена
          </Button>
          <Button type="submit" className="min-h-[44px] flex-1" loading={create.isPending}>
            Создать бронь
          </Button>
        </div>
      </form>
    </Modal>
  )
}
