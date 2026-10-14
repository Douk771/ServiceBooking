import routes from '../../../../contracts/cycle39/dom-routes.json'

/**
 * Advisory client-side check of an address (dom-routes.json is the single source, ARCHITECTURE_CYCLE39.md §39.14.1). The server has the last
 * word (`SlugInvalid` / `SlugReserved` / `SlugTaken`) and `GET /api/stays/slug-check` says whether the company address is free — this only
 * spares a request for an obviously malformed value.
 */
const companyPattern = new RegExp(routes.slugPattern)
const housePattern = new RegExp(routes.houseSlugPattern)
const servicePattern = new RegExp(routes.serviceSlugPattern)
const reservedHouse = new Set<string>(routes.reservedHouseSlugs)

export const COMPANY_SLUG_MIN = routes.slugMinLength
export const COMPANY_SLUG_MAX = routes.slugMaxLength
export const HOUSE_SLUG_MIN = routes.houseSlugMinLength
export const HOUSE_SLUG_MAX = routes.houseSlugMaxLength

export function isCompanySlugValid(slug: string): boolean {
  return slug.length >= COMPANY_SLUG_MIN && slug.length <= COMPANY_SLUG_MAX && companyPattern.test(slug)
}

export const SERVICE_SLUG_MIN = routes.serviceSlugMinLength
export const SERVICE_SLUG_MAX = routes.serviceSlugMaxLength

/** A house address cannot equal a word of the second URL segment that other pages use (`uslugi`, calendars of cycle 40). */
export function isHouseSlugReserved(slug: string): boolean {
  return reservedHouse.has(slug)
}

export function isServiceSlugValid(slug: string): boolean {
  return slug.length >= SERVICE_SLUG_MIN && slug.length <= SERVICE_SLUG_MAX && servicePattern.test(slug)
}

export function isHouseSlugValid(slug: string): boolean {
  return slug.length >= HOUSE_SLUG_MIN && slug.length <= HOUSE_SLUG_MAX && housePattern.test(slug)
}

/** Lowercase, drop characters an address cannot hold; the transliteration of a name is the server's job. */
export function normalizeSlugInput(raw: string): string {
  return raw
    .toLowerCase()
    .replace(/[^a-z0-9-]+/g, '-')
    .replace(/-{2,}/g, '-')
    .replace(/^-+/, '')
}

export const COMPANY_SLUG_FORMAT_TEXT = `Адрес — латиница, цифры и дефис, от ${COMPANY_SLUG_MIN} до ${COMPANY_SLUG_MAX} символов`
export const HOUSE_SLUG_FORMAT_TEXT = `Адрес дома — латиница, цифры и дефис, ${HOUSE_SLUG_MIN}–${HOUSE_SLUG_MAX} символов`
export const HOUSE_SLUG_RESERVED_TEXT = 'Этот адрес зарезервирован — выберите другой'
export const SERVICE_SLUG_FORMAT_TEXT = `Адрес услуги — латиница, цифры и дефис, ${SERVICE_SLUG_MIN}–${SERVICE_SLUG_MAX} символов`
