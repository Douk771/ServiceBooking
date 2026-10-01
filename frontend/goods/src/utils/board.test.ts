// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { activeHighlights, applyBoardResponse, boardTitle, byPickupThenCreated, detectNewOrders, freshness, HIGHLIGHT_MS, isOverdueNow, patchOrder, serverNow, type BoardState } from './board'
import type { OrderBoardDto, StaffOrderCardDto } from '../types'

const card = (id: string, status: StaffOrderCardDto['status'], createdAtUtc = '2026-10-05T10:00:00Z', extra: Partial<StaffOrderCardDto> = {}): StaffOrderCardDto =>
  ({ id, status, createdAtUtc, number: 1, version: 1, availableActions: [], items: [], pickup: { kind: 'Asap', date: '2026-10-05', startUtc: createdAtUtc, dueUtc: createdAtUtc, text: 'Как можно скорее', isPreorder: false, isOverdue: false }, ...extra }) as unknown as StaffOrderCardDto

const ACCEPTING = { mode: 'Accepting', statusText: 'Принимаем заказы' } as const

const board = (over: Partial<BoardState> = {}): BoardState => ({
  revision: 1,
  businessDate: '2026-10-05',
  serverTimeUtc: '2026-10-05T10:05:00Z',
  clockOffsetMs: 0,
  acceptance: null,
  newOrders: [],
  accepted: [],
  ready: [],
  preorders: [],
  completedToday: [],
  ...over,
})

describe('applyBoardResponse', () => {
  it('replaces the columns on a changed answer', () => {
    const res: OrderBoardDto = { revision: 5, changed: true, businessDate: '2026-10-05', serverTimeUtc: '2026-10-05T10:05:00Z', acceptance: ACCEPTING, newOrders: [card('a', 'New')], accepted: [], ready: null, completedToday: undefined }
    const s = applyBoardResponse(null, res)
    expect(s.revision).toBe(5)
    expect(s.newOrders.map((o) => o.id)).toEqual(['a'])
    expect(s.ready).toEqual([])
  })
  it('keeps the previous columns when nothing changed, moving only revision and clock', () => {
    const prev = board({ newOrders: [card('a', 'New')] })
    const s = applyBoardResponse(prev, { revision: 1, changed: false, businessDate: '2026-10-05', serverTimeUtc: '2026-10-05T10:06:00Z', acceptance: { mode: 'Paused', statusText: 'Пауза до 13:30' } })
    expect(s.newOrders).toBe(prev.newOrders)
    expect(s.serverTimeUtc).toBe('2026-10-05T10:06:00Z')
    expect(s.acceptance?.mode).toBe('Paused') // acceptance rides on every answer, even `changed: false`
  })
})

describe('patchOrder', () => {
  it('moves a card between columns and keeps active columns oldest-first', () => {
    const s = board({ newOrders: [card('a', 'New', '2026-10-05T10:00:00Z')], accepted: [card('b', 'Accepted', '2026-10-05T09:00:00Z')] })
    const next = patchOrder(s, card('a', 'Accepted', '2026-10-05T10:00:00Z'))
    expect(next.newOrders).toEqual([])
    expect(next.accepted.map((o) => o.id)).toEqual(['b', 'a'])
  })
  it('sends terminal orders to «Завершённые сегодня», newest first', () => {
    const s = board({ completedToday: [card('x', 'Issued', '2026-10-05T08:00:00Z', { completedAtUtc: '2026-10-05T09:00:00Z' })] })
    const next = patchOrder(s, card('y', 'Rejected', '2026-10-05T08:30:00Z', { completedAtUtc: '2026-10-05T10:00:00Z' }))
    expect(next.completedToday.map((o) => o.id)).toEqual(['y', 'x'])
  })
})

describe('detectNewOrders (sound / highlight trigger)', () => {
  it('is silent on the very first answer', () => {
    const r = detectNewOrders(null, board({ newOrders: [card('a', 'New')] }), false)
    expect(r.fresh).toEqual([])
    expect([...r.seen]).toEqual(['a'])
  })
  it('flags ids that were not in any earlier answer', () => {
    const r = detectNewOrders(new Set(['a']), board({ newOrders: [card('a', 'New'), card('b', 'New')] }), false)
    expect(r.fresh).toEqual(['b'])
  })
  it('does not beep when an order the screen already knew moves into «Принятые»', () => {
    const r = detectNewOrders(new Set(['a']), board({ accepted: [card('a', 'Accepted')] }), true)
    expect(r.fresh).toEqual([])
  })
  it('counts a brand-new accepted order only under auto-accept', () => {
    const s = board({ accepted: [card('n', 'Accepted')] })
    expect(detectNewOrders(new Set(), s, false).fresh).toEqual([])
    expect(detectNewOrders(new Set(), s, true).fresh).toEqual(['n'])
  })
  it('remembers everything already seen', () => {
    const r = detectNewOrders(new Set(['old']), board({ newOrders: [card('b', 'New')] }), false)
    expect(r.seen.has('old') && r.seen.has('b')).toBe(true)
  })
})

