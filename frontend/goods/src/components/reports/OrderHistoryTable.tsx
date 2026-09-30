import { Link } from 'react-router-dom'
import { formatRub } from '@/utils/money'
import { OrderStatusBadge } from '../OrderStatusBadge'
import type { OrderHistoryRowDto } from '../../types'

interface Props {
  shopId: string
  rows: OrderHistoryRowDto[]
  caption: string
  /** false in the buyer card: the buyer is the page itself, and the phone is never repeated there. */
  showCustomer: boolean
  onOpen: (row: OrderHistoryRowDto) => void
}

/** A real table (`<th scope>`): a screen reader announces the column for every cell. The server's texts are printed as they are. */
export function OrderHistoryTable({ shopId, rows, caption, showCustomer, onOpen }: Props) {
  return (
    <div className="overflow-x-auto rounded-2xl border border-line bg-white">
      <table className="w-full text-sm text-left">
        <caption className="sr-only">{caption}</caption>
        <thead className="bg-cream-deep/60 text-xs uppercase tracking-wide text-muted">
          <tr>
            <th scope="col" className="px-4 py-3 font-semibold">№</th>
            <th scope="col" className="px-4 py-3 font-semibold">Получение</th>
            <th scope="col" className="px-4 py-3 font-semibold">Статус</th>
            {showCustomer && <th scope="col" className="px-4 py-3 font-semibold">Покупатель</th>}
            <th scope="col" className="px-4 py-3 font-semibold text-right">Позиций</th>
            <th scope="col" className="px-4 py-3 font-semibold text-right">Сумма</th>
            <th scope="col" className="px-4 py-3 font-semibold"><span className="sr-only">Действия</span></th>
          </tr>
        </thead>
        <tbody className="divide-y divide-line">
          {rows.map((r) => (
            <tr key={r.orderId} data-testid="history-row">
              <th scope="row" className="px-4 py-3 font-semibold text-ink whitespace-nowrap">№ {r.number}</th>
              <td className="px-4 py-3 text-ink-soft whitespace-nowrap">{r.pickupText}</td>
              <td className="px-4 py-3"><OrderStatusBadge status={r.status} text={r.statusText} /></td>
              {showCustomer && (
                <td className="px-4 py-3">
                  {r.personalDataErased ? (
                    <span className="text-muted">Данные покупателя удалены</span>
                  ) : (
                    <>
                      <Link to={`/cabinet/${shopId}/customers/${r.orderId}`} className="font-medium text-ink underline underline-offset-2 hover:no-underline">
                        {r.customerName || 'Покупатель'}
                      </Link>
                      {r.customerPhoneMasked && <span className="block text-xs text-muted">{r.customerPhoneMasked}</span>}
                    </>
                  )}
                </td>
              )}
              <td className="px-4 py-3 text-right text-ink-soft">{r.itemCount}</td>
              <td className="px-4 py-3 text-right text-ink whitespace-nowrap">
                {r.totalIsApproximate ? '≈ ' : ''}
                {formatRub(r.total)}
              </td>
              <td className="px-4 py-3 text-right">
                <button
                  type="button"
                  onClick={() => onOpen(r)}
                  className="text-xs font-semibold text-ink underline underline-offset-2 hover:no-underline min-h-[36px] px-1"
                  aria-label={`Журнал заказа № ${r.number}`}
                >
                  Журнал
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
