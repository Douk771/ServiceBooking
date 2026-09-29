import type { EditOrderLineInput, ProductUnit, StaffOrderItemDto } from '../types'
import { orderTotal } from './orderMoney'

/** One row of the edit dialog: an existing order line (`itemId`) or a product added in the dialog (`productId`). */
export interface EditLine {
  key: string
  itemId?: string
  productId?: string
  name: string
  unit: ProductUnit
  /** Snapshot price for existing lines, CURRENT catalogue price for new ones (§396.3). */
  unitPrice: number
  quantity: number
  step: number
  max: number
}

export function linesFromOrder(items: StaffOrderItemDto[]): EditLine[] {
  return items.map((i) => ({
    key: i.id,
    itemId: i.id,
    name: i.name,
    unit: i.unit,
    unitPrice: i.unitPrice,
    quantity: i.quantityOrdered,
    step: i.unit === 'Weight' ? (i.weightStepGrams ?? 100) : 1,
    max: i.unit === 'Weight' ? 10000 : 99,
  }))
}

/** The FULL desired composition for `PUT …/items` (§417): existing lines by `itemId`, new ones by `productId`. */
export function toEditPayload(lines: EditLine[]): EditOrderLineInput[] {
  return lines.map((l) => (l.itemId ? { itemId: l.itemId, quantity: l.quantity } : { productId: l.productId, quantity: l.quantity }))
}

export function previewEditTotal(lines: EditLine[]): { total: number; isApproximate: boolean } {
  return orderTotal(lines.map((l) => ({ unit: l.unit, price: l.unitPrice, quantity: l.quantity })))
}

/** Was anything actually changed? Avoids a pointless save (and a journal entry) on an untouched dialog. */
export function isEdited(original: StaffOrderItemDto[], lines: EditLine[], comment: string): boolean {
  if (comment.trim()) return true
  if (original.length !== lines.length) return true
  return lines.some((l) => !l.itemId || original.find((o) => o.id === l.itemId)?.quantityOrdered !== l.quantity)
}

/** Client-side guard for what the dialog itself can prevent; the server (`InvalidQuantity`) stays the authority. */
export function validateEdit(lines: EditLine[]): string | null {
  if (lines.length === 0) return 'Пустой заказ не бывает — отклоните или отмените заказ'
  for (const l of lines) {
    if (!Number.isInteger(l.quantity) || l.quantity <= 0) return `Укажите количество: ${l.name}`
    if (l.quantity > l.max) return `Слишком много: ${l.name}`
  }
  return null
}
