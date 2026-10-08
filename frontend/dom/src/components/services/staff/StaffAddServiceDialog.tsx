import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { staysBoardApi } from '../../../api/staysBoard'
import { staysServicesApi } from '../../../api/staysServices'
import type { AvailabilityDayDto, ServiceRefusalDto, StaffStayBookingCardWithServices, StayServiceRequestBasis } from '../../../types'
import { newIdempotencyKey } from '../../../utils/idempotency'
import { chosenItems, isTimeGone } from '../../../utils/serviceSelection'
import { nightDates } from '../../../utils/stayDates'
import { getStayErrorMessage, readConflict } from '../../../utils/stayError'
import { ErrorState, Skeleton } from '../../StatePanels'
import { ServicePickDialog } from '../ServicePickDialog'
import { Modal } from '@/components/ui/Modal'
import { STAFF_ADD_NOTE } from '../../../utils/serviceForms'
import { BasisSelect } from './BasisSelect'

/**
 * «Добавить услугу» on the booking card (US-39-15, ЮР39-6): the staff picks a service and a time inside the stay and MUST say how the
 * guest asked for it (phone / in person / messenger) — without the basis the server answers 400. The guest is notified and can cancel
 * the session for free. Starts are the staff's (no minimum lead; an unpublished service is allowed).
 */
export function StaffAddServiceDialog({
  companyId,
  card,
  onAdded,
  onClose,
}: {
  companyId: string
  card: Pick<StaffStayBookingCardWithServices, 'id' | 'checkInDate' | 'checkOutDate'>
  onAdded: (c: StaffStayBookingCardWithServices) => void
  onClose: () => void
}) {
  const qc = useQueryClient()
  const list = useQuery({ queryKey: ['stays-services', companyId], queryFn: () => staysServicesApi.list(companyId), staleTime: 0 })
  const [basis, setBasis] = useState<StayServiceRequestBasis | ''>('')
  const [error, setError] = useState('')
  const [resetSignal, setResetSignal] = useState(0)
  const idempotencyKey = useMemo(() => newIdempotencyKey(), [])
  // The business days of the stay: the check-in day … the check-out day (the server cuts the starts to the stay itself).
  const days: AvailabilityDayDto[] = useMemo(() => {
    const dates = [...nightDates(card.checkInDate, card.checkOutDate), card.checkOutDate]
    return dates.map((d) => ({ businessDate: d, label: d, hasStarts: true }))
  }, [card.checkInDate, card.checkOutDate])

  const add = useMutation({
    mutationFn: (args: Parameters<typeof staysBoardApi.addSessionToBooking>[2]) => staysBoardApi.addSessionToBooking(companyId, card.id, args),
    onSuccess: (c) => {
      void qc.invalidateQueries({ queryKey: ['stays-board', companyId] })
      onAdded(c)
      onClose()
    },
    onError: (err) => {
      const refusal = readConflict<ServiceRefusalDto>(err)
      setError(getStayErrorMessage(err, 'Не удалось добавить услугу.'))
      if (refusal && isTimeGone(refusal.code)) setResetSignal((n) => n + 1)
      if (refusal?.code === 'PriceChanged') void qc.invalidateQueries({ queryKey: ['stays-session-quote'] })
    },
  })

  if (list.isLoading)
    return (
      <Modal title="Добавить услугу к брони" onClose={onClose}>
        <Skeleton className="h-32" />
      </Modal>
    )
  if (list.isError || !list.data)
    return (
      <Modal title="Добавить услугу к брони" onClose={onClose}>
        <ErrorState message={getStayErrorMessage(list.error, 'Не удалось загрузить услуги.')} onRetry={() => void list.refetch()} />
      </Modal>
    )
  const services = list.data.filter((s) => !s.isArchived)
  if (services.length === 0)
    return (
      <Modal title="Добавить услугу к брони" onClose={onClose}>
        <p className="text-sm text-ink-soft">У компании нет услуг, которые можно добавить. Создайте услугу в разделе «Услуги».</p>
      </Modal>
    )

  return (
    <ServicePickDialog
      title="Добавить услугу к брони"
      services={services.map((s) => ({ id: s.id, name: s.name }))}
      staticDays={() => days}
      loadStarts={(id, date) => staysBoardApi.staffStarts(companyId, id, { date, bookingId: card.id })}
      loadQuote={(id, sel) => staysBoardApi.sessionQuote(companyId, { serviceId: id, ...sel, bookingId: card.id })}
      loadItems={(id) => staysServicesApi.staffPickItems(companyId, id)}
      confirmLabel="Добавить к брони"
      showAddNotice={false}
      hint={STAFF_ADD_NOTE}
      extra={<BasisSelect value={basis} onChange={setBasis} />}
      extraReady={basis !== ''}
      pending={add.isPending}
      error={error}
      resetSignal={resetSignal}
      onClose={onClose}
      onConfirm={(serviceId, pick, _quote, order) => {
        if (basis === '') return
        setError('')
        add.mutate({
          serviceId,
          businessDate: pick.businessDate,
          startMinute: pick.startMinute,
          hours: pick.hours,
          items: chosenItems(pick.quantities, order),
          requestBasis: basis,
          idempotencyKey,
        })
      }}
    />
  )
}
