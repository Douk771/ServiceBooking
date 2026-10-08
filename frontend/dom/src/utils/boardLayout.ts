import type { BoardHouseDto, BoardItemDto, BoardItemState, StaysBoardDto } from '../types'
import { addDays, diffDays } from './stayDates'

/**
 * Geometry and polling logic of the board (шахматка, US-37-14, ARCHITECTURE_CYCLE37.md §37.7.2). A stay occupies the nights
 * `[startDate, endDate)`; on the grid its bar runs from the MIDDLE of the check-in day to the MIDDLE of the check-out day, so a departure
 * and an arrival on the same date sit side by side in one cell instead of looking like an overlap.
 */

export interface BarGeometry {
  /** Left edge in day columns (fractional: half a day for the middle of the check-in day). */
  left: number
  /** Width in day columns. */
  width: number
  clippedLeft: boolean
  clippedRight: boolean
}

/** The bar of an item inside the window `[from, from + days)`, or null when it does not touch the window. */
export function barGeometry(item: Pick<BoardItemDto, 'startDate' | 'endDate'>, from: string, days: number): BarGeometry | null {
  const start = diffDays(from, item.startDate) // column index of the check-in day (may be negative)
  const end = diffDays(from, item.endDate) // column index of the check-out day
  if (end < 0 || start >= days) return null // fully before or after the window; a check-out ON the first day still shows its morning half
  const clippedLeft = start < 0
  const clippedRight = end > days
  const left = clippedLeft ? 0 : start + 0.5
  const right = clippedRight ? days : end + 0.5 > days ? days : end + 0.5
  return { left, width: Math.max(0.25, right - left), clippedLeft, clippedRight }
}

export interface BoardRow {
  house: BoardHouseDto
  items: BoardItemDto[]
}

/** One row per house in the server's order, with its items in check-in order. Items of unknown houses are dropped, not guessed. */
export function rowsOf(board: Pick<StaysBoardDto, 'houses' | 'items'>): BoardRow[] {
  const items = board.items ?? []
  return (board.houses ?? []).map((house) => ({
    house,
    items: items.filter((i) => i.houseId === house.id).sort((a, b) => a.startDate.localeCompare(b.startDate) || a.endDate.localeCompare(b.endDate)),
  }))
}

/**
 * Applies a poll answer. `changed: false` carries no arrays — only a fresh revision, time and «today» — so the arrays of the last full
 * answer stay; a full answer replaces everything.
 */
export function mergeBoard(prev: StaysBoardDto | undefined, next: StaysBoardDto): StaysBoardDto {
  if (next.changed || !prev) return next
  return { ...prev, revision: next.revision, serverTimeUtc: next.serverTimeUtc, today: next.today, changed: true }
}

export const BOARD_POLL_MS = 15_000
export const BOARD_DEFAULT_DAYS = 30
export const BOARD_MIN_DAYS = 7
export const BOARD_MAX_DAYS = 62

export function clampDays(days: number): number {
  return Math.min(BOARD_MAX_DAYS, Math.max(BOARD_MIN_DAYS, Math.round(days)))
}

/** Next/previous window start: a week at a time, never before the company's «today» minus nothing (the past is read-only history, not hidden). */
export function shiftWindow(from: string, direction: -1 | 1, step = 7): string {
  return addDays(from, direction * step)
}

export type BarTone = 'held' | 'awaiting' | 'confirmed' | 'block' | 'external'

export function barTone(state: BoardItemState): BarTone {
  switch (state) {
    case 'Held':
      return 'held'
    case 'AwaitingPaymentCheck':
      return 'awaiting'
    case 'Confirmed':
      return 'confirmed'
    case 'Block':
      return 'block'
    case 'External':
      return 'external'
  }
}

export const BAR_CLASSES: Record<BarTone, string> = {
  held: 'border border-dashed border-warning bg-warning-bg text-warning',
  awaiting: 'border border-gold-dark bg-gold text-white shadow-soft',
  confirmed: 'border border-success/40 bg-success-bg text-success',
  block: 'border border-line-strong bg-cream-deep text-ink-soft',
  external: 'border border-info/40 bg-info-bg text-info',
}
