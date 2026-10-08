import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { InlineError } from '@/components/ui/InlineError'
import { Input } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { formatRub } from '@/utils/money'
import { staysHousesApi } from '../../api/staysHouses'
import type { HousePriceMode, PricePeriodDto, StaysConflictDto } from '../../types'
import { toPeriodInput, validatePeriod, validatePrice, type Errors, type PeriodField } from '../../utils/houseForms'
import { countUncoveredDays } from '../../utils/priceCalendar'
import { formatDateShort, formatDateNumeric, instantToZoned } from '../../utils/stayDates'
import { getStayErrorMessage, readConflict } from '../../utils/stayError'
import { NumberField, SavedNote, SectionCard } from '../cabinet/formParts'
import { ErrorState, LoadingList } from '../StatePanels'
import { houseKey } from './houseKey'
import { useHouseTab } from './houseContext'
import { PriceCalendar } from './PriceCalendar'

const MODES: { value: HousePriceMode; title: string; text: string }[] = [
  { value: 'Constant', title: 'Одна цена на все даты', text: 'Простой вариант: цена ночи не зависит от даты.' },
  { value: 'ByDates', title: 'Цены по периодам', text: 'Сезоны и праздники: у каждого периода своя цена; одна дата может иметь свою цену поверх периода. Ночь без цены не бронируется.' },
]

