import type { components } from '../types/api-cycle28.generated'

/** API_CONTRACT_CYCLE28.md §594.1 — `?showcase=` of the three admin lists (users, companies, billing accounts). */
export type ShowcaseFilter = components['parameters']['ShowcaseFilter']

export const SHOWCASE_FILTERS: { value: ShowcaseFilter; label: string }[] = [
  { value: 'all', label: 'Все' },
  { value: 'exclude', label: 'Без витрины' },
  { value: 'only', label: 'Только витрина' },
]

/** Filter value → `?showcase=`. «Все» is the server default, so nothing is sent (same convention as `kindParam`). */
export function showcaseParam(filter: ShowcaseFilter): ShowcaseFilter | undefined {
  return filter === 'all' ? undefined : filter
}

/** Label of the badge in the rows of the three admin lists. */
export const SHOWCASE_ROW_LABEL = 'Витрина'

interface ShowcaseCounters {
  showcaseCompanies?: number
  showcaseBookings?: number
}

function plural(n: number, one: string, few: string, many: string): string {
  const mod10 = n % 10
  const mod100 = n % 100
  if (mod10 === 1 && mod100 !== 11) return one
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20)) return few
  return many
}

/**
 * ARCHITECTURE_CYCLE28.md §578 — the dashboard's separate line «Витрина: N компаний, M записей». `null` when the
 * server sent no counters (an older server) or the showcase is empty — then the line is not rendered at all.
 */
export function formatShowcaseSummary(stats: ShowcaseCounters | undefined): string | null {
  const companies = stats?.showcaseCompanies
  const bookings = stats?.showcaseBookings
  if (companies === undefined || bookings === undefined) return null
  if (companies === 0 && bookings === 0) return null
  const c = `${companies.toLocaleString('ru-RU')} ${plural(companies, 'компания', 'компании', 'компаний')}`
  const b = `${bookings.toLocaleString('ru-RU')} ${plural(bookings, 'запись', 'записи', 'записей')}`
  return `Витрина: ${c}, ${b}`
}
