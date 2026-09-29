import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { menuApi } from '../../api/menu'
import { useShopContext } from '../../hooks/useShop'
import { EmptyState, ErrorState, InlineError, LoadingList } from '../../components/StatePanels'
import { groupByCategory, initialSelection, sameSelection, toggleId } from '../../utils/menu'
import { getGoodsErrorMessage } from '../../utils/orderError'
import type { DailyMenuDto } from '../../types'

/**
 * `/cabinet/:shopId/menu` — «Меню на дату» (US-24-11): a calendar of dates, and for the chosen date the products that
 * are sold that day. A date without a saved menu is pre-filled by the server with the products its weekday allows;
 * «Удалить меню» returns the date to the weekday rule. The menu never changes orders that already exist.
 */
export function MenuPage() {
  const { shop } = useShopContext()
  const qc = useQueryClient()
  const calendarKey = ['daily-menu-calendar', shop.id]
  const calendar = useQuery({ queryKey: calendarKey, queryFn: () => menuApi.calendar(shop.id) })
  const [date, setDate] = useState<string | null>(null)

  useEffect(() => {
    if (!date && calendar.data?.days[0]) setDate(calendar.data.days[0].date)
  }, [calendar.data, date])

  return (
    <main className="max-w-[980px] mx-auto px-4 sm:px-8 pt-8 pb-12">
      <h2 className="font-serif text-2xl text-ink">Меню на дату</h2>
      <p className="text-sm text-ink-soft mt-1 mb-5">Выберите день и отметьте товары, которые в этот день продаются. Без меню действуют дни недели из карточки товара.</p>

      {calendar.isLoading ? (
        <LoadingList rows={2} rowClass="h-14" />
      ) : calendar.isError ? (
        <ErrorState message={getGoodsErrorMessage(calendar.error, 'Не удалось загрузить календарь меню.')} onRetry={() => void calendar.refetch()} />
      ) : !calendar.data || calendar.data.days.length === 0 ? (
        <EmptyState title="Нет дат для меню" text="Даты появятся, когда магазин начнёт принимать заказы." />
      ) : (
        <>
          <nav aria-label="Дата меню" className="flex gap-2 overflow-x-auto pb-2 -mx-1 px-1">
            {calendar.data.days.map((d) => (
              <button
                key={d.date}
                type="button"
                aria-current={d.date === date ? 'date' : undefined}
                onClick={() => setDate(d.date)}
                className={`shrink-0 min-h-[56px] min-w-[88px] rounded-2xl border px-3 py-2 text-left transition-colors ${d.date === date ? 'bg-ink text-cream border-ink' : 'bg-white text-ink border-line hover:border-line-strong'}`}
              >
                <span className="block text-sm font-semibold">{d.label}</span>
                <span className={`block text-xs ${d.date === date ? 'text-cream/80' : 'text-muted'}`}>{d.hasMenu ? `меню: ${d.productCount}` : 'по дням недели'}</span>
              </button>
            ))}
          </nav>
          {date && (
            <MenuEditor
              key={date}
              shopId={shop.id}
              date={date}
              otherDates={calendar.data.days.filter((d) => d.hasMenu && d.date !== date)}
              onChanged={() => void qc.invalidateQueries({ queryKey: calendarKey })}
            />
          )}
        </>
      )}
    </main>
  )
}

