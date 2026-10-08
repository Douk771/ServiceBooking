// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { BAR_CLASSES, barGeometry, barTone, clampDays, mergeBoard, rowsOf, shiftWindow } from './boardLayout'
import type { BoardItemDto, StaysBoardDto } from '../types'

const FROM = '2027-01-01'

describe('bar geometry (middle of the check-in day to the middle of the check-out day)', () => {
  it('a stay inside the window', () => {
    // check-in 5 Jan (column 4), check-out 8 Jan (column 7): from 4.5 to 7.5 = 3 nights
    expect(barGeometry({ startDate: '2027-01-05', endDate: '2027-01-08' }, FROM, 30)).toEqual({ left: 4.5, width: 3, clippedLeft: false, clippedRight: false })
  })

  it('a departure and an arrival on the same date do not overlap', () => {
    const a = barGeometry({ startDate: '2027-01-05', endDate: '2027-01-08' }, FROM, 30)!
    const b = barGeometry({ startDate: '2027-01-08', endDate: '2027-01-10' }, FROM, 30)!
    expect(a.left + a.width).toBe(b.left)
  })

  it('a stay that began before the window is clipped on the left', () => {
    expect(barGeometry({ startDate: '2026-12-29', endDate: '2027-01-04' }, FROM, 30)).toEqual({ left: 0, width: 3.5, clippedLeft: true, clippedRight: false })
  })

  it('a stay that runs past the window is clipped on the right', () => {
    const g = barGeometry({ startDate: '2027-01-28', endDate: '2027-02-05' }, FROM, 30)!
    expect(g.clippedRight).toBe(true)
    expect(g.left + g.width).toBe(30)
  })

  it('nothing outside the window', () => {
    expect(barGeometry({ startDate: '2026-12-20', endDate: '2026-12-31' }, FROM, 30)).toBeNull()
    expect(barGeometry({ startDate: '2027-01-31', endDate: '2027-02-03' }, FROM, 30)).toBeNull()
  })

  it('a stay whose check-out is the first day of the window shows the morning half of that day', () => {
    expect(barGeometry({ startDate: '2026-12-30', endDate: '2027-01-01' }, FROM, 30)).toEqual({ left: 0, width: 0.5, clippedLeft: true, clippedRight: false })
  })

  it('a stay that checks in on the last day of the window shows the evening half of it', () => {
    expect(barGeometry({ startDate: '2027-01-30', endDate: '2027-02-03' }, FROM, 30)).toEqual({ left: 29.5, width: 0.5, clippedLeft: false, clippedRight: true })
  })
})

describe('rows and polling', () => {
  const item = (id: string, houseId: string, startDate: string, endDate = '2027-01-10'): BoardItemDto => ({
    kind: 'Booking',
    id,
    houseId,
    startDate,
    endDate,
    state: 'Confirmed',
    stateText: 'Подтверждена',
    label: id,
    needsAction: false,
  })
  const board = (over: Partial<StaysBoardDto> = {}): StaysBoardDto => ({
    changed: true,
    revision: 5,
    serverTimeUtc: '2027-01-02T10:00:00Z',
    today: '2027-01-02',
    from: FROM,
    days: 30,
    houses: [
      { id: 'h1', name: 'Дом 1', isPublished: true, isArchived: false },
      { id: 'h2', name: 'Дом 2', isPublished: true, isArchived: false },
    ],
    items: [item('b2', 'h1', '2027-01-08'), item('b1', 'h1', '2027-01-03'), item('x', 'ghost', '2027-01-03'), item('b3', 'h2', '2027-01-05')],
    awaitingPaymentCount: 1,
    ...over,
  })

  it('one row per house in the server order, items by check-in, items of unknown houses dropped', () => {
    const rows = rowsOf(board())
    expect(rows.map((r) => r.house.id)).toEqual(['h1', 'h2'])
    expect(rows[0].items.map((i) => i.id)).toEqual(['b1', 'b2'])
    expect(rows.flatMap((r) => r.items.map((i) => i.id))).not.toContain('x')
  })

  it('an unchanged answer keeps the arrays of the last full one and takes the fresh revision, time and today', () => {
    const prev = board()
    const next: StaysBoardDto = { changed: false, revision: 5, serverTimeUtc: '2027-01-02T10:00:15Z', today: '2027-01-02' }
    const merged = mergeBoard(prev, next)
    expect(merged.items).toBe(prev.items)
    expect(merged.houses).toBe(prev.houses)
    expect(merged.serverTimeUtc).toBe('2027-01-02T10:00:15Z')
  })

  it('a changed answer replaces everything; an unchanged one with nothing before it cannot be applied but never throws', () => {
    const prev = board()
    const next = board({ revision: 6, items: [] })
    expect(mergeBoard(prev, next)).toBe(next)
    const lone: StaysBoardDto = { changed: false, revision: 1, serverTimeUtc: 'x', today: '2027-01-01' }
    expect(mergeBoard(undefined, lone)).toBe(lone)
  })

  it('window helpers', () => {
    expect(clampDays(3)).toBe(7)
    expect(clampDays(100)).toBe(62)
    expect(clampDays(30)).toBe(30)
    expect(shiftWindow('2027-01-01', 1)).toBe('2027-01-08')
    expect(shiftWindow('2027-01-01', -1)).toBe('2026-12-25')
  })

  it('every state has a tone and a style — never colour as the only signal (the label carries the state)', () => {
    for (const s of ['Held', 'AwaitingPaymentCheck', 'Confirmed', 'Block', 'External'] as const) expect(BAR_CLASSES[barTone(s)]).toBeTruthy()
    expect(barTone('AwaitingPaymentCheck')).toBe('awaiting')
  })
})
