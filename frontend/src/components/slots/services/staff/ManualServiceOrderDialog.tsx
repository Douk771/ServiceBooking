import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Input } from '@/components/ui/Input'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { Modal } from '@/components/ui/Modal'
import type { AvailabilityDayDto, ServiceRefusalDto, StayServiceRequestBasis } from '@/types/slots'
import { COMMENT_MAX } from '@/utils/slots/guestFields'
import { newIdempotencyKey } from '@/utils/slots/idempotency'
import { chosenItems, isTimeGone } from '@/utils/slots/serviceSelection'
import { addDays } from '@/utils/slots/slotDates'
import { getStayErrorMessage, readConflict } from '@/utils/slots/slotError'
import { ErrorState, Skeleton } from '@/components/slots/ui/StatePanels'
import { ServicePickDialog } from '@/components/slots/services/ServicePickDialog'
import { BasisSelect } from '@/components/slots/services/staff/BasisSelect'
import { useSlotVertical } from '@/components/slots/SlotVerticalContext'

const DAYS_AHEAD = 14

/**
 * «Ручной заказ» (P1, US-39-15): a session without a stay for a guest who called or came. The order is `Confirmed` at once, with no
 * prepayment; the phone is optional and no messenger message is sent (there is no consent, Т37-12). The basis is required.
 */
export function ManualServiceOrderDialog({ companyId, date, onCreated, onClose }: { companyId: string; date: string; onCreated: (sessionId: string) => void; onClose: () => void }) {
  const { api, features } = useSlotVertical()
  const staysServicesApi = api.cabinet
  const staysBoardApi = api.sessions
  const qc = useQueryClient()
  const list = useQuery({ queryKey: ['stays-services', companyId], queryFn: () => staysServicesApi.list(companyId), staleTime: 0 })
  const [basis, setBasis] = useState<StayServiceRequestBasis | ''>('')
  const [name, setName] = useState('')
  const [phone, setPhone] = useState('')
  const [comment, setComment] = useState('')
  /** Optional for the staff (1…capacity, checked by the server); only where the places have a capacity. */
  const [guestsRaw, setGuestsRaw] = useState('')
  const [error, setError] = useState('')
  const [resetSignal, setResetSignal] = useState(0)
  const idempotencyKey = useMemo(() => newIdempotencyKey(), [])
  const days: AvailabilityDayDto[] = useMemo(() => Array.from({ length: DAYS_AHEAD }, (_, i) => addDays(date, i)).map((d) => ({ businessDate: d, label: d, hasStarts: true })), [date])

  const create = useMutation({
    mutationFn: (args: Parameters<typeof staysBoardApi.createManualOrder>[1]) => staysBoardApi.createManualOrder(companyId, args),
    onSuccess: (card) => {
      void qc.invalidateQueries({ queryKey: ['stays-service-day', companyId] })
      onCreated(card.id)
    },
    onError: (err) => {
      const refusal = readConflict<ServiceRefusalDto>(err)
      setError(getStayErrorMessage(err, 'Не удалось создать заказ.'))
      if (refusal && isTimeGone(refusal.code)) setResetSignal((n) => n + 1)
    },
  })

  if (list.isLoading)
    return (
      <Modal title="Ручной заказ" onClose={onClose}>
        <Skeleton className="h-32" />
      </Modal>
    )
  if (list.isError || !list.data)
    return (
      <Modal title="Ручной заказ" onClose={onClose}>
        <ErrorState message={getStayErrorMessage(list.error, 'Не удалось загрузить услуги.')} onRetry={() => void list.refetch()} />
      </Modal>
    )
  const services = list.data.filter((s) => !s.isArchived)
  if (services.length === 0)
    return (
      <Modal title="Ручной заказ" onClose={onClose}>
        <p className="text-sm text-ink-soft">У компании нет услуг. Создайте услугу в разделе «Услуги».</p>
      </Modal>
    )

  const guestReady = name.trim().length > 0 && name.trim().length <= 100
  const guestsCount = features.capacity && /^\d+$/.test(guestsRaw.trim()) ? Number(guestsRaw) : null

  return (
    <ServicePickDialog
      title="Ручной заказ услуги"
      services={services.map((s) => ({ id: s.id, name: s.name }))}
      staticDays={() => days}
      loadStarts={(id, d) => staysBoardApi.staffStarts(companyId, id, { date: d })}
      loadQuote={(id, sel) => staysBoardApi.sessionQuote(companyId, { serviceId: id, ...sel })}
      loadItems={(id) => staysServicesApi.staffPickItems(companyId, id)}
      confirmLabel="Создать заказ"
      showAddNotice={false}
      hint="Заказ сразу подтверждён, предоплаты нет. Сообщение в мессенджер гостю не отправляется."
      extra={
        <div className="flex flex-col gap-4">
          <BasisSelect value={basis} onChange={setBasis} />
          <Input label="Имя гостя *" maxLength={100} value={name} onChange={(e) => setName(e.target.value)} />
          <PhoneInput label="Телефон (необязательно)" value={phone} onChange={setPhone} />
          {features.capacity && (
            <Input label="Сколько человек (необязательно)" type="number" inputMode="numeric" min={1} value={guestsRaw} onChange={(e) => setGuestsRaw(e.target.value)} />
          )}
          <div className="flex flex-col gap-1.5">
            <label htmlFor="manual-comment" className="text-[13px] font-medium text-[#4A4038]">
              Комментарий <span className="font-normal text-muted">(необязательно)</span>
            </label>
            <textarea
              id="manual-comment"
              rows={2}
              maxLength={COMMENT_MAX}
              value={comment}
              onChange={(e) => setComment(e.target.value)}
              className="rounded-xl border border-line bg-white px-3 py-2.5 text-sm text-ink outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep"
            />
          </div>
        </div>
      }
      extraReady={basis !== '' && guestReady}
      pending={create.isPending}
      error={error}
      resetSignal={resetSignal}
      onClose={onClose}
      onConfirm={(serviceId, pick, _quote, order) => {
        if (basis === '') return
        setError('')
        create.mutate({
          serviceId,
          businessDate: pick.businessDate,
          startMinute: pick.startMinute,
          hours: pick.hours,
          items: chosenItems(pick.quantities, order),
          guestName: name.trim(),
          guestPhone: phone || null,
          comment: comment.trim() || null,
          requestBasis: basis,
          ...(guestsCount != null ? { guestsCount } : {}),
          idempotencyKey,
        })
      }}
    />
  )
}