/** Prices of a house (`ManageHouses`, US-37-12/13): the mode, the constant price or the periods, and the calendar of prices by date. */
export function PricesTab() {
  const { companyId, house, setHouse, canManage, timeZoneId } = useHouseTab()
  const qc = useQueryClient()
  const [mode, setMode] = useState<HousePriceMode>(house.priceMode)
  const [constant, setConstant] = useState<number>(house.constantPriceRub ?? NaN)
  const [constError, setConstError] = useState('')
  const [formError, setFormError] = useState('')
  const [saved, setSaved] = useState(false)
  const [includePast, setIncludePast] = useState(false)
  const [edit, setEdit] = useState<PricePeriodDto | 'new' | null>(null)
  const [prefillDate, setPrefillDate] = useState<string | null>(null)
  const [toDelete, setToDelete] = useState<PricePeriodDto | null>(null)

  const periods = useQuery({
    queryKey: ['stays-price-periods', house.id, includePast],
    queryFn: () => staysHousesApi.pricePeriods(companyId, house.id, includePast),
    enabled: canManage,
  })
  const calendarPeriods = useQuery({
    queryKey: ['stays-price-periods', house.id, 'calendar'],
    queryFn: () => staysHousesApi.pricePeriods(companyId, house.id, false),
    enabled: canManage,
  })

  const refreshAfterPeriod = () => {
    void qc.invalidateQueries({ queryKey: ['stays-price-periods', house.id] })
    void qc.invalidateQueries({ queryKey: houseKey(companyId, house.id) })
    void qc.invalidateQueries({ queryKey: ['stays-houses', companyId] })
  }

  const savePricing = useMutation({
    mutationFn: () => staysHousesApi.updatePricing(companyId, house.id, { mode, constantPriceRub: mode === 'Constant' ? constant : (house.constantPriceRub ?? null) }),
    onSuccess: (h) => {
      setHouse(h)
      setSaved(true)
      setFormError('')
      refreshAfterPeriod()
    },
    onError: (err) => {
      const c = readConflict<StaysConflictDto>(err)
      setFormError(c?.message ?? getStayErrorMessage(err, 'Не удалось сохранить цены.'))
    },
  })

  const remove = useMutation({
    mutationFn: (p: PricePeriodDto) => staysHousesApi.deletePricePeriod(companyId, house.id, p.id),
    onSuccess: () => {
      setToDelete(null)
      refreshAfterPeriod()
    },
  })

  if (!canManage) return <p className="text-sm text-ink-soft">Цены дома меняет владелец.</p>

  const submitPricing = () => {
    if (mode === 'Constant') {
      const bad = validatePrice(constant)
      setConstError(bad ?? '')
      if (bad) return
    }
    setSaved(false)
    savePricing.mutate()
  }

  const today = instantToZoned(Date.now(), timeZoneId).date

  return (
    <div className="flex flex-col gap-6">
      <SectionCard title="Как задана цена" description="Цена — целые рубли за ночь. Действует для новых броней.">
        <fieldset className="flex flex-col gap-2.5">
          <legend className="sr-only">Режим цены</legend>
          {MODES.map((m) => (
            <label
              key={m.value}
              className={`flex min-h-[44px] cursor-pointer items-start gap-3 rounded-2xl border p-4 ${mode === m.value ? 'border-ink bg-cream-deep/50' : 'border-line hover:border-line-strong'}`}
            >
              <input
                type="radio"
                name="price-mode"
                className="mt-1 h-5 w-5 accent-gold"
                checked={mode === m.value}
                onChange={() => {
                  setMode(m.value)
                  setSaved(false)
                }}
              />
              <span>
                <span className="block text-sm font-semibold text-ink">{m.title}</span>
                <span className="mt-0.5 block text-xs text-ink-soft">{m.text}</span>
              </span>
            </label>
          ))}
        </fieldset>
        {mode === 'Constant' && (
          <NumberField label="Цена за ночь" suffix="₽" value={constant} onChange={(v) => { setConstant(v); setSaved(false) }} error={constError} min={1} max={1000000} />
        )}
        {formError && <InlineError>{formError}</InlineError>}
        <div className="flex items-center gap-3">
          <Button loading={savePricing.isPending} onClick={submitPricing} className="min-h-[44px]">
            Сохранить
          </Button>
          <SavedNote show={saved} />
        </div>
        {house.isPublished && mode === 'ByDates' && (
          <p className="text-xs text-muted">Опубликованный дом в режиме «по датам» нужен хотя бы один период, который ещё не закончился.</p>
        )}
      </SectionCard>

      {mode === 'ByDates' && house.priceMode === 'ByDates' && (
        <SectionCard title="Периоды" description="Однодневный период поверх длинного — допустим. Два периода не должны пересекаться.">
          {house.uncoveredDates.length > 0 && (
            <p role="status" className="rounded-2xl bg-warning-bg px-4 py-3 text-sm text-warning" data-testid="uncovered-warning">
              Без цены останутся {countUncoveredDays(house.uncoveredDates)} дн. в горизонте бронирования — эти ночи гости забронировать не смогут:{' '}
              {house.uncoveredDates.slice(0, 4).map((r) => (r.startDate === r.endDate ? formatDateNumeric(r.startDate) : `${formatDateNumeric(r.startDate)}–${formatDateNumeric(r.endDate)}`)).join(', ')}
              {house.uncoveredDates.length > 4 ? ' и другие' : ''}.
            </p>
          )}
          {periods.isLoading ? (
            <LoadingList rows={2} rowClass="h-14" />
          ) : periods.isError ? (
            <ErrorState message={getStayErrorMessage(periods.error, 'Не удалось загрузить периоды.')} onRetry={() => void periods.refetch()} />
          ) : (periods.data ?? []).length === 0 ? (
            <p className="text-sm text-ink-soft">Периодов пока нет. Добавьте первый — иначе ни одну ночь нельзя будет забронировать.</p>
          ) : (
            <ul className="flex flex-col gap-2">
              {periods.data!.map((p) => (
                <li key={p.id} className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-line px-4 py-3">
                  <div>
                    <p className="text-sm font-medium text-ink">
                      {p.startDate === p.endDate ? formatDateShort(p.startDate, 0) : `${formatDateShort(p.startDate, 0)} — ${formatDateShort(p.endDate, 0)}`}
                    </p>
                    <p className="text-xs text-ink-soft">{formatRub(p.priceRub)} за ночь</p>
                  </div>
                  <div className="flex gap-2">
                    <Button variant="secondary" size="sm" className="min-h-[44px]" onClick={() => setEdit(p)}>
                      Изменить
                    </Button>
                    <Button variant="danger" size="sm" className="min-h-[44px]" onClick={() => setToDelete(p)}>
                      Удалить
                    </Button>
                  </div>
                </li>
              ))}
            </ul>
          )}
          <label className="flex min-h-[44px] cursor-pointer items-center gap-3 text-sm text-ink-soft">
            <input type="checkbox" className="h-5 w-5 accent-gold" checked={includePast} onChange={(e) => setIncludePast(e.target.checked)} />
            Показать прошедшие периоды
          </label>
          <div>
            <Button className="min-h-[44px]" onClick={() => { setPrefillDate(null); setEdit('new') }}>
              Добавить период
            </Button>
          </div>
        </SectionCard>
      )}

      <SectionCard title="Цены по датам" description="Так видит цену гость: цена ночи, которая начинается в этот день.">
        <PriceCalendar
          today={today}
          house={{ mode: house.priceMode, constantPriceRub: house.constantPriceRub }}
          periods={calendarPeriods.data ?? []}
          uncovered={house.uncoveredDates}
          onPick={house.priceMode === 'ByDates' ? (d) => { setPrefillDate(d); setEdit('new') } : undefined}
        />
      </SectionCard>

      {edit && (
        <PeriodDialog
          companyId={companyId}
          houseId={house.id}
          period={edit === 'new' ? null : edit}
          prefillDate={prefillDate}
          onClose={() => setEdit(null)}
          onSaved={() => {
            setEdit(null)
            refreshAfterPeriod()
          }}
        />
      )}
      {toDelete && (
        <Modal title="Удалить период?" onClose={() => setToDelete(null)} dismissible={!remove.isPending}>
          <p className="text-sm text-ink-soft">
            {formatDateShort(toDelete.startDate, 0)} — {formatDateShort(toDelete.endDate, 0)}, {formatRub(toDelete.priceRub)}. Уже оформленные брони сохранят цену, которую видел гость.
          </p>
          {remove.isError && (
            <div className="mt-3">
              <InlineError>{getStayErrorMessage(remove.error, 'Не удалось удалить период.')}</InlineError>
            </div>
          )}
          <div className="mt-5 flex gap-3">
            <Button variant="secondary" className="min-h-[44px] flex-1" onClick={() => setToDelete(null)} disabled={remove.isPending}>
              Отмена
            </Button>
            <Button variant="danger" className="min-h-[44px] flex-1" loading={remove.isPending} onClick={() => remove.mutate(toDelete)}>
              Удалить
            </Button>
          </div>
        </Modal>
      )}
    </div>
  )
}

