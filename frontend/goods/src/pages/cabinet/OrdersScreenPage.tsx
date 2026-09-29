import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { useShopContext } from '../../hooks/useShop'
import { useOrderBoardPolling } from '../../hooks/useOrderBoardPolling'
import { useNewOrderSound } from '../../hooks/useNewOrderSound'
import { useWakeLock } from '../../hooks/useWakeLock'
import { useOrderingStatus } from '../../hooks/useOrderingStatus'
import { ordersApi } from '../../api/orders'
import { OrderCard, type CardAction } from '../../components/orders/OrderCard'
import { ConfirmModal, ReasonModal } from '../../components/orders/ReasonModal'
import { IssueModal } from '../../components/orders/IssueModal'
import { EditOrderModal } from '../../components/orders/EditOrderModal'
import { OrderDetailsModal } from '../../components/orders/OrderDetailsModal'
import { ChangePickupModal } from '../../components/orders/ChangePickupModal'
import { AcceptancePanel } from '../../components/acceptance/AcceptancePanel'
import { OrderingBanner } from '../../components/acceptance/OrderingBanner'
import { ProductsPanel } from '../../components/orders/ProductsPanel'
import { ErrorState, InlineError, LoadingList } from '../../components/StatePanels'
import { activeHighlights, boardTitle, detectNewOrders, freshness, serverNow } from '../../utils/board'
import { getGoodsErrorMessage, httpStatus, readOrderConflict } from '../../utils/orderError'
import type { OrderConflictDto, StaffOrderCardDto, StaffOrderDto } from '../../types'

type Column = 'newOrders' | 'accepted' | 'ready'
const COLUMNS: { key: Column; title: string }[] = [
  { key: 'newOrders', title: 'Новые' },
  { key: 'accepted', title: 'Принятые' },
  { key: 'ready', title: 'Готовы к выдаче' },
]

type Dialog =
  | { kind: 'reject'; order: StaffOrderCardDto }
  | { kind: 'cancel'; order: StaffOrderCardDto }
  | { kind: 'notPickedUp'; order: StaffOrderCardDto }
  | { kind: 'issue'; order: StaffOrderCardDto }
  | { kind: 'edit'; order: StaffOrderCardDto }
  | { kind: 'changePickup'; order: StaffOrderCardDto }
  | { kind: 'details'; order: StaffOrderCardDto }

/**
 * US-23-23…26 — the staff orders screen. Polls every 5 s (worker ticker), rings and highlights new orders, keeps
 * the tab title and the screen awake, and never hides a stale connection. Three columns on a tablet/desktop, tabs
 * on a phone. Actions carry `expectedVersion`; on 409 the card is replaced by the fresh order from the body and
 * the action is NOT repeated (§416).
 */
