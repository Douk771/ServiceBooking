import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Icon } from '@/components/ui/Icon'
import { LinkButton } from '@/components/slots/ui/LinkButton'
import { EmptyState, ErrorState, InlineError, LoadingList } from '@/components/slots/ui/StatePanels'
import { useCabinetCompany, useSlotVertical } from '@/components/slots/SlotVerticalContext'
import type { ServiceListItemDto } from '@/types/slots'
import { businessDateOf } from '@/utils/slots/businessClock'
import { can } from '@/utils/slots/slotPermissions'
import { PUBLISH_PROBLEM_TEXT, moveId } from '@/utils/slots/serviceForms'
import { getStayErrorMessage } from '@/utils/slots/slotError'

function statusOf(s: ServiceListItemDto): { text: string; cls: string } {
  if (s.isArchived) return { text: 'В архиве', cls: 'bg-cream-deep text-ink-soft' }
  if (s.isPublished) return { text: 'Опубликована', cls: 'bg-success-bg text-success' }
  return { text: 'Не опубликована', cls: 'bg-warning-bg text-warning' }
}

/**
 * `/cabinet/:companyId/services` — the services of the company with their state, what stops each from being published and the order the
 * guest sees them in (`ManageServices` adds and moves; `EditServiceContent` / `ManageServiceDates` only open a service).
 */
export function CabinetServicesView() {
  const { api, paths, words, NotFound } = useSlotVertical()
  const staysServicesApi = api.cabinet
  const { company } = useCabinetCompany()
  const qc = useQueryClient()
  const key = ['stays-services', company.id]
  const perms = company.myPermissions
  const manage = can(perms, 'ManageServices')
  const allowed = manage || can(perms, 'EditServiceContent') || can(perms, 'ManageServiceDates')
  const q = useQuery({ queryKey: key, queryFn: () => staysServicesApi.list(company.id), enabled: allowed })

  const order = useMutation({
    mutationFn: (ids: string[]) => staysServicesApi.reorder(company.id, ids),
    onSuccess: (list) => qc.setQueryData(key, list),
    onError: () => void qc.invalidateQueries({ queryKey: key }),
  })

  if (!allowed) return <NotFound title="Раздел недоступен" hint="Услуги открыты владельцу и управляющему." />

  const active = (q.data ?? []).filter((s) => !s.isArchived)
  const archived = (q.data ?? []).filter((s) => s.isArchived)
  const activeIds = active.map((s) => s.id)
  const today = businessDateOf(new Date()).businessDate

  const row = (s: ServiceListItemDto, index: number | null) => {
    const st = statusOf(s)
    return (
      <li key={s.id} className="flex flex-wrap items-center gap-4 rounded-2xl border border-line bg-white p-4">
        <Link to={paths.cabinetService(company.id, s.id)} className="flex min-w-0 flex-1 items-center gap-4 !text-ink">
          {s.coverThumbUrl ? (
            <img src={s.coverThumbUrl} alt="" className="h-16 w-20 shrink-0 rounded-xl object-cover" />
          ) : (
            <span className="flex h-16 w-20 shrink-0 items-center justify-center rounded-xl bg-cream-deep">
              <Icon name="calendar" size={22} className="text-muted" strokeWidth={1.4} />
            </span>
          )}
          <div className="min-w-0">
            <p className="truncate font-semibold">{s.name}</p>
            <span className={`mt-1.5 inline-block rounded-full px-2.5 py-0.5 text-xs font-semibold ${st.cls}`}>{st.text}</span>
            {!s.isPublished && !s.isArchived && s.publishProblems.length > 0 && (
              <ul className="mt-1.5 text-xs text-ink-soft">
                {s.publishProblems.map((c) => (
                  <li key={c}>· {PUBLISH_PROBLEM_TEXT[c] ?? c}</li>
                ))}
              </ul>
            )}
          </div>
        </Link>
        {manage && index !== null && (
          <div className="flex shrink-0 items-center gap-1">
            <button
              type="button"
              aria-label={`Поднять выше: ${s.name}`}
              disabled={index === 0 || order.isPending}
              onClick={() => order.mutate(moveId(activeIds, s.id, -1))}
              className="flex h-11 w-11 items-center justify-center rounded-full border border-line text-ink-soft hover:border-line-strong disabled:opacity-35"
            >
              <Icon name="arrow-up" size={15} strokeWidth={1.8} />
            </button>
            <button
              type="button"
              aria-label={`Опустить ниже: ${s.name}`}
              disabled={index === activeIds.length - 1 || order.isPending}
              onClick={() => order.mutate(moveId(activeIds, s.id, 1))}
              className="flex h-11 w-11 items-center justify-center rounded-full border border-line text-ink-soft hover:border-line-strong disabled:opacity-35"
            >
              <Icon name="arrow-down" size={15} strokeWidth={1.8} />
            </button>
          </div>
        )}
      </li>
    )
  }

  return (
    <main className="mx-auto max-w-[900px] px-4 pb-6 pt-8 sm:px-8">
      <div className="mb-5 flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 className="font-serif text-[26px] text-ink">Услуги</h2>
          <p className="mt-0.5 text-sm text-ink-soft">Баня, чан и другие услуги на время. Гости видят только опубликованные. Порядок здесь — порядок на странице компании.</p>
        </div>
        <div className="flex flex-wrap gap-2">
          {can(perms, 'ViewBookings') && (
            <LinkButton to={paths.cabinetDay(company.id, today)} variant="secondary">
              День услуг
            </LinkButton>
          )}
          {manage && (
            <LinkButton to={paths.cabinetServiceNew(company.id)}>
              <Icon name="plus" size={16} /> Добавить услугу
            </LinkButton>
          )}
        </div>
      </div>

      {q.isLoading ? (
        <LoadingList rows={3} />
      ) : q.isError ? (
        <ErrorState message={getStayErrorMessage(q.error, 'Не удалось загрузить услуги.')} onRetry={() => void q.refetch()} />
      ) : (q.data ?? []).length === 0 ? (
        <EmptyState
          title="Услуг пока нет"
          text={words.servicesEmptyText}
          action={manage ? <LinkButton to={paths.cabinetServiceNew(company.id)}>Добавить услугу</LinkButton> : undefined}
        />
      ) : (
        <>
          {order.isError && (
            <div className="mb-3">
              <InlineError>{getStayErrorMessage(order.error, 'Не удалось изменить порядок.')}</InlineError>
            </div>
          )}
          <ul className="flex flex-col gap-3">{active.map((s, i) => row(s, i))}</ul>
          {archived.length > 0 && (
            <section className="mt-8" aria-labelledby="archived-h">
              <h3 id="archived-h" className="mb-3 text-sm font-semibold text-ink-soft">
                Архив
              </h3>
              <ul className="flex flex-col gap-3 opacity-80">{archived.map((s) => row(s, null))}</ul>
            </section>
          )}
        </>
      )}
    </main>
  )
}
