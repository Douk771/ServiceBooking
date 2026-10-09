import { useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { ManualServiceOrderDialog } from '@/components/slots/services/staff/ManualServiceOrderDialog'
import { ServiceDayList, ServiceDayScale } from '@/components/slots/services/staff/ServiceDayView'
import { EmptyState, ErrorState, LoadingList } from '@/components/slots/ui/StatePanels'
import { useMediaQuery } from '@/hooks/slots/useMediaQuery'
import { useCabinetCompany, useSlotVertical } from '@/components/slots/SlotVerticalContext'
import { businessDateOf } from '@/utils/slots/businessClock'
import { can } from '@/utils/slots/slotPermissions'
import { businessDateLabel } from '@/utils/slots/serviceTimeFormat'
import { addDays, isIsoDate } from '@/utils/slots/slotDates'
import { getStayErrorMessage } from '@/utils/slots/slotError'

const SERVICE_DAY_POLL_MS = 30_000

/**
 * `/cabinet/:companyId/service-day/:date` (`ViewBookings`, US-39-14) — «День услуг»: a scale of the business day with the sessions of
 * every service (a tablet and up) or a list per service (a phone). The day runs 06:00 → 06:00, a session over midnight is one bar. The
 * page re-reads every 30 s, so a new order or a confirmed payment shows up without a reload.
 */
export function CabinetServiceDayView() {
  const { api, paths, NotFound } = useSlotVertical()
  const staysBoardApi = api.sessions
  const { date = '' } = useParams()
  const { company } = useCabinetCompany()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const wide = useMediaQuery('(min-width: 768px)')
  const allowed = can(company.myPermissions, 'ViewBookings')
  const canBook = can(company.myPermissions, 'ManageBookings')
  const valid = isIsoDate(date)
  const [manual, setManual] = useState(false)

  const q = useQuery({
    queryKey: ['stays-service-day', company.id, date],
    queryFn: () => staysBoardApi.serviceDay(company.id, date),
    enabled: allowed && valid,
    staleTime: 0,
    refetchInterval: SERVICE_DAY_POLL_MS,
    placeholderData: (prev) => prev,
    retry: 1,
  })
  if (!allowed) return <NotFound title="Раздел недоступен" />
  if (!valid) return <NotFound title="Неверная дата" hint="Откройте «День услуг» из раздела «Услуги»." />

  const go = (d: string) => navigate(paths.cabinetDay(company.id, d))
  const today = q.data?.today ?? businessDateOf(new Date()).businessDate
  const day = q.data

  return (
    <main className="mx-auto max-w-[1280px] px-4 pb-8 pt-6 sm:px-8">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <button type="button" aria-label="Предыдущий день" onClick={() => go(addDays(date, -1))} className="flex h-11 w-11 items-center justify-center rounded-full border border-line bg-white text-ink-soft hover:border-line-strong">
            <Icon name="chevron-left" size={16} strokeWidth={1.8} />
          </button>
          <Button variant="secondary" className="min-h-[44px]" onClick={() => go(today)}>
            Сегодня
          </Button>
          <button type="button" aria-label="Следующий день" onClick={() => go(addDays(date, 1))} className="flex h-11 w-11 items-center justify-center rounded-full border border-line bg-white text-ink-soft hover:border-line-strong">
            <Icon name="chevron-right" size={16} strokeWidth={1.8} />
          </button>
          <label className="sr-only" htmlFor="sd-date">
            Дата
          </label>
          <input id="sd-date" type="date" value={date} onChange={(e) => e.target.value && go(e.target.value)} className="min-h-[44px] rounded-xl border border-line bg-white px-3 text-sm text-ink" />
          <h2 className="font-serif text-[22px] text-ink">{businessDateLabel(date)}</h2>
        </div>
        {canBook && (
          <Button className="min-h-[44px]" onClick={() => setManual(true)}>
            <Icon name="plus" size={15} /> Ручной заказ
          </Button>
        )}
      </div>
      <p className="mb-4 text-xs text-muted">День услуги идёт с 06:00 до 06:00 следующего дня: сеанс после полуночи показан на этом же дне.</p>

      {q.isLoading ? (
        <LoadingList rows={3} rowClass="h-14" />
      ) : q.isError && !day ? (
        <ErrorState message={getStayErrorMessage(q.error, 'Не удалось загрузить день услуг.')} onRetry={() => void q.refetch()} />
      ) : !day || day.services.length === 0 ? (
        <EmptyState title="Услуг пока нет" text="Когда у компании появятся услуги, здесь будет их день: окна, сеансы и время на подготовку." />
      ) : (
        <>
          {q.isError && (
            <p role="status" className="mb-3 rounded-xl bg-warning-bg px-4 py-2 text-xs text-warning">
              Не удаётся обновить данные — показано последнее известное состояние.
            </p>
          )}
          {wide ? <ServiceDayScale day={day} companyId={company.id} /> : <ServiceDayList day={day} companyId={company.id} />}
          <p className="mt-3 text-xs text-muted">Заказы, ожидающие проверки оплаты, отмечены точкой; удержания без оплаты исчезают сами, когда истекает время на оплату.</p>
        </>
      )}

      {manual && (
        <ManualServiceOrderDialog
          companyId={company.id}
          date={date}
          onClose={() => setManual(false)}
          onCreated={(id) => {
            void qc.invalidateQueries({ queryKey: ['stays-service-day', company.id] })
            setManual(false)
            navigate(paths.cabinetSession(company.id, id))
          }}
        />
      )}
    </main>
  )
}
