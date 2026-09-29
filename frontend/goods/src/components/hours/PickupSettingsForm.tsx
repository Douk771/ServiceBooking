import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { scheduleApi } from '../../api/schedule'
import { InlineError } from '../StatePanels'
import { getGoodsErrorMessage } from '../../utils/orderError'
import type { PickupSettingsDto } from '../../types'

const FIELD = 'rounded-xl border border-line px-4 py-3 text-sm bg-white text-ink outline-none focus:border-gold min-h-[44px]'

/** US-24-05 — how buyers choose the time: «как можно скорее», «ко времени», slot step, preorder horizon, preparation time. */
export function PickupSettingsForm({ shopId, initial, onSaved }: { shopId: string; initial: PickupSettingsDto; onSaved: () => void }) {
  const [v, setV] = useState({ ...initial, preorderDays: String(initial.preorderDays), minPrepMinutes: String(initial.minPrepMinutes) })
  const [saved, setSaved] = useState(false)
  const [localError, setLocalError] = useState<string | null>(null)
  const set = <K extends keyof typeof v>(k: K, value: (typeof v)[K]) => {
    setV((p) => ({ ...p, [k]: value }))
    setSaved(false)
    setLocalError(null)
  }
  const save = useMutation({
    mutationFn: () =>
      scheduleApi.putPickupSettings(shopId, {
        asapEnabled: v.asapEnabled,
        scheduledEnabled: v.scheduledEnabled,
        slotStepMinutes: v.slotStepMinutes,
        preorderDays: Number(v.preorderDays),
        minPrepMinutes: Number(v.minPrepMinutes),
      }),
    onSuccess: () => {
      setSaved(true)
      onSaved()
    },
  })

  const submit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!v.asapEnabled && !v.scheduledEnabled) return setLocalError('Включите хотя бы один вариант времени получения')
    const days = Number(v.preorderDays)
    const prep = Number(v.minPrepMinutes)
    if (!Number.isInteger(days) || days < 0 || days > 14) return setLocalError('Предзаказ — от 0 до 14 дней вперёд')
    if (!Number.isInteger(prep) || prep < 0 || prep > 180) return setLocalError('Время приготовления — от 0 до 180 минут')
    save.mutate()
  }

  return (
    <form onSubmit={submit} noValidate aria-labelledby="pickup-settings-title" className="rounded-2xl border border-line bg-white p-5 sm:p-6 flex flex-col gap-4">
      <div>
        <h2 id="pickup-settings-title" className="font-serif text-xl text-ink">
          Время получения
        </h2>
        <p className="text-sm text-ink-soft mt-1">Что предлагать покупателю. Изменения действуют на новые заказы.</p>
      </div>

      <label className="flex items-start gap-3 text-sm text-ink cursor-pointer min-h-[44px]">
        <input type="checkbox" className="mt-0.5 h-5 w-5 accent-[#2B2420]" checked={v.asapEnabled} onChange={(e) => set('asapEnabled', e.target.checked)} />
        <span>
          <span className="font-medium">«Как можно скорее»</span>
          <span className="block text-xs text-muted">Заказ на ближайшее возможное время, с учётом времени приготовления.</span>
        </span>
      </label>
      <label className="flex items-start gap-3 text-sm text-ink cursor-pointer min-h-[44px]">
        <input type="checkbox" className="mt-0.5 h-5 w-5 accent-[#2B2420]" checked={v.scheduledEnabled} onChange={(e) => set('scheduledEnabled', e.target.checked)} />
        <span>
          <span className="font-medium">Заказ ко времени и предзаказ</span>
          <span className="block text-xs text-muted">Покупатель выбирает день и слот.</span>
        </span>
      </label>

      <div className="grid sm:grid-cols-3 gap-4">
        <div className="flex flex-col gap-1.5">
          <label htmlFor="slot-step" className="text-[13px] font-medium text-[#4A4038]">
            Шаг слотов
          </label>
          <select id="slot-step" className={FIELD} value={v.slotStepMinutes} onChange={(e) => set('slotStepMinutes', Number(e.target.value) as 15 | 30 | 60)}>
            <option value={15}>15 минут</option>
            <option value={30}>30 минут</option>
            <option value={60}>60 минут</option>
          </select>
        </div>
        <div className="flex flex-col gap-1.5">
          <label htmlFor="preorder-days" className="text-[13px] font-medium text-[#4A4038]">
            Предзаказ, дней вперёд
          </label>
          <input id="preorder-days" inputMode="numeric" className={FIELD} value={v.preorderDays} onChange={(e) => set('preorderDays', e.target.value)} />
          <span className="text-xs text-muted">0 — только сегодня</span>
        </div>
        <div className="flex flex-col gap-1.5">
          <label htmlFor="min-prep" className="text-[13px] font-medium text-[#4A4038]">
            Время приготовления, мин
          </label>
          <input id="min-prep" inputMode="numeric" className={FIELD} value={v.minPrepMinutes} onChange={(e) => set('minPrepMinutes', e.target.value)} />
        </div>
      </div>

      {(localError || save.isError) && <InlineError>{localError ?? getGoodsErrorMessage(save.error, 'Не удалось сохранить настройки.')}</InlineError>}
      <div className="flex items-center gap-3 flex-wrap">
        <Button type="submit" loading={save.isPending}>
          Сохранить
        </Button>
        {saved && (
          <span role="status" className="text-sm text-success font-medium">
            Сохранено
          </span>
        )}
      </div>
    </form>
  )
}
