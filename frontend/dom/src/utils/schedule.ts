import type { ScheduleArrivalDto, ScheduleDayDto } from '../types'

/**
 * The schedule of cleanings and arrivals (`ViewSchedule`, US-37-18, API_CONTRACT_CYCLE37.md §37.31). One form for every role: it carries
 * no phone, no sums, no files, no requisites (ЮР-5) — what a housekeeper needs to prepare the house. The comment of the guest is `null`
 * unless the owner allowed it.
 */

/** «2 взр., 1 дет., доп. мест: 1, собак: 2, кроватка» — only the parts that apply. */
export function arrivalGuestsText(a: Pick<ScheduleArrivalDto, 'adults' | 'children' | 'extraBeds' | 'dogs' | 'needCot'>): string {
  const parts = [`${a.adults} взр.`]
  if (a.children > 0) parts.push(`${a.children} дет.`)
  if (a.extraBeds > 0) parts.push(`доп. мест: ${a.extraBeds}`)
  if (a.dogs > 0) parts.push(`собак: ${a.dogs}`)
  if (a.needCot) parts.push('кроватка')
  return parts.join(', ')
}

/** A day with nothing to do. */
export const isQuietDay = (d: Pick<ScheduleDayDto, 'arrivals' | 'departures'>): boolean => d.arrivals.length === 0 && d.departures.length === 0

/** Houses whose cleaning is tight the same day: a departure and an arrival in one house (the server flags both sides). */
export function turnoverHouses(d: Pick<ScheduleDayDto, 'arrivals' | 'departures'>): string[] {
  return [...new Set([...d.arrivals, ...d.departures].filter((x) => x.sameDayTurnover).map((x) => x.houseName))]
}

export const SCHEDULE_DAYS = 15
