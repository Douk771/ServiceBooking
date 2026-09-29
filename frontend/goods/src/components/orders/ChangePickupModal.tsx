import { useEffect, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { ordersApi } from '../../api/orders'
import { scheduleApi } from '../../api/schedule'
import { RadioChips } from '../pickup/RadioChips'
import { SlotList } from '../pickup/SlotList'
import { InlineError } from '../StatePanels'
import { getGoodsErrorMessage, readOrderConflict } from '../../utils/orderError'
import type { OrderConflictDto, StaffOrderCardDto, StaffOrderDto } from '../../types'

interface Props {
  shopId: string
  order: StaffOrderCardDto
  onClose: () => void
  onDone: (o: StaffOrderDto) => void
  onConflict: (c: OrderConflictDto) => void
}

type Kind = 'Asap' | 'Slot'

/**
 * US-24-09 (P1) — staff moves the pick-up time. The slots are the STAFF variant (`GET …/pickup-slots`: no preparation
 * time or pause, only «end is in the future»); the date is a plain date field because the staff route has no date list.
 * The buyer gets a notification and, if the date changes, the order number may change — the server says so in the journal.
 */
export function ChangePickupModal({ shopId, order, onClose, onDone, onConflict }: Props) {
  const [kind, setKind] = useState<Kind>('Slot')
  const [date, setDate] = useState(order.pickup.date)
  const [slot, setSlot] = useState<string | null>(null)
  const [comment, setComment] = useState('')
  const [error, setError] = useState<string | null>(null)

  const slots = useQuery({
    queryKey: ['staff-pickup-slots', shopId, date],
    queryFn: () => scheduleApi.staffPickupSlots(shopId, date),
    enabled: kind === 'Slot' && /^\d{4}-\d{2}-\d{2}$/.test(date),
  })
  useEffect(() => setSlot(null), [date])

  const save = useMutation({
    mutationFn: () =>
      ordersApi.changePickup(shopId, order.id, {
        expectedVersion: order.version,
        pickup: kind === 'Asap' ? { kind: 'Asap' } : { kind: 'Slot', date, slotStartUtc: slot ?? undefined },
        comment: comment.trim() || undefined,
      }),
    onSuccess: onDone,
    onError: (err) => {
      const c = readOrderConflict(err)
      if (c && c.code !== 'PickupTimeUnavailable') return onConflict(c) // stale version / wrong status: show the fresh order
      if (c) void slots.refetch()
      setError(c?.message ?? getGoodsErrorMessage(err, 'Не удалось изменить время.'))
    },
  })

  const canSave = !save.isPending && (kind === 'Asap' || slot !== null) && comment.length <= 500

  return (
    <Modal title={`Время получения — заказ № ${order.number}`} onClose={onClose} dismissible={!save.isPending}>
      <p className="text-sm text-ink-soft mb-4">
        Сейчас: <span className="font-medium text-ink">{order.pickup.text}</span>
      </p>
      <div className="flex flex-col gap-4">
        <RadioChips<Kind>
          label="Как изменить время"
          value={kind}
          onChange={setKind}
          options={[
            { value: 'Slot', label: 'Выбрать время' },
            { value: 'Asap', label: 'Как можно скорее' },
          ]}
        />
        {kind === 'Slot' && (
          <>
            <div className="flex flex-col gap-1.5">
              <label htmlFor="change-pickup-date" className="text-[13px] font-medium text-[#4A4038]">
                Дата
              </label>
              <input id="change-pickup-date" type="date" value={date} min={order.businessDate} onChange={(e) => setDate(e.target.value)} className="rounded-xl border border-line px-4 py-3 text-sm bg-white text-ink outline-none focus:border-gold w-fit min-h-[44px]" />
            </div>
            <SlotList
              groupLabel="Время получения"
              data={slots.data}
              isLoading={slots.isLoading}
              error={slots.isError ? getGoodsErrorMessage(slots.error, 'Не удалось загрузить время.') : null}
              onRetry={() => void slots.refetch()}
              selectedStartUtc={slot}
              onSelect={(s) => setSlot(s.startUtc)}
            />
          </>
        )}
        <div className="flex flex-col gap-1.5">
          <label htmlFor="change-pickup-comment" className="text-[13px] font-medium text-[#4A4038]">
            Комментарий покупателю (необязательно)
          </label>
          <textarea id="change-pickup-comment" rows={2} maxLength={500} value={comment} onChange={(e) => setComment(e.target.value)} className="rounded-xl border border-line px-4 py-3 text-sm resize-none bg-white text-ink outline-none focus:border-gold" />
        </div>
        {error && <InlineError>{error}</InlineError>}
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose} disabled={save.isPending}>
            Отмена
          </Button>
          <Button onClick={() => save.mutate()} disabled={!canSave} loading={save.isPending}>
            Сохранить
          </Button>
        </div>
      </div>
    </Modal>
  )
}
