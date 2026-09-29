import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { Modal } from '@/components/ui/Modal'
import { catalogApi } from '../../api/catalog'
import { ordersApi } from '../../api/orders'
import { InlineError } from '../StatePanels'
import { isEdited, linesFromOrder, previewEditTotal, toEditPayload, validateEdit, type EditLine } from '../../utils/orderEdit'
import { filterProducts } from '../../utils/catalogGroups'
import { formatMoney, formatQuantity, formatUnitPrice } from '../../utils/quantityFormat'
import { getGoodsErrorMessage, readOrderConflict } from '../../utils/orderError'
import type { OrderConflictDto, ProductDto, StaffOrderCardDto, StaffOrderDto } from '../../types'

interface Props {
  shopId: string
  order: StaffOrderCardDto
  onClose: () => void
  onDone: (order: StaffOrderDto) => void
  onConflict: (c: OrderConflictDto) => void
}

/**
 * US-23-24 — change quantities/weights, remove a line (never the last one), replace a line with another product
 * at today's catalogue price, add a comment for the buyer. Sends the FULL desired composition (§417).
 */
export function EditOrderModal({ shopId, order, onClose, onDone, onConflict }: Props) {
  const [lines, setLines] = useState<EditLine[]>(() => linesFromOrder(order.items))
  const [comment, setComment] = useState('')
  const [search, setSearch] = useState('')
  const [adding, setAdding] = useState(false)
  const [problem, setProblem] = useState<string | null>(null)
  const [serverProblems, setServerProblems] = useState<{ productId: string; name: string; message: string }[]>([])

  const products = useQuery({ queryKey: ['shop-products', shopId], queryFn: () => catalogApi.products(shopId), enabled: adding, staleTime: 30_000 })

  const save = useMutation({
    mutationFn: () => ordersApi.edit(shopId, order.id, { expectedVersion: order.version, items: toEditPayload(lines), commentForCustomer: comment.trim() || undefined }),
    onSuccess: onDone,
    onError: (err) => {
      const c = readOrderConflict(err)
      if (c && (c.code === 'VersionMismatch' || c.code === 'InvalidTransition')) {
        onConflict(c)
        return
      }
      if (c) {
        setProblem(c.message)
        setServerProblems((c.problems ?? []).map((p) => ({ productId: p.productId, name: p.name, message: p.message })))
        return
      }
      setProblem(getGoodsErrorMessage(err, 'Не удалось сохранить изменения.'))
    },
  })

  const preview = previewEditTotal(lines)
  const setQty = (key: string, quantity: number) => setLines((ls) => ls.map((l) => (l.key === key ? { ...l, quantity } : l)))
  // A product that currently has a line (kept old line or a new one) cannot be added a second time; a line
  // removed in this dialog frees its product again, which is how «replace» and «put it back» work.
  const inOrder = new Set(
    lines
      .map((l) => l.productId ?? order.items.find((i) => i.id === l.itemId)?.productId)
      .filter((x): x is string => !!x),
  )

  const addProduct = (p: ProductDto) => {
    const step = p.unit === 'Weight' ? (p.weightStepGrams ?? 100) : 1
    setLines((ls) => [
      ...ls,
      { key: `new-${p.id}`, productId: p.id, name: p.name, unit: p.unit, unitPrice: p.price, quantity: p.unit === 'Weight' ? (p.minQuantity ?? step) : 1, step, max: p.unit === 'Weight' ? 10000 : 99 },
    ])
    setAdding(false)
    setSearch('')
  }

  const submit = () => {
    const v = validateEdit(lines)
    setProblem(v)
    setServerProblems([])
    if (!v) save.mutate()
  }

  return (
    <Modal title={`Изменить заказ № ${order.number}`} onClose={onClose} dismissible={!save.isPending}>
      <ul className="flex flex-col gap-3">
        {lines.map((l) => (
          <li key={l.key} className="rounded-xl border border-line bg-white p-3">
            <div className="flex justify-between gap-3">
              <div className="min-w-0">
                <p className="text-sm font-medium text-ink">{l.name}{!l.itemId && <span className="ml-2 text-[11px] font-semibold text-success bg-success-bg rounded-full px-2 py-0.5">новая</span>}</p>
                <p className="text-xs text-muted">{formatUnitPrice(l.unit, l.unitPrice)}{!l.itemId ? ' (цена каталога)' : ''}</p>
              </div>
              <button
                type="button"
                aria-label={`Убрать позицию: ${l.name}`}
                disabled={lines.length <= 1}
                title={lines.length <= 1 ? 'Последнюю позицию убрать нельзя — отклоните или отмените заказ' : undefined}
                className="w-8 h-8 rounded-full flex items-center justify-center text-ink-soft hover:bg-cream-deep disabled:opacity-30 disabled:pointer-events-none"
                onClick={() => setLines((ls) => ls.filter((x) => x.key !== l.key))}
              >
                <Icon name="trash" size={15} />
              </button>
            </div>
            <div className="mt-2 flex items-center gap-2">
              <button type="button" aria-label={`Меньше: ${l.name}`} disabled={l.quantity - l.step < 1} className="w-9 h-9 rounded-full border border-line flex items-center justify-center disabled:opacity-40" onClick={() => setQty(l.key, l.quantity - l.step)}>
                <Icon name="minus" size={15} />
              </button>
              <label className="sr-only" htmlFor={`qty-${l.key}`}>Количество: {l.name}</label>
              <input
                id={`qty-${l.key}`}
                inputMode="numeric"
                value={String(l.quantity)}
                onChange={(e) => {
                  const n = Number(e.target.value.replace(/\D/g, ''))
                  setQty(l.key, Number.isFinite(n) ? n : 0)
                }}
                className="w-24 text-center rounded-lg border border-line px-2 py-2 text-sm bg-white outline-none focus:border-gold"
              />
              <span className="text-xs text-ink-soft w-10">{l.unit === 'Weight' ? 'г' : 'шт'}</span>
              <button type="button" aria-label={`Больше: ${l.name}`} disabled={l.quantity + l.step > l.max} className="w-9 h-9 rounded-full border border-line flex items-center justify-center disabled:opacity-40" onClick={() => setQty(l.key, l.quantity + l.step)}>
                <Icon name="plus" size={15} />
              </button>
              <span className="ml-auto text-sm font-medium text-ink">{formatQuantity(l.unit, l.quantity)}</span>
            </div>
          </li>
        ))}
      </ul>

      {!adding ? (
        <Button type="button" variant="secondary" size="sm" className="mt-3" onClick={() => setAdding(true)}>
          <Icon name="plus" size={14} /> Добавить товар
        </Button>
      ) : (
        <div className="mt-3 rounded-xl border border-line bg-white p-3">
          <label htmlFor="edit-add-search" className="sr-only">Найти товар</label>
          <input id="edit-add-search" autoFocus type="search" placeholder="Найти товар" value={search} onChange={(e) => setSearch(e.target.value)} className="w-full rounded-lg border border-line px-3 py-2 text-sm bg-white outline-none focus:border-gold" />
          <div className="mt-2 max-h-48 overflow-y-auto">
            {products.isLoading ? (
              <p className="text-sm text-muted py-2">Загрузка…</p>
            ) : products.isError ? (
              <p role="alert" className="text-sm text-danger py-2">{getGoodsErrorMessage(products.error, 'Не удалось загрузить товары.')}</p>
            ) : (
              <ul>
                {filterProducts(products.data ?? [], search).map((p) => {
                  const taken = inOrder.has(p.id)
                  return (
                    <li key={p.id}>
                      <button type="button" disabled={taken} className="w-full text-left px-2 py-2 rounded-lg hover:bg-cream-deep disabled:opacity-40 flex justify-between gap-3 text-sm" onClick={() => addProduct(p)}>
                        <span className="truncate">{p.name}</span>
                        <span className="text-ink-soft shrink-0">{taken ? 'уже в заказе' : formatUnitPrice(p.unit, p.price)}</span>
                      </button>
                    </li>
                  )
                })}
              </ul>
            )}
          </div>
          <Button type="button" variant="ghost" size="sm" onClick={() => setAdding(false)}>Закрыть список</Button>
        </div>
      )}

      <div className="mt-4 flex flex-col gap-1.5">
        <label htmlFor="edit-comment" className="text-[13px] font-medium text-[#4A4038]">Комментарий для покупателя</label>
        <textarea id="edit-comment" rows={2} maxLength={500} value={comment} onChange={(e) => setComment(e.target.value)} placeholder="Например: сыра не осталось, заменили на другой" className="rounded-xl border border-line px-4 py-3 text-sm resize-none bg-white text-ink outline-none focus:border-gold" />
      </div>

      <div className="mt-4 rounded-xl bg-cream-deep px-4 py-3 flex items-baseline justify-between">
        <span className="text-sm text-ink-soft">Итого (предварительно)</span>
        <span className="font-serif text-xl text-ink">{formatMoney(preview.total, preview.isApproximate)}</span>
      </div>

      {problem && <div className="mt-3"><InlineError>{problem}</InlineError></div>}
      {serverProblems.length > 0 && (
        <ul className="mt-2 list-disc pl-5 text-sm text-danger">
          {serverProblems.map((p) => (
            <li key={p.productId}>{p.name}: {p.message}</li>
          ))}
        </ul>
      )}

      <div className="flex gap-3 mt-5">
        <Button variant="secondary" className="flex-1" onClick={onClose} disabled={save.isPending}>Отмена</Button>
        <Button className="flex-1" loading={save.isPending} disabled={!isEdited(order.items, lines, comment)} onClick={submit}>Сохранить</Button>
      </div>
    </Modal>
  )
}