function PeriodDialog({
  companyId,
  houseId,
  period,
  prefillDate,
  onClose,
  onSaved,
}: {
  companyId: string
  houseId: string
  period: PricePeriodDto | null
  prefillDate: string | null
  onClose: () => void
  onSaved: () => void
}) {
  const [start, setStart] = useState(period?.startDate ?? prefillDate ?? '')
  const [end, setEnd] = useState(period?.endDate ?? prefillDate ?? '')
  const [price, setPrice] = useState<number>(period?.priceRub ?? NaN)
  const [errors, setErrors] = useState<Errors<PeriodField>>({})
  const [formError, setFormError] = useState('')

  const save = useMutation({
    mutationFn: () => {
      const input = toPeriodInput({ startDate: start, endDate: end, priceRub: price })
      return period ? staysHousesApi.updatePricePeriod(companyId, houseId, period.id, input) : staysHousesApi.createPricePeriod(companyId, houseId, input)
    },
    onSuccess: onSaved,
    onError: (err) => {
      const c = readConflict<StaysConflictDto>(err)
      // «Период пересекается с …» is the server's sentence with the conflicting period in it.
      setFormError(c?.message ?? getStayErrorMessage(err, 'Не удалось сохранить период.'))
    },
  })

  const submit = () => {
    const v = validatePeriod({ startDate: start, endDate: end, priceRub: price })
    setErrors(v)
    setFormError('')
    if (Object.keys(v).length === 0) save.mutate()
  }

  return (
    <Modal title={period ? 'Изменить период' : 'Новый период'} onClose={onClose} dismissible={!save.isPending}>
      <form
        noValidate
        className="flex flex-col gap-4"
        onSubmit={(e) => {
          e.preventDefault()
          submit()
        }}
      >
        <div className="grid grid-cols-2 gap-3">
          <Input label="С даты" type="date" value={start} error={errors.startDate} onChange={(e) => { setStart(e.target.value); if (!end || end < e.target.value) setEnd(e.target.value) }} />
          <Input label="По дату включительно" type="date" value={end} min={start || undefined} error={errors.endDate} onChange={(e) => setEnd(e.target.value)} />
        </div>
        <NumberField label="Цена за ночь" suffix="₽" value={price} onChange={setPrice} error={errors.priceRub} min={1} max={1000000} />
        <p className="text-xs text-muted">Один день — одна и та же дата в обоих полях.</p>
        {formError && <InlineError>{formError}</InlineError>}
        <div className="flex gap-3">
          <Button type="button" variant="secondary" className="min-h-[44px] flex-1" onClick={onClose} disabled={save.isPending}>
            Отмена
          </Button>
          <Button type="submit" className="min-h-[44px] flex-1" loading={save.isPending}>
            Сохранить
          </Button>
        </div>
      </form>
    </Modal>
  )
}