describe('highlights, title, freshness', () => {
  it('expires a highlight after 60 seconds', () => {
    const h = new Map([['a', 1000], ['b', 1000 + HIGHLIGHT_MS - 1]])
    expect([...activeHighlights(h, 1000 + HIGHLIGHT_MS).keys()]).toEqual(['b'])
  })
  it('puts the count of new orders into the tab title', () => {
    expect(boardTitle(3, 'Шаурма на Ленина')).toBe('(3) Заказы — Шаурма на Ленина')
    expect(boardTitle(0, 'Шаурма')).toBe('Заказы — Шаурма')
  })
  it('turns stale after 30 seconds without a successful poll', () => {
    expect(freshness(null, 5000)).toEqual({ seconds: null, stale: false })
    expect(freshness(0, 30_000)).toEqual({ seconds: 30, stale: false })
    expect(freshness(0, 31_000)).toEqual({ seconds: 31, stale: true })
  })
})


describe('cycle 24: pick-up time on the board', () => {
  const pickup = (startUtc: string, date = '2026-10-05', dueUtc = startUtc) => ({ kind: 'Slot' as const, date, startUtc, dueUtc, text: 't', isPreorder: date > '2026-10-05', isOverdue: false })

  it('orders columns by pick-up time, then by creation', () => {
    const early = card('a', 'Accepted', '2026-10-05T10:30:00Z', { pickup: pickup('2026-10-05T11:00:00Z') })
    const late = card('b', 'Accepted', '2026-10-05T09:00:00Z', { pickup: pickup('2026-10-05T12:00:00Z') })
    const sameTimeOlder = card('c', 'Accepted', '2026-10-05T08:00:00Z', { pickup: pickup('2026-10-05T11:00:00Z') })
    expect([late, early, sameTimeOlder].sort(byPickupThenCreated).map((o) => o.id)).toEqual(['c', 'a', 'b'])
  })

  it('keeps the preorder groups from a full answer and counts them as known ids (no beep when they move to «Принятые»)', () => {
    const pre = card('p', 'Accepted', '2026-10-04T10:00:00Z', { pickup: pickup('2026-10-06T09:00:00Z', '2026-10-06') })
    const res: OrderBoardDto = { revision: 2, changed: true, businessDate: '2026-10-05', serverTimeUtc: '2026-10-05T10:05:00Z', acceptance: ACCEPTING, preorders: [{ date: '2026-10-06', label: 'Завтра', orders: [pre] }] }
    const s = applyBoardResponse(null, res)
    expect(s.preorders[0].orders.map((o) => o.id)).toEqual(['p'])
    const next = board({ businessDate: '2026-10-06', accepted: [{ ...pre, pickup: pickup('2026-10-06T09:00:00Z', '2026-10-06') }] })
    expect(detectNewOrders(new Set(['p']), next, true).fresh).toEqual([])
  })

  it('patchOrder puts a future-dated accepted order into its preorder group, and back out when it is cancelled', () => {
    const s = board({ newOrders: [card('n', 'New', undefined, { pickup: pickup('2026-10-07T09:00:00Z', '2026-10-07') })] })
    const accepted = card('n', 'Accepted', undefined, { pickup: pickup('2026-10-07T09:00:00Z', '2026-10-07') })
    const withPre = patchOrder(s, accepted)
    expect(withPre.newOrders).toEqual([])
    expect(withPre.accepted).toEqual([])
    expect(withPre.preorders.map((g) => [g.date, g.orders.map((o) => o.id)])).toEqual([['2026-10-07', ['n']]])
    const cancelled = patchOrder(withPre, { ...accepted, status: 'CancelledByShop' })
    expect(cancelled.preorders).toEqual([])
    expect(cancelled.completedToday.map((o) => o.id)).toEqual(['n'])
  })

  it('«Просрочен» is decided on the SERVER clock, only for active statuses', () => {
    const due = card('a', 'Accepted', undefined, { pickup: pickup('2026-10-05T11:00:00Z', '2026-10-05', '2026-10-05T11:10:00Z') })
    const server = (ms: number) => ms
    expect(isOverdueNow(due, server(new Date('2026-10-05T11:09:59Z').getTime()))).toBe(false)
    expect(isOverdueNow(due, server(new Date('2026-10-05T11:10:01Z').getTime()))).toBe(true)
    expect(isOverdueNow({ ...due, status: 'Issued' }, new Date('2026-10-05T12:00:00Z').getTime())).toBe(false)
    // a tablet running 10 minutes slow: client 11:00 + offset 10 min = server 11:10:xx
    const s = applyBoardResponse(null, { revision: 1, changed: true, businessDate: '2026-10-05', serverTimeUtc: '2026-10-05T11:10:00Z', acceptance: ACCEPTING }, new Date('2026-10-05T11:00:00Z').getTime())
    expect(serverNow(s, new Date('2026-10-05T11:00:30Z').getTime())).toBe(new Date('2026-10-05T11:10:30Z').getTime())
  })
})
