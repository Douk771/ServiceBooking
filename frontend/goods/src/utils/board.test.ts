import { describe, it, expect } from 'vitest'
import { activeHighlights, applyBoardResponse, boardTitle, detectNewOrders, freshness, HIGHLIGHT_MS, patchOrder, type BoardState } from './board'
import type { OrderBoardDto, StaffOrderCardDto } from '../types'

const card = (id: string, status: StaffOrderCardDto['status'], createdAtUtc = '2026-10-05T10:00:00Z', extra: Partial<StaffOrderCardDto> = {}): StaffOrderCardDto =>
  ({ id, status, createdAtUtc, number: 1, version: 1, availableActions: [], items: [], ...extra }) as unknown as StaffOrderCardDto

const board = (over: Partial<BoardState> = {}): BoardState => ({
  revision: 1,
  businessDate: '2026-10-05',
  serverTimeUtc: '2026-10-05T10:05:00Z',
  newOrders: [],
  accepted: [],
  ready: [],
  completedToday: [],
  ...over,
})

describe('applyBoardResponse', () => {
  it('replaces the columns on a changed answer', () => {
    const res: OrderBoardDto = { revision: 5, changed: true, businessDate: '2026-10-05', serverTimeUtc: 't', newOrders: [card('a', 'New')], accepted: [], ready: null, completedToday: undefined }
    const s = applyBoardResponse(null, res)
    expect(s.revision).toBe(5)
    expect(s.newOrders.map((o) => o.id)).toEqual(['a'])
    expect(s.ready).toEqual([])
  })
  it('keeps the previous columns when nothing changed, moving only revision and clock', () => {
    const prev = board({ newOrders: [card('a', 'New')] })
    const s = applyBoardResponse(prev, { revision: 1, changed: false, businessDate: '2026-10-05', serverTimeUtc: 'later' })
    expect(s.newOrders).toBe(prev.newOrders)
    expect(s.serverTimeUtc).toBe('later')
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
