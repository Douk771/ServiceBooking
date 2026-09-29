import type { OrderBoardDto, OrderStatus, PreorderGroupDto, ShopAcceptanceDto, StaffOrderCardDto } from '../types'
import { isTerminalStatus } from './orderStatus'

/** The staff screen's view of the board: the last FULL answer, kept while polls say «no changes» (§415). */
export interface BoardState {
  revision: number
  businessDate: string
  serverTimeUtc: string
  /** `serverTime − clientTime` at the moment the answer arrived: a tablet with a wrong clock must not lie about «Просрочен» (§480). */
  clockOffsetMs: number
  acceptance: ShopAcceptanceDto | null
  newOrders: StaffOrderCardDto[]
  accepted: StaffOrderCardDto[]
  ready: StaffOrderCardDto[]
  /** `Accepted` with a pick-up date after today, grouped by date ascending (API_CONTRACT_CYCLE24.md §480). */
  preorders: PreorderGroupDto[]
  completedToday: StaffOrderCardDto[]
}

/** Folds one `order-board` answer into the state. `changed: false` keeps the previous columns and only moves
 *  revision/clock (the arrays are null in that answer). */
export function applyBoardResponse(prev: BoardState | null, res: OrderBoardDto, nowMs: number = Date.now()): BoardState {
  const clockOffsetMs = new Date(res.serverTimeUtc).getTime() - nowMs
  const offset = Number.isFinite(clockOffsetMs) ? clockOffsetMs : (prev?.clockOffsetMs ?? 0)
  if (!res.changed && prev) {
    // `acceptance` comes in EVERY answer, `changed: false` included (§480).
    return { ...prev, revision: res.revision, businessDate: res.businessDate, serverTimeUtc: res.serverTimeUtc, clockOffsetMs: offset, acceptance: res.acceptance }
  }
  return {
    revision: res.revision,
    businessDate: res.businessDate,
    serverTimeUtc: res.serverTimeUtc,
    clockOffsetMs: offset,
    acceptance: res.acceptance,
    newOrders: res.newOrders ?? [],
    accepted: res.accepted ?? [],
    ready: res.ready ?? [],
    preorders: res.preorders ?? [],
    completedToday: res.completedToday ?? [],
  }
}

/** Inside a column: by pick-up time, then by creation (API_CONTRACT_CYCLE24.md §480). */
export const byPickupThenCreated = (a: StaffOrderCardDto, b: StaffOrderCardDto) =>
  new Date(a.pickup.startUtc).getTime() - new Date(b.pickup.startUtc).getTime() ||
  new Date(a.createdAtUtc).getTime() - new Date(b.createdAtUtc).getTime()

const ACTIVE: ReadonlySet<OrderStatus> = new Set<OrderStatus>(['New', 'Accepted', 'Ready'])

/**
 * «Просрочен» — the ONLY thing the frontend computes about time (§490): the server's clock is past `pickup.dueUtc` while
 * the order is still New/Accepted/Ready. Between full answers this is re-evaluated on every tick.
 */
export function isOverdueNow(order: Pick<StaffOrderCardDto, 'status' | 'pickup'>, serverNowMs: number): boolean {
  return ACTIVE.has(order.status) && serverNowMs > new Date(order.pickup.dueUtc).getTime()
}

export function serverNow(state: Pick<BoardState, 'clockOffsetMs'>, nowMs: number = Date.now()): number {
  return nowMs + state.clockOffsetMs
}

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
  // A future-dated accepted order lives in «Предзаказы», not in the day's «Принятые» (§480).
  const preorders = stripped.preorders.map((g) => ({ ...g, orders: g.orders.filter((o) => o.id !== order.id) })).filter((g) => g.orders.length > 0)
  if (order.status === 'Accepted' && order.pickup.date > state.businessDate) {
    const group = preorders.find((g) => g.date === order.pickup.date)
    if (group) group.orders = [...group.orders, order].sort(byPickupThenCreated)
    else preorders.push({ date: order.pickup.date, label: order.pickup.text.split(',')[0], orders: [order] })
    preorders.sort((a, b) => a.date.localeCompare(b.date))
    return { ...stripped, preorders }
  }
  stripped.preorders = preorders
  const col = columnFor(order.status)
  const merged = [...stripped[col], order]
  stripped[col] = col === 'completedToday' ? merged.sort((a, b) => new Date(b.completedAtUtc ?? b.createdAtUtc).getTime() - new Date(a.completedAtUtc ?? a.createdAtUtc).getTime()) : merged.sort(byPickupThenCreated)
  return stripped
}

export function allIds(state: BoardState): string[] {
  return [...state.newOrders, ...state.accepted, ...state.ready, ...state.preorders.flatMap((g) => g.orders), ...state.completedToday].map((o) => o.id)
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
