import type { DayOfWeek, WorkingDayDto, WorkingHoursInput } from '../types'
import { ALL_WEEKDAYS } from './weekdays'

/**
 * Working-hours form model (API_CONTRACT_CYCLE24.md §473.2). Only FORM concerns live here: the fields are «HH:mm»
 * on a 5-minute grid and at most three intervals per day. The intersection / midnight rules are the server's (its 400
 * texts are shown as-is) — the frontend does not reproduce them.
 */
export interface EditableInterval {
  start: string
  end: string
}
export type EditableWeek = Record<DayOfWeek, EditableInterval[]>

export const MAX_INTERVALS_PER_DAY = 3
export const DEFAULT_INTERVAL: EditableInterval = { start: '09:00', end: '21:00' }

export function emptyWeek(): EditableWeek {
  return Object.fromEntries(ALL_WEEKDAYS.map((d) => [d, []])) as unknown as EditableWeek
}

export function weekFromDto(days: readonly WorkingDayDto[]): EditableWeek {
  const week = emptyWeek()
  for (const d of days) week[d.dayOfWeek] = d.intervals.map((i) => ({ start: i.start, end: i.end }))
  return week
}

export function weekToInput(week: EditableWeek): WorkingHoursInput {
  return { days: ALL_WEEKDAYS.map((d) => ({ dayOfWeek: d, intervals: week[d].map((i) => ({ start: i.start, end: i.end })) })) }
}

/** `end <= start` means «через полночь» (the server's rule, shown to the owner as a hint). */
export function crossesMidnight(i: EditableInterval): boolean {
  return isValidTime(i.start) && isValidTime(i.end) && i.end <= i.start && i.end !== i.start
}

export function isValidTime(value: string): boolean {
  const m = /^([01]\d|2[0-3]):([0-5]\d)$/.exec(value)
  return !!m
}

/** A field problem in the server's own wording, or null. */
export function intervalFieldError(i: EditableInterval): string | null {
  if (!isValidTime(i.start) || !isValidTime(i.end)) return 'Укажите время в формате ЧЧ:ММ'
  if (Number(i.start.slice(3)) % 5 !== 0 || Number(i.end.slice(3)) % 5 !== 0) return 'Время указывается с шагом 5 минут'
  if (i.start === i.end) return 'Интервал не может быть нулевой длины'
  return null
}

export function firstWeekError(week: EditableWeek): string | null {
  for (const d of ALL_WEEKDAYS) {
    if (week[d].length > MAX_INTERVALS_PER_DAY) return 'В дне не больше трёх интервалов'
    for (const i of week[d]) {
      const e = intervalFieldError(i)
      if (e) return e
    }
  }
  return null
}

/** Copies one day's intervals onto the chosen days («применить к будням»). */
export function applyDayTo(week: EditableWeek, from: DayOfWeek, to: readonly DayOfWeek[]): EditableWeek {
  const next = { ...week }
  for (const d of to) if (d !== from) next[d] = week[from].map((i) => ({ ...i }))
  return next
}

export function nextInterval(existing: readonly EditableInterval[]): EditableInterval {
  const last = existing[existing.length - 1]
  if (!last) return { ...DEFAULT_INTERVAL }
  return { start: '', end: '' } // the owner fills the next one — a guessed time would silently overlap
}
