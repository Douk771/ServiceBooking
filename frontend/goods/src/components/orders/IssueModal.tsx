import { useMemo, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { useDebouncedValue } from '@/hooks/useDebouncedValue'
import { ordersApi } from '../../api/orders'
import { InlineError } from '../StatePanels'
import { formatMoney, formatQuantity } from '../../utils/quantityFormat'
import { getGoodsErrorMessage, readOrderConflict } from '../../utils/orderError'
import { parseActualGrams } from '../../utils/issue'
import type { ActualQuantityInput, OrderConflictDto, StaffOrderCardDto, StaffOrderDto } from '../../types'

interface Props {
  shopId: string
  order: StaffOrderCardDto
  onClose: () => void
  onDone: (order: StaffOrderDto) => void
  onConflict: (c: OrderConflictDto) => void
}

/**
 * US-23-25 — «Выдан»: actual weight for every weighed line (pre-filled with the ordered weight), the exact sum
 * from `issue-quote`, then `issue` with the same quantities. «Выдан» means «выдан и оплачен на месте».
 */
export function IssueModal({ shopId, order, onClose, onDone, onConflict }: Props) {
  const weightItems = order.items.filter((i) => i.unit === 'Weight')
  const [values, setValues] = useState<Record<string, string>>(() => Object.fromEntries(weightItems.map((i) => [i.id, String(i.quantityOrdered)])))

  const parsed = weightItems.map((i) => ({ itemId: i.id, quantity: parseActualGrams(values[i.id] ?? '') }))
  const valid = parsed.every((p) => p.quantity !== null)
  const actual: ActualQuantityInput[] = useMemo(() => (valid ? parsed.map((p) => ({ itemId: p.itemId, quantity: p.quantity as number })) : []), [valid, JSON.stringify(parsed)]) // eslint-disable-line react-hooks/exhaustive-deps
  const debounced = useDebouncedValue(actual, 300)

  const quote = useQuery({
    queryKey: ['issue-quote', shopId, order.id, JSON.stringify(debounced)],
    queryFn: () => ordersApi.issueQuote(shopId, order.id, debounced),
    enabled: valid,
    retry: 1,
  })

  const issue = useMutation({
    mutationFn: () => ordersApi.issue(shopId, order.id, order.version, actual),
    onSuccess: onDone,
    onError: (err) => {
      const c = readOrderConflict(err)
      if (c) onConflict(c)
    },
  })
  const inSync = valid && JSON.stringify(debounced) === JSON.stringify(actual)
  const lineOf = (id: string) => quote.data?.lines.find((l) => l.id === id)

  return (
    <Modal title={`Выдать заказ № ${order.number}`} onClose={onClose} dismissible={!issue.isPending}>
      <ul className="divide-y divide-line mb-4">
        {order.items.map((it) => {
          const line = lineOf(it.id)
          return (
            <li key={it.id} className="py-3 flex items-center justify-between gap-3">
              <div className="min-w-0">
                <p className="text-sm font-medium text-ink">{it.name}</p>
                <p className="text-xs text-muted">Заказано: {formatQuantity(it.unit, it.quantityOrdered)}</p>
              </div>
              {it.unit === 'Weight' ? (
                <div className="shrink-0">
                  <label htmlFor={`actual-${it.id}`} className="sr-only">
                    Фактический вес, г: {it.name}
                  </label>
                  <div className="flex items-center gap-1.5">
                    <input
                      id={`actual-${it.id}`}
                      inputMode="numeric"
                      value={values[it.id] ?? ''}
                      onChange={(e) => setValues((v) => ({ ...v, [it.id]: e.target.value }))}
                      className={`w-24 rounded-lg border px-3 py-2 text-sm text-right bg-white outline-none focus:border-gold ${parseActualGrams(values[it.id] ?? '') === null ? 'border-danger' : 'border-line'}`}
                    />
                    <span className="text-sm text-ink-soft">г</span>
                  </div>
                </div>
              ) : (
                <span className="text-sm text-ink-soft shrink-0">{formatMoney(line?.lineTotal ?? it.lineTotal)}</span>
              )}
            </li>
          )
        })}
      </ul>

      {!valid && <p role="alert" className="text-sm text-danger mb-3">Укажите фактический вес каждой весовой позиции — от 1 до 100 000 г.</p>}

      <div className="rounded-xl bg-cream-deep px-4 py-3 flex items-baseline justify-between">
        <span className="text-sm text-ink-soft">К оплате</span>
        <span className="font-serif text-2xl text-ink" data-testid="issue-total" aria-live="polite">
          {valid && inSync && quote.data ? formatMoney(quote.data.finalTotal) : '…'}
        </span>
      </div>
      {quote.isError && valid && <div className="mt-3"><InlineError>{getGoodsErrorMessage(quote.error, 'Не удалось посчитать сумму.')}</InlineError></div>}
      {issue.isError && !readOrderConflict(issue.error) && <div className="mt-3"><InlineError>{getGoodsErrorMessage(issue.error, 'Не удалось выдать заказ.')}</InlineError></div>}

      <div className="flex gap-3 mt-5">
        <Button variant="secondary" className="flex-1" onClick={onClose} disabled={issue.isPending}>
          Назад
        </Button>
        <Button className="flex-1" loading={issue.isPending} disabled={!valid || !inSync || !quote.data} onClick={() => issue.mutate()}>
          Выдать заказ
        </Button>
      </div>
    </Modal>
  )
}
