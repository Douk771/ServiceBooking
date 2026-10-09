import routes from '../../../../contracts/cycle42/bani-routes.json'

/**
 * Advisory client-side check of the address of a «Бани» company (`bani-routes.json` is the single source, ARCHITECTURE_CYCLE42.md §42.10.1).
 * The server has the last word (`SlugInvalid` / `SlugReserved` / `SlugTaken`), and `GET /api/baths/slug-check` says whether the address
 * is free — this only spares a request for an obviously malformed or reserved value.
 */
const pattern = new RegExp(routes.slugPattern)
const reserved = new Set<string>(routes.reservedSlugs)

export const BATHS_SLUG_MIN = routes.slugMinLength
export const BATHS_SLUG_MAX = routes.slugMaxLength

export function isBathsSlugFormatValid(slug: string): boolean {
  return slug.length >= BATHS_SLUG_MIN && slug.length <= BATHS_SLUG_MAX && pattern.test(slug)
}

export function isBathsSlugReserved(slug: string): boolean {
  return reserved.has(slug.toLowerCase())
}

export const BATHS_SLUG_FORMAT_TEXT = `Адрес — латиница, цифры и дефис, от ${BATHS_SLUG_MIN} до ${BATHS_SLUG_MAX} символов`
export const BATHS_SLUG_RESERVED_TEXT = 'Это слово занято сервисом — выберите другой адрес'

/** What the form says about a typed address before asking the server; null when it looks fine (or is empty). */
export function slugLocalProblem(slug: string): string | null {
  if (slug === '') return null
  if (!isBathsSlugFormatValid(slug)) return BATHS_SLUG_FORMAT_TEXT
  if (isBathsSlugReserved(slug)) return BATHS_SLUG_RESERVED_TEXT
  return null
}

/** Lowercase, drop characters an address cannot hold; the transliteration of a name is the server's job. */
export function normalizeSlugInput(raw: string): string {
  return raw
    .toLowerCase()
    .replace(/[^a-z0-9-]+/g, '-')
    .replace(/-{2,}/g, '-')
    .replace(/^-+/, '')
}