function MenuEditor({ shopId, date, otherDates, onChanged }: { shopId: string; date: string; otherDates: { date: string; label: string }[]; onChanged: () => void }) {
  const qc = useQueryClient()
  const key = ['daily-menu', shopId, date]
  const q = useQuery({ queryKey: key, queryFn: () => menuApi.get(shopId, date) })
  const [selection, setSelection] = useState<Set<string>>(new Set())
  const [base, setBase] = useState<Set<string>>(new Set())
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [copyFrom, setCopyFrom] = useState('')
  const [saved, setSaved] = useState(false)

  const apply = (dto: DailyMenuDto) => {
    const s = initialSelection(dto.products)
    setSelection(s)
    setBase(s)
  }
  useEffect(() => {
    if (q.data) apply(q.data)
  }, [q.data])

  const after = (dto: DailyMenuDto) => {
    qc.setQueryData(key, dto)
    apply(dto)
    setSaved(true)
    onChanged()
  }
  const save = useMutation({ mutationFn: () => menuApi.put(shopId, date, [...selection]), onSuccess: after })
  const copy = useMutation({ mutationFn: () => menuApi.copy(shopId, date, copyFrom), onSuccess: after })
  const remove = useMutation({
    mutationFn: () => menuApi.remove(shopId, date),
    onSuccess: () => {
      setConfirmDelete(false)
      setSaved(false)
      void qc.invalidateQueries({ queryKey: key })
      onChanged()
    },
  })

  const groups = useMemo(() => groupByCategory(q.data?.products ?? []), [q.data])
  const dirty = !sameSelection(selection, base)

  if (q.isLoading) return <div className="mt-5"><LoadingList rows={4} rowClass="h-14" /></div>
  if (q.isError || !q.data) return <div className="mt-5"><ErrorState message={getGoodsErrorMessage(q.error, 'Не удалось загрузить меню на дату.')} onRetry={() => void q.refetch()} /></div>

  const menu = q.data
  const error = save.error ?? copy.error ?? remove.error

  return (
    <section aria-labelledby="menu-editor-title" className="mt-5 rounded-2xl border border-line bg-white p-5 sm:p-6">
      <div className="flex items-start justify-between gap-3 flex-wrap">
        <div>
          <h3 id="menu-editor-title" className="font-serif text-xl text-ink">
            {menu.label}
          </h3>
          <p className="text-sm text-ink-soft mt-0.5" data-testid="menu-state">
            {menu.exists ? 'Меню на эту дату сохранено.' : 'Меню ещё не сохранено — отмечены товары, разрешённые в этот день недели.'}
          </p>
        </div>
        <div className="flex gap-2 flex-wrap">
          {otherDates.length > 0 && (
            <div className="flex items-center gap-2">
              <label htmlFor="copy-from" className="sr-only">
                Скопировать меню с даты
              </label>
              <select id="copy-from" value={copyFrom} onChange={(e) => setCopyFrom(e.target.value)} className="rounded-xl border border-line bg-white px-3 py-2 text-sm min-h-[44px]">
                <option value="">Скопировать с даты…</option>
                {otherDates.map((d) => (
                  <option key={d.date} value={d.date}>
                    {d.label}
                  </option>
                ))}
              </select>
              <Button variant="secondary" className="min-h-[44px]" disabled={!copyFrom} loading={copy.isPending} onClick={() => copy.mutate()}>
                Скопировать
              </Button>
            </div>
          )}
          {menu.exists && (
            <Button variant="danger" className="min-h-[44px]" onClick={() => setConfirmDelete(true)}>
              Удалить меню
            </Button>
          )}
        </div>
      </div>

      {menu.products.length === 0 ? (
        <div className="mt-5">
          <EmptyState title="В каталоге пока нет товаров" text="Сначала добавьте товары в каталог." action={<Link to={`/cabinet/${shopId}/catalog`} className="text-gold font-semibold">Открыть каталог</Link>} />
        </div>
      ) : (
        <div className="mt-5 flex flex-col gap-6">
          {groups.map((g) => (
            <fieldset key={g.name}>
              <legend className="text-sm font-semibold text-ink mb-2">{g.name}</legend>
              <ul className="flex flex-col gap-1">
                {g.products.map((p) => (
                  <li key={p.productId}>
                    <label className="flex items-center gap-3 rounded-xl px-3 min-h-[44px] hover:bg-cream-deep/60 cursor-pointer">
                      <input type="checkbox" className="h-5 w-5 accent-[#2B2420]" checked={selection.has(p.productId)} onChange={() => { setSaved(false); setSelection(toggleId(selection, p.productId)) }} />
                      <span className="text-sm text-ink flex-1">{p.name}</span>
                      {!p.isPublished && <span className="text-xs text-muted">не опубликован</span>}
                      {!p.allowedByWeekdays && <span className="text-xs text-muted">не по дню недели</span>}
                    </label>
                  </li>
                ))}
              </ul>
            </fieldset>
          ))}
        </div>
      )}

      {error && (
        <div className="mt-4">
          <InlineError>{getGoodsErrorMessage(error, 'Не удалось выполнить действие с меню.')}</InlineError>
        </div>
      )}
      <div className="mt-5 flex items-center gap-3 flex-wrap">
        <Button onClick={() => save.mutate()} loading={save.isPending} disabled={menu.products.length === 0 || (!dirty && menu.exists)}>
          Сохранить меню
        </Button>
        <span className="text-sm text-ink-soft">Выбрано: {selection.size}</span>
        {saved && !dirty && (
          <span role="status" className="text-sm text-success font-medium">
            Сохранено
          </span>
        )}
      </div>

      {confirmDelete && (
        <Modal title="Удалить меню на эту дату?" onClose={() => setConfirmDelete(false)} dismissible={!remove.isPending}>
          <p className="text-sm text-ink-soft">Дата вернётся к дням недели из карточек товаров. Уже созданные заказы не изменятся.</p>
          <div className="flex gap-3 mt-5">
            <Button variant="secondary" className="flex-1" onClick={() => setConfirmDelete(false)} disabled={remove.isPending}>
              Отмена
            </Button>
            <Button variant="danger" className="flex-1" loading={remove.isPending} onClick={() => remove.mutate()}>
              Удалить
            </Button>
          </div>
        </Modal>
      )}
    </section>
  )
}
