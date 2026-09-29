import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { formatPhone, telHref } from '@/utils/phone'
import { OrderStatusBadge } from '../OrderStatusBadge'
import { formatAgo, minutesAgo } from '../../utils/orderStatus'
import { formatMoney, formatQuantity } from '../../utils/quantityFormat'
import type { OrderAction, StaffOrderCardDto } from '../../types'

export type CardAction = 'accept' | 'reject' | 'ready' | 'issue' | 'notPickedUp' | 'cancel' | 'edit' | 'details'

const LABELS: Record<OrderAction, { label: string; act: CardAction; primary?: boolean; danger?: boolean }> = {
  Accept: { label: 'Принять', act: 'accept', primary: true },
  MarkReady: { label: 'Готов к выдаче', act: 'ready', primary: true },
  Issue: { label: 'Выдать', act: 'issue', primary: true },
  Reject: { label: 'Отклонить', act: 'reject', danger: true },
  NotPickedUp: { label: 'Не забран', act: 'notPickedUp' },
  Cancel: { label: 'Отменить', act: 'cancel', danger: true },
  Edit: { label: 'Изменить', act: 'edit' },
}

interface Props {
  order: StaffOrderCardDto
  serverNow: string
  highlighted: boolean
  busy: boolean
  readOnly?: boolean
  onAction: (act: CardAction, order: StaffOrderCardDto) => void
}

/**
 * US-23-23 — one order on the board. Which buttons exist is decided ONLY by `availableActions` (the client
 * never derives it from the status, §396.1). The highlight is a frame plus a text badge — not colour alone.
 */
export function OrderCard({ order, serverNow, highlighted, busy, readOnly, onAction }: Props) {
  const ago = formatAgo(minutesAgo(order.createdAtUtc, serverNow))
  const erased = !order.customerName && !order.customerPhone
  const actions = readOnly ? [] : order.availableActions.map((a) => LABELS[a]).filter(Boolean)

  return (
    <article
      className={`rounded-2xl bg-white p-4 border-2 transition-colors ${highlighted ? 'border-gold shadow-card' : 'border-line'}`}
      aria-label={`Заказ № ${order.number}`}
      data-testid={`order-card-${order.number}`}
      data-highlighted={highlighted ? 'true' : 'false'}
    >
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="flex items-center gap-2 flex-wrap">
            <h3 className="font-serif text-2xl text-ink leading-none">№ {order.number}</h3>
            {highlighted && <span className="text-[11px] font-bold uppercase tracking-wide bg-gold text-white rounded-full px-2 py-0.5">Новый</span>}
            {order.isModified && <span className="text-[11px] font-semibold bg-warning-bg text-warning rounded-full px-2 py-0.5">изменён</span>}
          </div>
          <p className="text-xs text-muted mt-1">{ago}</p>
        </div>
        {readOnly && <OrderStatusBadge status={order.status} text={order.statusText} />}
      </div>

      <div className="mt-3 text-sm">
        {erased ? (
          <p className="text-muted">Данные покупателя удалены</p>
        ) : (
          <>
            <p className="font-medium text-ink">{order.customerName}</p>
            {order.customerPhone && (
              <a href={telHref(order.customerPhone) || undefined} className="inline-flex items-center gap-1.5 text-ink-soft hover:text-gold-dark min-h-[36px]">
                <Icon name="phone" size={13} strokeWidth={1.8} />
                {formatPhone(order.customerPhone)}
              </a>
            )}
            <p className="text-[11px] text-muted">
              {order.customerKind === 'Guest' ? 'Гость' : 'Аккаунт'}
              {order.customerPhoneVerified ? ' · номер подтверждён' : ''}
            </p>
          </>
        )}
      </div>

      <ul className="mt-3 divide-y divide-line text-sm">
        {order.items.map((it) => (
          <li key={it.id} className="py-1.5 flex justify-between gap-3">
            <span className="text-ink">{it.name}</span>
            <span className="text-ink-soft whitespace-nowrap">
              {formatQuantity(it.unit, it.quantityActual ?? it.quantityOrdered)}
            </span>
          </li>
        ))}
      </ul>

      {order.comment && <p className="mt-2 text-sm text-ink rounded-lg bg-cream-deep px-3 py-2">«{order.comment}»</p>}
      {order.reason && <p className="mt-2 text-xs text-ink-soft">Причина: {order.reason}</p>}

      <div className="mt-3 flex items-baseline justify-between">
        <span className="text-xs text-muted">Итого</span>
        <span className="font-semibold text-ink">{formatMoney(order.total, order.totalIsApproximate)}</span>
      </div>

      <div className="mt-3 flex flex-wrap gap-2">
          {actions.map((a) => (
            <Button
              key={a.act}
              size="sm"
              variant={a.primary ? 'primary' : a.danger ? 'danger' : 'secondary'}
              disabled={busy}
              loading={busy && !!a.primary}
              onClick={() => onAction(a.act, order)}
            >
              {a.label}
            </Button>
          ))}
          <Button size="sm" variant="ghost" onClick={() => onAction('details', order)}>
            Журнал
          </Button>
        </div>
    </article>
  )
}
