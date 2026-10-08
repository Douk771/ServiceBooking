import type { CatalogQuery } from '../api/publicStays'
import { isIsoDate, nightsBetween } from './stayDates'

/**
 * The catalog filters live in the URL, so a result list can be shared (SPEC US-37-05; `catalogQueryParams` of
 * contracts/cycle37/dom-routes.json: checkIn, checkOut, guests, maxPrice, page). The page URL calls the price filter `maxPrice`;
 * the API parameter is `maxPricePerNight`.
 */
export interface CatalogFilters {
  checkIn: string | null
  checkOut: string | null
  guests: number
  maxPrice: number | null
  page: number
}

export const DEFAULT_GUESTS = 1
export const MAX_GUESTS = 60
export const CATALOG_PAGE_SIZE = 12

function positiveInt(raw: string | null, max: number): number | null {
  if (raw === null || !/^\d+$/.test(raw.trim())) return null
  const n = Number(raw)
  return n >= 1 && n <= max ? n : null
}

/** Reads filters tolerantly: a malformed value falls back to «not set» instead of breaking the page. Dates are both-or-none. */
export function parseCatalogFilters(sp: URLSearchParams): CatalogFilters {
  const inRaw = sp.get('checkIn')
  const outRaw = sp.get('checkOut')
  const datesOk = !!inRaw && !!outRaw && isIsoDate(inRaw) && isIsoDate(outRaw) && nightsBetween(inRaw, outRaw) > 0
  return {
    checkIn: datesOk ? inRaw : null,
    checkOut: datesOk ? outRaw : null,
    guests: positiveInt(sp.get('guests'), MAX_GUESTS) ?? DEFAULT_GUESTS,
    maxPrice: positiveInt(sp.get('maxPrice'), 10_000_000),
    page: positiveInt(sp.get('page'), 10_000) ?? 1,
  }
}

/** Filters → the URL's query string; defaults are omitted so the plain catalog stays `/`. */
export function toSearchParams(f: CatalogFilters): URLSearchParams {
  const sp = new URLSearchParams()
  if (f.checkIn && f.checkOut) {
    sp.set('checkIn', f.checkIn)
    sp.set('checkOut', f.checkOut)
  }
  if (f.guests !== DEFAULT_GUESTS) sp.set('guests', String(f.guests))
  if (f.maxPrice) sp.set('maxPrice', String(f.maxPrice))
  if (f.page > 1) sp.set('page', String(f.page))
  return sp
}

export function toApiQuery(f: CatalogFilters, pageSize = CATALOG_PAGE_SIZE): CatalogQuery {
  const q: CatalogQuery = { guests: f.guests, page: f.page, pageSize }
  if (f.checkIn && f.checkOut) {
    q.checkIn = f.checkIn
    q.checkOut = f.checkOut
  }
  if (f.maxPrice) q.maxPricePerNight = f.maxPrice
  return q
}

/** True when any filter narrows the list (drives the «Сбросить» button and the empty-state wording). */
export function hasActiveFilters(f: CatalogFilters): boolean {
  return !!(f.checkIn && f.checkOut) || f.guests !== DEFAULT_GUESTS || f.maxPrice !== null
}

/**
 * Link to a house page carrying the chosen dates and guests on (`houseQueryParams`: checkIn, checkOut, adults, children).
 * `path` is the server's relative `/<companySlug>/<houseSlug>`.
 */
export function houseLink(path: string, f: Pick<CatalogFilters, 'checkIn' | 'checkOut' | 'guests'>): string {
  const sp = new URLSearchParams()
  if (f.checkIn && f.checkOut) {
    sp.set('checkIn', f.checkIn)
    sp.set('checkOut', f.checkOut)
  }
  if (f.guests > 1) sp.set('adults', String(f.guests))
  const qs = sp.toString()
  return qs ? `${path}?${qs}` : path
}
