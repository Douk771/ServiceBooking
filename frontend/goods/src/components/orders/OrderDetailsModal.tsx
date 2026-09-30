import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { format } from 'date-fns'
import { ru } from 'date-fns/locale'
import { Modal } from '@/components/ui/Modal'
import { ordersApi } from '../../api/orders'
import { customersApi } from '../../api/customers'
import { ErrorState, LoadingList } from '../StatePanels'
import { getGoodsErrorMessage } from '../../utils/orderError'
import type { OrderActorKind } from '../../types'

const ACTOR: Record<OrderActorKind, string> = { Customer: 'Покупатель', Guest: 'Гость', Staff: 'Сотрудник', SuperAdmin: 'Администратор', System: 'Система' }

/** US-23-26 (P1) — the order journal: time, author and the server-built text of every event. */
export function OrderDetailsModal({ shopId, orderId, number, onClose }: { shopId: string; orderId: string; number: number; onClose: () => void }) {
  const q = useQuery({ queryKey: ['staff-order', shopId, orderId], queryFn: () => ordersApi.get(shopId, orderId) })
  const order = q.data
  // The buyer card exists only while the order still has the buyer's data (an erased order has no `customerPhone`).
  const cardOrderId = order && order.customerPhone ? order.id : null
  // P1 (US-25-10): a one-line hint «Есть заметка» so the shop sees it before handing the order over. Best effort — no error UI.
  const noteQ = useQuery({
    queryKey: ['shop-customer-note', shopId, cardOrderId],
    queryFn: () => customersApi.getNote(shopId, cardOrderId as string),
    enabled: cardOrderId !== null,
    retry: false,
  })
  return (
    <Modal title={`Журнал заказа № ${number}`} onClose={onClose}>
      {order && cardOrderId && (
        <div className="mb-4 text-sm" data-testid="order-buyer">
          <Link to={`/cabinet/${shopId}/customers/${cardOrderId}`} onClick={onClose} className="font-medium text-ink underline underline-offset-2 hover:no-underline">
            {order.customerName || 'Покупатель'}: карточка покупателя
          </Link>
          {noteQ.data?.note && <p className="mt-1 text-ink-soft" data-testid="order-buyer-note">Есть заметка: {noteQ.data.note.text}</p>}
        </div>
      )}
      {q.isLoading ? (
        <LoadingList rows={3} rowClass="h-12" />
      ) : q.isError || !q.data ? (
        <ErrorState message={getGoodsErrorMessage(q.error, 'Не удалось загрузить журнал.')} onRetry={() => void q.refetch()} />
      ) : q.data.events.length === 0 ? (
        <p className="text-sm text-ink-soft">Событий пока нет.</p>
      ) : (
        <ol className="flex flex-col gap-4">
          {q.data.events.map((e, i) => (
            <li key={i} className="border-l-2 border-line pl-4">
              <p className="text-xs text-muted">
                {format(new Date(e.occurredAtUtc), 'd MMM, HH:mm', { locale: ru })} · {ACTOR[e.actorKind] ?? e.actorKind}
                {e.actorName ? `, ${e.actorName}` : ''}
              </p>
              <p className="text-sm text-ink mt-0.5">{e.text}</p>
              {e.reason && <p className="text-xs text-ink-soft mt-0.5">Причина: {e.reason}</p>}
              {e.comment && <p className="text-xs text-ink-soft mt-0.5">Комментарий: {e.comment}</p>}
              {e.changes && e.changes.length > 0 && (
                <ul className="mt-1 list-disc pl-5 text-xs text-ink-soft">
                  {e.changes.map((c, j) => (
                    <li key={j}>{c.text}</li>
                  ))}
                </ul>
              )}
            </li>
          ))}
        </ol>
      )}
    </Modal>
  )
}
