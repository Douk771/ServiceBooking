import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Icon } from '@/components/ui/Icon'
import { formatRub } from '@/utils/money'
import { staysHousesApi } from '../../api/staysHouses'
import { LinkButton } from '../../components/LinkButton'
import { EmptyState, ErrorState, InlineError, LoadingList } from '../../components/StatePanels'
import { useStaysCompany } from '../../hooks/useStaysCompany'
import type { HouseListItemDto } from '../../types'
import { can } from '../../utils/permissions'
import { publishProblemText } from '../../utils/houseForms'
import { moveHouse } from '../../utils/houseOrder'
import { getStayErrorMessage } from '../../utils/stayError'

function statusOf(h: HouseListItemDto): { text: string; cls: string } {
  if (h.isArchived) return { text: 'В архиве', cls: 'bg-cream-deep text-ink-soft' }
  if (h.isPublished) return { text: 'Опубликован', cls: 'bg-success-bg text-success' }
  return { text: 'Не опубликован', cls: 'bg-warning-bg text-warning' }
}

/**
 * `/cabinet/:companyId/houses` — the houses of the company with their state, what stops each from being published, and the order the guest
 * sees them in (`ManageHouses` moves and adds; a manager with `EditHouseContent` only opens a house to edit its description and photos).
 */
export function HousesPage() {
  const { company } = useStaysCompany()
  const qc = useQueryClient()
  const key = ['stays-houses', company.id]
  const manage = can(company.myPermissions, 'ManageHouses')
  const q = useQuery({ queryKey: key, queryFn: () => staysHousesApi.list(company.id) })

  const order = useMutation({
    mutationFn: (ids: string[]) => staysHousesApi.order(company.id, ids),
    onSuccess: (list) => qc.setQueryData(key, list),
    onError: () => void qc.invalidateQueries({ queryKey: key }),
  })

  const active = (q.data ?? []).filter((h) => !h.isArchived)
  const archived = (q.data ?? []).filter((h) => h.isArchived)
  const activeIds = active.map((h) => h.id)

  const row = (h: HouseListItemDto, index: number | null) => {
    const st = statusOf(h)
    return (
      <li key={h.id} className="flex flex-wrap items-center gap-4 rounded-2xl border border-line bg-white p-4">
        <Link to={`/cabinet/${company.id}/houses/${h.id}`} className="flex min-w-0 flex-1 items-center gap-4 !text-ink">
          {h.coverUrl ? (
            <img src={h.coverUrl} alt="" className="h-16 w-20 shrink-0 rounded-xl object-cover" />
          ) : (
            <span className="flex h-16 w-20 shrink-0 items-center justify-center rounded-xl bg-cream-deep">
              <Icon name="home" size={22} className="text-muted" strokeWidth={1.4} />
            </span>
          )}
          <div className="min-w-0">
            <p className="truncate font-semibold">{h.name}</p>
            <p className="text-xs text-muted">
              до {h.capacity} гостей
              {h.priceFromRub != null ? ` · от ${formatRub(h.priceFromRub)} за ночь` : ' · цена не задана'}
            </p>
            <span className={`mt-1.5 inline-block rounded-full px-2.5 py-0.5 text-xs font-semibold ${st.cls}`}>{st.text}</span>
            {!h.isPublished && !h.isArchived && h.publishProblems.length > 0 && (
              <ul className="mt-1.5 text-xs text-ink-soft">
                {h.publishProblems.map((c) => (
                  <li key={c}>· {publishProblemText(c)}</li>
                ))}
              </ul>
            )}
          </div>
        </Link>
        {manage && index !== null && (
          <div className="flex shrink-0 items-center gap-1">
            <button
              type="button"
              aria-label={`Поднять выше: ${h.name}`}
              disabled={index === 0 || order.isPending}
              onClick={() => {
                const next = moveHouse(activeIds, index, -1)
                if (next) order.mutate(next)
              }}
              className="flex h-11 w-11 items-center justify-center rounded-full border border-line text-ink-soft hover:border-line-strong disabled:opacity-35"
            >
              <Icon name="arrow-up" size={15} strokeWidth={1.8} />
            </button>
            <button
              type="button"
              aria-label={`Опустить ниже: ${h.name}`}
              disabled={index === activeIds.length - 1 || order.isPending}
              onClick={() => {
                const next = moveHouse(activeIds, index, 1)
                if (next) order.mutate(next)
              }}
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
          <h2 className="font-serif text-[26px] text-ink">Дома</h2>
          <p className="mt-0.5 text-sm text-ink-soft">Гости видят только опубликованные дома. Порядок здесь — порядок на странице компании.</p>
        </div>
        {manage && (
          <LinkButton to={`/cabinet/${company.id}/houses/new`}>
            <Icon name="plus" size={16} /> Добавить дом
          </LinkButton>
        )}
      </div>

      {q.isLoading ? (
        <LoadingList rows={3} />
      ) : q.isError ? (
        <ErrorState message={getStayErrorMessage(q.error, 'Не удалось загрузить дома.')} onRetry={() => void q.refetch()} />
      ) : (q.data ?? []).length === 0 ? (
        <EmptyState
          title="Домов пока нет"
          text="Добавьте первый дом: название и вместимость. Описание, фото, цены и публикацию настроите на странице дома."
          action={manage ? <LinkButton to={`/cabinet/${company.id}/houses/new`}>Добавить дом</LinkButton> : undefined}
        />
      ) : (
        <>
          {order.isError && (
            <div className="mb-3">
              <InlineError>{getStayErrorMessage(order.error, 'Не удалось изменить порядок.')}</InlineError>
            </div>
          )}
          <ul className="flex flex-col gap-3">{active.map((h, i) => row(h, i))}</ul>
          {archived.length > 0 && (
            <section className="mt-8" aria-labelledby="archived-h">
              <h3 id="archived-h" className="mb-3 text-sm font-semibold text-ink-soft">
                Архив
              </h3>
              <ul className="flex flex-col gap-3 opacity-80">{archived.map((h) => row(h, null))}</ul>
            </section>
          )}
        </>
      )}
    </main>
  )
}