export function OrdersScreenPage() {
  const { shop } = useShopContext()
  const autoAccept = shop.settings.acceptanceMode === 'Auto'
  const board = useOrderBoardPolling(shop.id)
  const ordering = useOrderingStatus(shop.id)
  const sound = useNewOrderSound()
  const wake = useWakeLock()

  const [now, setNow] = useState(() => Date.now())
  const [tab, setTab] = useState<Column>('newOrders')
  const [dialog, setDialog] = useState<Dialog | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [showProducts, setShowProducts] = useState(false)

  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(id)
  }, [])

  // ── new orders: sound + highlight ─────────────────────────────────────────────────────────────────
  const seenRef = useRef<Set<string> | null>(null)
  const [highlights, setHighlights] = useState<Map<string, number>>(new Map())
  const soundPlay = sound.play
  useEffect(() => {
    if (!board.state) return
    const { fresh, seen } = detectNewOrders(seenRef.current, board.state, autoAccept)
    seenRef.current = seen
    if (fresh.length > 0) {
      const at = Date.now()
      setHighlights((h) => {
        const next = new Map(h)
        fresh.forEach((id) => next.set(id, at))
        return next
      })
      soundPlay()
    }
  }, [board.state, autoAccept, soundPlay])

  const live = useMemo(() => activeHighlights(highlights, now), [highlights, now])
  const acknowledge = useCallback((id: string) => setHighlights((h) => (h.has(id) ? new Map([...h].filter(([k]) => k !== id)) : h)), [])

  // ── tab title: «(3) Заказы — Шаурма» ──────────────────────────────────────────────────────────────
  const attention = board.state ? new Set([...board.state.newOrders.map((o) => o.id), ...live.keys()]).size : 0
  useEffect(() => {
    document.title = boardTitle(attention, shop.name)
    return () => {
      document.title = 'ezbook · Заказы'
    }
  }, [attention, shop.name])

  // ── actions ───────────────────────────────────────────────────────────────────────────────────────
  const closeDialog = () => setDialog(null)
  const handleConflict = useCallback(
    (c: OrderConflictDto, number: number) => {
      // The body carries the current state of the order: show it, do not retry (§416).
      if (c.order) board.patch(c.order)
      setNotice(`Заказ № ${number}: ${c.message}`)
      setDialog(null)
      void board.refresh()
    },
    [board],
  )
  const run = useCallback(
    async (order: StaffOrderCardDto, fn: () => Promise<StaffOrderDto>) => {
      setBusyId(order.id)
      setActionError(null)
      setNotice(null)
      try {
        const updated = await fn()
        board.patch(updated)
        acknowledge(order.id)
        setDialog(null)
        void board.refresh()
      } catch (err) {
        const c = readOrderConflict(err)
        if (c) handleConflict(c, order.number)
        else {
          setDialog(null)
          setActionError(`Заказ № ${order.number}: ${getGoodsErrorMessage(err, 'Не удалось выполнить действие.')}`)
        }
      } finally {
        setBusyId(null)
      }
    },
    [board, acknowledge, handleConflict],
  )

  const onAction = (act: CardAction, order: StaffOrderCardDto) => {
    switch (act) {
      case 'accept':
        return void run(order, () => ordersApi.accept(shop.id, order.id, order.version))
      case 'ready':
        return void run(order, () => ordersApi.ready(shop.id, order.id, order.version))
      case 'reject':
      case 'cancel':
      case 'notPickedUp':
      case 'issue':
      case 'edit':
      case 'changePickup':
      case 'details':
        return setDialog({ kind: act, order })
    }
  }

  const doneFromDialog = (updated: StaffOrderDto) => {
    board.patch(updated)
    acknowledge(updated.id)
    setDialog(null)
    void board.refresh()
  }

  // ── render ────────────────────────────────────────────────────────────────────────────────────────
  const fresh = freshness(board.lastOkAt, now)
  const state = board.state
  const forbidden = !state && board.error && [401, 403, 404].includes(httpStatus(board.error) ?? 0)

  return (
    <main className="max-w-[1400px] mx-auto px-3 sm:px-6 pt-5 pb-10">
      <div className="flex items-center gap-x-4 gap-y-2 flex-wrap mb-4">
        <SoundPill state={sound.state} onEnable={() => void sound.enable()} onDisable={sound.disable} />
        <span className="text-xs text-ink-soft" role="status" data-testid="freshness">
          {fresh.seconds === null ? 'Загрузка…' : `Обновлено ${fresh.seconds} с назад`}
        </span>
        {wake === 'unsupported' && <span className="text-xs text-muted">Отключите автоблокировку экрана — браузер не умеет держать его включённым.</span>}
        {wake === 'active' && <span className="text-xs text-muted">Экран не погаснет</span>}
        <Button variant="secondary" size="sm" className="ml-auto" onClick={() => setShowProducts(true)}>
          <Icon name="shopping-bag" size={14} /> Товары
        </Button>
      </div>

      {state && (state.acceptance ?? ordering.data?.acceptance) && (
        <AcceptancePanel
          shopId={shop.id}
          acceptance={(state.acceptance ?? ordering.data?.acceptance)!}
          serverNowMs={serverNow(state, now)}
          onChanged={() => {
            void board.refresh()
            void ordering.refresh()
          }}
        />
      )}
      {ordering.data ? (
        <OrderingBanner status={ordering.data} />
      ) : ordering.isError && state ? (
        <p className="mb-4 text-xs text-muted" role="status">Не удалось обновить статус приёма заказов — повторим через минуту.</p>
      ) : null}

      {sound.state === 'off' && (
        <div className="mb-4 rounded-xl bg-warning-bg text-warning px-4 py-3 flex items-center justify-between gap-3 flex-wrap">
          <span className="text-sm font-medium">Звук выключен: браузер разрешает его только после нажатия.</span>
          <Button size="sm" onClick={() => void sound.enable()}>
            <Icon name="volume" size={14} /> Включить звук
          </Button>
        </div>
      )}
      {sound.state === 'blocked' && (
        <div role="alert" className="mb-4 rounded-xl bg-danger-bg text-danger px-4 py-3 flex items-center justify-between gap-3 flex-wrap">
          <span className="text-sm font-medium">Звук заблокирован браузером — новые заказы не зазвучат. Нажмите, чтобы включить снова.</span>
          <Button size="sm" onClick={() => void sound.enable()}>Включить звук</Button>
        </div>
      )}
      {fresh.stale && (
        <div role="alert" className="mb-4 rounded-xl bg-danger text-white px-4 py-3 text-sm font-semibold" data-testid="stale-banner">
          Нет связи, новые заказы могут не появиться
        </div>
      )}
      {notice && (
        <div role="status" className="mb-4 rounded-xl bg-info-bg text-info px-4 py-3 text-sm flex items-start justify-between gap-3">
          <span>{notice}</span>
          <button className="text-xs font-semibold underline shrink-0" onClick={() => setNotice(null)}>Понятно</button>
        </div>
      )}
      {actionError && <div className="mb-4"><InlineError>{actionError}</InlineError></div>}

      {!state ? (
        forbidden ? (
          <ErrorState message="Нет доступа к экрану заказов этого магазина." />
        ) : board.error ? (
          <ErrorState message={getGoodsErrorMessage(board.error, 'Не удалось загрузить заказы.')} onRetry={() => void board.refresh()} />
        ) : (
          <LoadingList rows={3} rowClass="h-40" />
        )
      ) : (
        <>
          {/* phone: tabs */}
          <div className="md:hidden flex gap-1 mb-3 rounded-full bg-cream-deep p-1" role="tablist" aria-label="Колонки заказов">
            {COLUMNS.map((c) => (
              <button
                key={c.key}
                role="tab"
                aria-selected={tab === c.key}
                onClick={() => setTab(c.key)}
                className={`flex-1 rounded-full px-2 py-2 text-xs font-semibold transition-colors ${tab === c.key ? 'bg-white text-ink shadow-soft' : 'text-ink-soft'}`}
              >
                {c.title} <span className="ml-0.5 text-muted">{state[c.key].length}</span>
              </button>
            ))}
          </div>

          <div className="grid md:grid-cols-3 gap-4 items-start">
            {COLUMNS.map((c) => (
              <section key={c.key} aria-label={c.title} className={`${tab === c.key ? 'block' : 'hidden'} md:block`}>
                <h2 className="hidden md:flex items-center gap-2 font-serif text-xl text-ink mb-3">
                  {c.title}
                  <span className="text-sm font-sans text-muted">{state[c.key].length}</span>
                </h2>
                {state[c.key].length === 0 ? (
                  <p className="rounded-2xl border border-dashed border-line-strong px-4 py-8 text-center text-sm text-muted">
                    {c.key === 'newOrders' ? 'Новых заказов нет' : c.key === 'accepted' ? 'Нет принятых заказов' : 'Нет заказов, готовых к выдаче'}
                  </p>
                ) : (
                  <div className="flex flex-col gap-3">
                    {state[c.key].map((o) => (
                      <OrderCard key={o.id} order={o} serverNow={state.serverTimeUtc} nowMs={serverNow(state, now)} highlighted={live.has(o.id)} busy={busyId === o.id} onAction={onAction} />
                    ))}
                  </div>
                )}
              </section>
            ))}
          </div>

          {/* «Предзаказы» — accepted orders with a pick-up date after today, grouped by date (§480). */}
          <section aria-label="Предзаказы" className="mt-10" data-testid="preorders">
            <h2 className="font-serif text-xl text-ink mb-3">
              Предзаказы <span className="text-sm font-sans text-muted">{state.preorders.reduce((n, g) => n + g.orders.length, 0)}</span>
            </h2>
            {state.preorders.length === 0 ? (
              <p className="text-sm text-muted">Заказов на другие дни нет. Принятые предзаказы появятся здесь и сами перейдут в «Принятые» в день выдачи.</p>
            ) : (
              <div className="flex flex-col gap-6">
                {state.preorders.map((g) => (
                  <div key={g.date}>
                    <h3 className="text-sm font-semibold text-ink-soft mb-2">
                      {g.label} <span className="font-normal text-muted">· {g.orders.length}</span>
                    </h3>
                    <div className="grid sm:grid-cols-2 lg:grid-cols-3 gap-3">
                      {g.orders.map((o) => (
                        <OrderCard key={o.id} order={o} serverNow={state.serverTimeUtc} nowMs={serverNow(state, now)} highlighted={false} busy={busyId === o.id} onAction={onAction} />
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </section>

          <section aria-label="Завершённые сегодня" className="mt-10">
            <h2 className="font-serif text-xl text-ink mb-3">
              Завершённые сегодня <span className="text-sm font-sans text-muted">{state.completedToday.length}</span>
            </h2>
            {state.completedToday.length === 0 ? (
              <p className="text-sm text-muted">Пока ничего: здесь появятся выданные, отклонённые, отменённые и не забранные заказы.</p>
            ) : (
              <div className="grid sm:grid-cols-2 lg:grid-cols-3 gap-3">
                {state.completedToday.map((o) => (
                  <OrderCard key={o.id} order={o} serverNow={state.serverTimeUtc} nowMs={serverNow(state, now)} highlighted={false} busy={false} readOnly onAction={onAction} />
                ))}
              </div>
            )}
          </section>
        </>
      )}

      {dialog?.kind === 'reject' && (
        <ReasonModal title={`Отклонить заказ № ${dialog.order.number}?`} confirmLabel="Отклонить" busy={busyId === dialog.order.id} onClose={closeDialog} onConfirm={(reason) => void run(dialog.order, () => ordersApi.reject(shop.id, dialog.order.id, dialog.order.version, reason))} />
      )}
      {dialog?.kind === 'cancel' && (
        <ReasonModal title={`Отменить заказ № ${dialog.order.number}?`} description="Резерв товаров вернётся, покупатель увидит статус «Отменён магазином»." confirmLabel="Отменить заказ" busy={busyId === dialog.order.id} onClose={closeDialog} onConfirm={(reason) => void run(dialog.order, () => ordersApi.cancel(shop.id, dialog.order.id, dialog.order.version, reason))} />
      )}
      {dialog?.kind === 'notPickedUp' && (
        <ConfirmModal title={`Заказ № ${dialog.order.number} не забран?`} text="Заказ закроется со статусом «Не забран», резерв товаров вернётся." confirmLabel="Да, не забран" busy={busyId === dialog.order.id} onClose={closeDialog} onConfirm={() => void run(dialog.order, () => ordersApi.notPickedUp(shop.id, dialog.order.id, dialog.order.version))} />
      )}
      {dialog?.kind === 'issue' && (
        <IssueModal shopId={shop.id} order={dialog.order} onClose={closeDialog} onDone={doneFromDialog} onConflict={(c) => handleConflict(c, dialog.order.number)} />
      )}
      {dialog?.kind === 'edit' && (
        <EditOrderModal shopId={shop.id} order={dialog.order} onClose={closeDialog} onDone={doneFromDialog} onConflict={(c) => handleConflict(c, dialog.order.number)} />
      )}
      {dialog?.kind === 'changePickup' && (
        <ChangePickupModal shopId={shop.id} order={dialog.order} onClose={closeDialog} onDone={doneFromDialog} onConflict={(c) => handleConflict(c, dialog.order.number)} />
      )}
      {dialog?.kind === 'details' && <OrderDetailsModal shopId={shop.id} orderId={dialog.order.id} number={dialog.order.number} onClose={closeDialog} />}
      {showProducts && <ProductsPanel shopId={shop.id} trackStock={shop.settings.trackStock} onClose={() => setShowProducts(false)} />}
    </main>
  )
}

function SoundPill({ state, onEnable, onDisable }: { state: 'off' | 'on' | 'blocked' | 'unsupported'; onEnable: () => void; onDisable: () => void }) {
  if (state === 'unsupported') return <span className="text-xs font-semibold px-3 py-1.5 rounded-full bg-cream-deep text-muted">Звук недоступен в этом браузере</span>
  const on = state === 'on'
  return (
    <button
      type="button"
      onClick={on ? onDisable : onEnable}
      aria-pressed={on}
      className={`inline-flex items-center gap-1.5 text-xs font-semibold px-3 py-1.5 rounded-full border transition-colors ${on ? 'bg-success-bg text-success border-transparent' : state === 'blocked' ? 'bg-danger-bg text-danger border-transparent' : 'bg-white text-ink-soft border-line'}`}
    >
      <Icon name={on ? 'volume' : 'volume-off'} size={13} />
      {on ? 'Звук включён' : state === 'blocked' ? 'Звук заблокирован' : 'Звук выключен'}
    </button>
  )
}
