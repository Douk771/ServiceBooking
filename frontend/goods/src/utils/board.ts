import type { OrderBoardDto, OrderStatus, StaffOrderCardDto } from '../types'
import { isTerminalStatus } from './orderStatus'

/** The staff screen's view of the board: the last FULL answer, kept while polls say «no changes» (§415). */
export interface BoardState {
  revision: number
  businessDate: string
  serverTimeUtc: string
  newOrders: StaffOrderCardDto[]
  accepted: StaffOrderCardDto[]
  ready: StaffOrderCardDto[]
  completedToday: StaffOrderCardDto[]
}

/** Folds one `order-board` answer into the state. `changed: false` keeps the previous columns and only moves
 *  revision/clock (the arrays are null in that answer). */
export function applyBoardResponse(prev: BoardState | null, res: OrderBoardDto): BoardState {
  if (!res.changed && prev) {
    return { ...prev, revision: res.revision, businessDate: res.businessDate, serverTimeUtc: res.serverTimeUtc }
  }
  return {
    revision: res.revision,
    businessDate: res.businessDate,
    serverTimeUtc: res.serverTimeUtc,
    newOrders: res.newOrders ?? [],
    accepted: res.accepted ?? [],
    ready: res.ready ?? [],
    completedToday: res.completedToday ?? [],
  }
}

const byCreatedAsc = (a: StaffOrderCardDto, b: StaffOrderCardDto) => new Date(a.createdAtUtc).getTime() - new Date(b.createdAtUtc).getTime()

function columnFor(status: OrderStatus): 'newOrders' | 'accepted' | 'ready' | 'completedToday' {
  switch (status) {
    case 'New':
      return 'newOrders'
    case 'Accepted':
      return 'accepted'
    case 'Ready':
      return 'ready'
    default:
      return isTerminalStatus(status) ? 'completedToday' : 'newOrders'
  }
}

/** Puts a fresh card (from an action answer or a 409 body) into the right column, replacing any older copy. */
export function patchOrder(state: BoardState, order: StaffOrderCardDto): BoardState {
  const stripped = {
    ...state,
    newOrders: state.newOrders.filter((o) => o.id !== order.id),
    accepted: state.accepted.filter((o) => o.id !== order.id),
    ready: state.ready.filter((o) => o.id !== order.id),
    completedToday: state.completedToday.filter((o) => o.id !== order.id),
  }
  const col = columnFor(order.status)
  const merged = [...stripped[col], order]
  stripped[col] = col === 'completedToday' ? merged.sort((a, b) => new Date(b.completedAtUtc ?? b.createdAtUtc).getTime() - new Date(a.completedAtUtc ?? a.createdAtUtc).getTime()) : merged.sort(byCreatedAsc)
  return stripped
}

export function allIds(state: BoardState): string[] {
  return [...state.newOrders, ...state.accepted, ...state.ready, ...state.completedToday].map((o) => o.id)
}

/**
 * Orders worth a sound and a highlight (§397.3): ids that were not in ANY earlier answer and sit in «Новые» —
 * or in «Принятые» when the shop auto-accepts. `seen === null` is the first answer: everything already on
 * the board is baseline, nothing beeps on opening the screen.
 */
export function detectNewOrders(seen: ReadonlySet<string> | null, state: BoardState, autoAccept: boolean): { fresh: string[]; seen: Set<string> } {
  const nextSeen = new Set(allIds(state))
  if (seen === null) return { fresh: [], seen: nextSeen }
  const candidates = autoAccept ? [...state.newOrders, ...state.accepted] : state.newOrders
  const fresh = candidates.filter((o) => !seen.has(o.id)).map((o) => o.id)
  seen.forEach((id) => nextSeen.add(id))
  return { fresh, seen: nextSeen }
}

export const HIGHLIGHT_MS = 60_000

/** Highlight lives until the first action over the order or 60 s, whichever comes first. */
export function activeHighlights(highlights: ReadonlyMap<string, number>, now: number): Map<string, number> {
  const out = new Map<string, number>()
  highlights.forEach((at, id) => {
    if (now - at < HIGHLIGHT_MS) out.set(id, at)
  })
  return out
}

/** «(3) Заказы — Шаурма на Ленина»; no prefix at zero. */
export function boardTitle(attentionCount: number, shopName: string): string {
  return `${attentionCount > 0 ? `(${attentionCount}) ` : ''}Заказы — ${shopName}`
}

/** Seconds since the last successful poll, and whether the screen should shout «Нет связи» (> 30 s, §397.1). */
export function freshness(lastOkAt: number | null, now: number): { seconds: number | null; stale: boolean } {
  if (lastOkAt === null) return { seconds: null, stale: false }
  const seconds = Math.max(0, Math.floor((now - lastOkAt) / 1000))
  return { seconds, stale: seconds > 30 }
}
