import type { CatalogQuery } from '../api/bathsPublic'

/**
 * The catalog filters live in the URL, so a result list can be shared (`catalogQueryParams` of contracts/cycle42/bani-routes.json:
 * city, date, page). The URL calls the city `city`; the API parameter is `cityId`.
 */
export interface CatalogFilters {
  cityId: number | null
  /** YYYY-MM-DD, P1. */
  date: string | null
  page: number
}

export const CATALOG_PAGE_SIZE = 12

export function isIsoDate(s: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(s)) return false
  const d = new Date(`${s}T00:00:00Z`)
  return !Number.isNaN(d.getTime()) && d.toISOString().slice(0, 10) === s
}

function positiveInt(raw: string | null, max: number): number | null {
  if (raw === null || !/^\d+$/.test(raw.trim())) return null
  const n = Number(raw)
  return n >= 1 && n <= max ? n : null
}

/** Reads filters tolerantly: a malformed value falls back to «not set» instead of breaking the page. */
export function parseCatalogFilters(sp: URLSearchParams): CatalogFilters {
  const date = sp.get('date')
  return {
    cityId: positiveInt(sp.get('city'), 2_000_000_000),
    date: date && isIsoDate(date) ? date : null,
    page: positiveInt(sp.get('page'), 10_000) ?? 1,
  }
}

/** Filters → the URL's query string; defaults are omitted so the plain catalog stays `/`. */
export function toSearchParams(f: CatalogFilters): URLSearchParams {
  const sp = new URLSearchParams()
  if (f.cityId) sp.set('city', String(f.cityId))
  if (f.date) sp.set('date', f.date)
  if (f.page > 1) sp.set('page', String(f.page))
  return sp
}

export function toApiQuery(f: CatalogFilters, pageSize = CATALOG_PAGE_SIZE): CatalogQuery {
  const q: CatalogQuery = { page: f.page, pageSize }
  if (f.cityId) q.cityId = f.cityId
  if (f.date) q.date = f.date
  return q
}

/** True when any filter narrows the list (drives «Сбросить» and the empty-state wording). */
export function hasActiveFilters(f: CatalogFilters): boolean {
  return f.cityId !== null || f.date !== null
}

/** Link to a resource page: the chosen date goes on as `date` (`resourceQueryParams`). `path` is the server's `/<companySlug>/<resourceSlug>`. */
export function resourceLink(path: string, f: Pick<CatalogFilters, 'date'>): string {
  return f.date ? `${path}?date=${f.date}` : path
}
