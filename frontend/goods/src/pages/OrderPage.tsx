import { useEffect, useState } from 'react'
import { Link, useLocation, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { format } from 'date-fns'
import { ru } from 'date-fns/locale'
import { CompanyMapLinks } from '@/components/company/CompanyMapLinks'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { Modal } from '@/components/ui/Modal'
import { formatPhone } from '@/utils/phone'
import { dialHref } from '../utils/dial'
import { ordersApi } from '../api/orders'
import { OrderStatusBadge } from '../components/OrderStatusBadge'
import { OrderTimeline } from '../components/OrderTimeline'
import { OrderPushCard } from '../components/order/OrderPushCard'
import { ErrorState, InlineError, Skeleton } from '../components/StatePanels'
import { isFailedOutcome, isTerminalStatus } from '../utils/orderStatus'
import { formatMoney, formatQuantity, formatUnitPrice } from '../utils/quantityFormat'
import { getGoodsErrorMessage, httpStatus, readOrderConflict } from '../utils/orderError'
import { NotFoundPage } from './NotFoundPage'
import type { PublicOrderDto } from '../types'

const POLL_MS = 10_000

/**
 * US-23-20/21 — the buyer's order page at `/o/<token>`, no login needed. Polls every 10 s while the tab is open
 * and the status is not final (§397.2). The phone is masked by the server; after data erasure name/phone are null.
 */
export function OrderPage() {
  const { token = '' } = useParams<{ token: string }>()
  const location = useLocation()
  const justCreated = (location.state as { justCreated?: boolean } | null)?.justCreated === true
  const qc = useQueryClient()
  const key = ['order', token]

  const query = useQuery({
    queryKey: key,
    queryFn: () => ordersApi.getPublic(token),
    // Stops on a final status; the default refetchIntervalInBackground=false pauses it in a hidden tab.
    refetchInterval: (q) => (q.state.data && isTerminalStatus(q.state.data.status) ? false : POLL_MS),
    retry: (count, err) => httpStatus(err) === undefined && count < 2,
  })
  const order = query.data

  const [confirmCancel, setConfirmCancel] = useState(false)
  const [cancelMessage, setCancelMessage] = useState('')
  const cancel = useMutation({
    mutationFn: () => ordersApi.cancelPublic(token),
    onSuccess: (o) => {
      qc.setQueryData(key, o)
      setConfirmCancel(false)
      setCancelMessage('')
    },
    onError: (err) => {
      setConfirmCancel(false)
      const c = readOrderConflict(err)
      if (c?.publicOrder) qc.setQueryData(key, c.publicOrder)
      setCancelMessage(getGoodsErrorMessage(err, 'Не удалось отменить заказ.'))
    },
  })

  useEffect(() => {
    if (order) document.title = `Заказ № ${order.number} — ${order.shop.name}`
    return () => {
      document.title = 'ezbook · Заказы'
    }
  }, [order])

  if (query.isLoading)
    return (
      <main className="max-w-[720px] mx-auto px-4 pt-10">
        <Skeleton className="h-40 mb-4" />
        <Skeleton className="h-64" />
      </main>
    )
  if (query.isError && !order) {
    if (httpStatus(query.error) === 404) return <NotFoundPage title="Заказ не найден" hint="Проверьте ссылку: она должна быть скопирована целиком." />
    return (
      <main className="max-w-[720px] mx-auto px-4 pt-10">
        <ErrorState message={getGoodsErrorMessage(query.error, 'Не удалось загрузить заказ.')} onRetry={() => void query.refetch()} />
      </main>
    )
  }
  if (!order) return null

  return (
    <main className="max-w-[720px] mx-auto px-4 sm:px-8 pt-8 flex flex-col gap-5">
      {justCreated && (
        <div role="status" className="rounded-2xl bg-success-bg text-success px-5 py-4">
          <p className="font-semibold">Заказ оформлен</p>
          <p className="text-sm mt-0.5">Магазин увидит его сразу. Эта страница обновляется сама.</p>
        </div>
      )}

      {query.isError && (
        <p role="status" className="text-xs text-warning bg-warning-bg rounded-lg px-3 py-2">
          Нет связи — показаны последние полученные данные. Пробуем ещё раз.
        </p>
      )}

      <section className="rounded-3xl bg-white border border-line p-6 sm:p-8">
        <div className="flex items-start justify-between gap-4 flex-wrap">
          <div>
            <p className="text-xs uppercase tracking-wider text-muted">Заказ</p>
            <h1 className="font-serif text-[52px] leading-none text-ink mt-1" data-testid="order-number">
              № {order.number}
            </h1>
            <p className="text-sm text-ink-soft mt-2">{format(new Date(order.createdAtUtc), "d MMMM yyyy, HH:mm", { locale: ru })}</p>
          </div>
          <OrderStatusBadge status={order.status} text={order.statusText} large />
        </div>

        <p className="mt-5 flex items-center gap-2 flex-wrap font-serif text-2xl text-ink leading-tight" data-testid="order-pickup">
          <Icon name="clock" size={18} strokeWidth={1.7} className="shrink-0" />
          <span>{order.pickup.text}</span>
          {order.pickup.isPreorder && <span className="font-sans text-[11px] font-bold uppercase tracking-wide bg-cream-deep text-ink-soft rounded-full px-2.5 py-1">Предзаказ</span>}
        </p>
        {order.notifications.messengerRequested && !isTerminalStatus(order.status) && (
          <p className="mt-2 text-sm text-ink-soft" data-testid="order-messenger">Статус заказа пришлём сообщением в MAX/WhatsApp.</p>
        )}

        <div className="mt-6">
          {isFailedOutcome(order.status) ? (
            <div className="rounded-xl bg-cream-deep px-4 py-3">
              <p className="text-sm font-semibold text-ink">{order.statusText}</p>
              {order.reason && <p className="text-sm text-ink-soft mt-1">Причина: {order.reason}</p>}
            </div>
          ) : (
            <OrderTimeline steps={order.timeline} />
          )}
        </div>
      </section>

      <OrderPushCard token={token} info={order.notifications.webPush} />
      <ShareLink order={order} justCreated={justCreated} />
      <ShopChanges order={order} />

      <section className="rounded-3xl bg-white border border-line p-6 sm:p-8" aria-labelledby="order-items">
        <h2 id="order-items" className="text-[15px] font-semibold text-ink mb-4">
          Состав заказа
        </h2>
        <ul className="divide-y divide-line">
          {order.items.map((it, i) => (
            <li key={i} className="py-3 flex justify-between gap-4">
              <div className="min-w-0">
                <p className="text-ink font-medium">{it.name}</p>
                <p className="text-xs text-muted mt-0.5">
                  {formatUnitPrice(it.unit, it.unitPrice)}
                  {it.portionText ? ` · ${it.portionText}` : ''} · {formatQuantity(it.unit, it.quantityOrdered)}
                  {it.quantityActual != null && it.quantityActual !== it.quantityOrdered ? ` → выдано ${formatQuantity(it.unit, it.quantityActual)}` : ''}
                </p>
              </div>
              <p className="font-semibold text-ink whitespace-nowrap">{formatMoney(it.lineTotal, it.isApproximate)}</p>
            </li>
          ))}
        </ul>
        <div className="mt-4 pt-4 border-t border-line flex items-baseline justify-between">
          <span className="text-sm text-ink-soft">{order.status === 'Issued' ? 'К оплате (итог)' : 'Итого'}</span>
          <span className="font-serif text-2xl text-ink" data-testid="order-total">
            {formatMoney(order.total, order.totalIsApproximate)}
          </span>
        </div>
        {order.totalIsApproximate && <p className="text-xs text-muted mt-1">Сумма уточнится при выдаче по фактическому весу.</p>}
        {order.comment && <p className="mt-4 text-sm text-ink-soft">Комментарий: <span className="text-ink">{order.comment}</span></p>}
        <p className="mt-2 text-xs text-muted">
          {order.customerName ? `${order.customerName}${order.customerPhoneMasked ? `, ${order.customerPhoneMasked}` : ''}` : 'Данные покупателя удалены'}
        </p>
      </section>

      <section className="rounded-3xl bg-white border border-line p-6 sm:p-8" aria-labelledby="order-shop">
        <h2 id="order-shop" className="text-[15px] font-semibold text-ink mb-2">
          Где забрать
        </h2>
        <Link to={`/${order.shop.slug}`} className="font-serif text-xl text-ink hover:text-gold-dark !text-ink">
          {order.shop.name}
        </Link>
        <div className="mt-2 flex flex-col gap-1 text-sm text-ink-soft">
          {(order.shop.address || order.shop.cityName) && (
            <p className="flex items-start gap-1.5">
              <Icon name="map-pin" size={14} strokeWidth={1.8} className="mt-0.5 shrink-0" />
              {[order.shop.cityName, order.shop.address].filter(Boolean).join(', ')}
            </p>
          )}
          {order.shop.phone && (
            <p className="flex items-center gap-1.5">
              <Icon name="phone" size={14} strokeWidth={1.8} />
              <a href={dialHref(order.shop.phone) || undefined} className="text-ink font-medium hover:text-gold-dark">
                {formatPhone(order.shop.phone)}
              </a>
            </p>
          )}
          <CompanyMapLinks yandexUrl={order.shop.yandexMapsUrl} twoGisUrl={order.shop.twoGisUrl} />
        </div>
      </section>

      {cancelMessage && <InlineError>{cancelMessage}</InlineError>}
      {order.canCancel && (
        <div>
          <Button variant="danger" onClick={() => { setCancelMessage(''); setConfirmCancel(true) }}>
            Отменить заказ
          </Button>
        </div>
      )}

      {confirmCancel && (
        <Modal title={`Отменить заказ № ${order.number}?`} onClose={() => setConfirmCancel(false)} dismissible={!cancel.isPending}>
          <p className="text-sm text-ink-soft">Магазин увидит, что вы отказались от заказа. Вернуть его будет нельзя.</p>
          <div className="flex gap-3 mt-5">
            <Button variant="secondary" className="flex-1" onClick={() => setConfirmCancel(false)} disabled={cancel.isPending}>
              Не отменять
            </Button>
            <Button variant="danger" className="flex-1" loading={cancel.isPending} onClick={() => cancel.mutate()}>
              Да, отменить
            </Button>
          </div>
        </Modal>
      )}
    </main>
  )
}

/** Big link with «Скопировать» (US-23-20); a guest is told there is no other way back to the order. */
function ShareLink({ order, justCreated }: { order: PublicOrderDto; justCreated: boolean }) {
  const [copied, setCopied] = useState(false)
  const [failed, setFailed] = useState(false)
  // The address bar is the link the buyer already has; copy exactly that (no client-built absolute URLs, §423).
  const url = `${window.location.origin}${window.location.pathname}`
  if (isTerminalStatus(order.status) && !justCreated && !order.isGuest) return null

  const copy = async () => {
    setFailed(false)
    try {
      await navigator.clipboard.writeText(url)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      setFailed(true)
    }
  }

  return (
    <section className="rounded-2xl border border-line bg-cream-deep/60 px-5 py-4" aria-label="Ссылка на заказ">
      <p className="text-sm font-semibold text-ink">Ссылка на этот заказ</p>
      {order.isGuest && <p className="text-sm text-warning mt-0.5">Сохраните ссылку: другого способа вернуться к заказу нет.</p>}
      <div className="mt-2 flex items-center gap-2 flex-wrap">
        <code className="text-xs text-ink-soft break-all select-all flex-1 min-w-0" data-testid="order-link">
          {url}
        </code>
        <Button variant="secondary" size="sm" onClick={() => void copy()}>
          <Icon name={copied ? 'check' : 'copy'} size={14} />
          {copied ? 'Скопировано' : 'Скопировать'}
        </Button>
      </div>
      {failed && <p className="text-xs text-muted mt-1.5">Не удалось скопировать автоматически — выделите ссылку вручную.</p>}
    </section>
  )
}

/** «Магазин изменил заказ: было → стало» + the shop's comment (US-23-20), highlighted. */
function ShopChanges({ order }: { order: PublicOrderDto }) {
  if (order.shopChanges.length === 0) return null
  return (
    <section className="rounded-2xl border border-warning/40 bg-warning-bg px-5 py-4" aria-label="Магазин изменил заказ">
      <p className="text-sm font-semibold text-warning">Магазин изменил заказ</p>
      <ul className="mt-2 flex flex-col gap-3">
        {order.shopChanges.map((c, i) => (
          <li key={i} className="text-sm text-ink">
            <p className="text-xs text-muted">{format(new Date(c.occurredAtUtc), 'd MMMM, HH:mm', { locale: ru })}</p>
            <ul className="mt-1 list-disc pl-5">
              {c.changes.map((l, j) => (
                <li key={j}>{l.text}</li>
              ))}
            </ul>
            <p className="mt-1 text-ink-soft">
              Итого: {formatMoney(c.totalBefore)} → {formatMoney(c.totalAfter)}
            </p>
            {c.comment && <p className="mt-1 italic text-ink-soft">«{c.comment}»</p>}
          </li>
        ))}
      </ul>
    </section>
  )
}
