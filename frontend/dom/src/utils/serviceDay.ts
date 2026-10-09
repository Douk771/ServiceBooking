import type { ServiceDayAxisDto, ServiceDayBarDto } from '../types'
import { clockOf, MINUTES_PER_DAY } from './businessClock'

/**
 * Geometry of «День услуг» (ARCHITECTURE_CYCLE39.md §39.10, API_CONTRACT_CYCLE39.md §39.30.2): the axis runs in minutes of the business
 * day (360…1800); a bar is placed by its minutes, so a session that crosses midnight stays ONE bar and the midnight is only a mark on the
 * scale (`axis.midnightMinute`). The state of a bar is in its text; the colour only supports it.
 */

export interface BarBox {
  /** Percent of the axis width. */
  leftPct: number
  widthPct: number
  /** The bar was cut by the edge of the axis (a buffer that goes on past 06:00 of the next day). */
  clippedLeft: boolean
  clippedRight: boolean
}

export function barBox(axis: Pick<ServiceDayAxisDto, 'fromMinute' | 'toMinute'>, startMinute: number, endMinute: number): BarBox {
  const span = Math.max(1, axis.toMinute - axis.fromMinute)
  const s = Math.max(axis.fromMinute, startMinute)
  const e = Math.min(axis.toMinute, Math.max(endMinute, s))
  return {
    leftPct: ((s - axis.fromMinute) / span) * 100,
    widthPct: ((e - s) / span) * 100,
    clippedLeft: startMinute < axis.fromMinute,
    clippedRight: endMinute > axis.toMinute,
  }
}

export interface AxisTick {
  minute: number
  label: string
  leftPct: number
  isMidnight: boolean
}

/** A tick per full hour; `step` hours apart (every 2 h on a narrow scale). */
export function axisTicks(axis: ServiceDayAxisDto, stepHours = 1): AxisTick[] {
  const span = Math.max(1, axis.toMinute - axis.fromMinute)
  const first = Math.ceil(axis.fromMinute / 60) * 60
  const out: AxisTick[] = []
  for (let m = first; m <= axis.toMinute; m += 60 * stepHours) {
    out.push({ minute: m, label: clockOf(m), leftPct: ((m - axis.fromMinute) / span) * 100, isMidnight: m === MINUTES_PER_DAY })
  }
  return out
}

/** Bars of one service in time order; sessions before buffers when they start together. */
export function sortedBars(bars: readonly ServiceDayBarDto[]): ServiceDayBarDto[] {
  const rank = (b: ServiceDayBarDto) => (b.kind === 'Session' ? 0 : 1)
  return [...bars].sort((a, b) => a.startMinute - b.startMinute || rank(a) - rank(b))
}

/** «16:00 – 18:00» for the list view of a bar (the phone), after-midnight times are marked in the label itself. */
export function barTimeText(b: Pick<ServiceDayBarDto, 'startMinute' | 'endMinute'>): string {
  const mark = (m: number) => (m >= MINUTES_PER_DAY ? `${clockOf(m)} (ночь)` : clockOf(m))
  return `${mark(b.startMinute)} – ${mark(b.endMinute)}`
}
