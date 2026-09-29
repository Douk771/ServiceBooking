import routes from '../../../../contracts/cycle23/goods-routes.json'

/**
 * Advisory client-side check of the shop address (goods-routes.json is the single source, §390). The
 * server has the last word (`SlugInvalid` / `SlugReserved` / `SlugTaken`), and `GET /shops/slug-check`
 * says whether it is free — this only spares a request for an obviously malformed value.
 */
const pattern = new RegExp(routes.slugPattern)

export function isSlugFormatValid(slug: string): boolean {
  return slug.length >= routes.slugMinLength && slug.length <= routes.slugMaxLength && pattern.test(slug)
}

/** Lowercase, drop characters the address cannot hold; the transliteration is the server's job. */
export function normalizeSlugInput(raw: string): string {
  return raw
    .toLowerCase()
    .replace(/[^a-z0-9-]+/g, '-')
    .replace(/-{2,}/g, '-')
    .replace(/^-+/, '')
}
