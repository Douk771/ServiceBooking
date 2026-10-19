// ARCHITECTURE_CYCLE44_PRODUCTS_LANDING.md §44.3, API_CONTRACT_CYCLE44 §44.22 — the single place in frontend/ that knows
// the public addresses of the five ezbook sites (and of the public demo stands, DEMO_SITE_URLS). Pure module (no browser or Node APIs): used by the SSR render of `about`,
// by the footers of the four SPAs and by tests. The production addresses are checked against `PublicSites` in
// ServiceBooking.API/appsettings.json by publicSites.consistency.test.ts.

export type SiteKey = 'zapis' | 'goods' | 'dom' | 'bani' | 'about'
export const SITE_KEYS: readonly SiteKey[] = ['zapis', 'goods', 'dom', 'bani', 'about']

/** Absolute origin without path, query, fragment and trailing slash, e.g. 'https://goods.ezbook.ru'. */
export type SiteOrigin = string
export type SiteUrls = Readonly<Record<SiteKey, SiteOrigin>>

export const PRODUCTION_SITE_URLS: SiteUrls = {
  zapis: 'https://ezbook.ru',
  goods: 'https://goods.ezbook.ru',
  dom: 'https://dom.ezbook.ru',
  bani: 'https://bani.ezbook.ru',
  about: 'https://about.ezbook.ru',
}

/**
 * Public demo stands with sample data (cycle 28: demo.visit, cycle 35: demo.zakaz). One stand per product that has it,
 * the same in every environment, so no VITE_SITE_URL_* override. Dom and bani have no demo.
 */
export const DEMO_SITE_URLS: Readonly<Partial<Record<Exclude<SiteKey, 'about'>, SiteOrigin>>> = {
  zapis: 'https://demo.visit.ezbook.ru',
  goods: 'https://demo.zakaz.ezbook.ru',
}

export const DEV_SITE_URLS: SiteUrls = {
  zapis: 'http://localhost:5173',
  goods: 'http://localhost:5174',
  dom: 'http://localhost:5175',
  bani: 'http://localhost:5176',
  about: 'http://localhost:5177',
}

export type SiteMode = 'production' | 'development'
export type SiteEnv = Readonly<Record<string, string | boolean | undefined>>

export class SiteUrlError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'SiteUrlError'
  }
}

/** 'zapis' -> 'VITE_SITE_URL_ZAPIS'. */
export function siteUrlEnvName(key: SiteKey): string {
  return `VITE_SITE_URL_${key.toUpperCase()}`
}

const LOCAL_HOSTS = new Set(['localhost', '127.0.0.1'])

function validateOrigin(key: SiteKey, raw: string, mode: SiteMode): SiteOrigin {
  const fail = (why: string): never => {
    throw new SiteUrlError(`${siteUrlEnvName(key)}: ${why} (got "${raw}")`)
  }
  let url: URL
  try {
    url = new URL(raw)
  } catch {
    return fail('not an absolute URL')
  }
  if (url.protocol !== 'http:' && url.protocol !== 'https:') fail('scheme must be http or https')
  if (url.username !== '' || url.password !== '') fail('credentials are not allowed')
  if (url.search !== '' || url.hash !== '' || raw.includes('?') || raw.includes('#')) fail('query and fragment are not allowed')
  if (url.pathname !== '/') fail('path is not allowed')
  if (raw.endsWith('/')) fail('trailing slash is not allowed')
  if (mode === 'production' && url.protocol === 'http:' && !LOCAL_HOSTS.has(url.hostname)) fail('production requires https')
  return url.origin
}

/** For every key: env[VITE_SITE_URL_<KEY>] (trimmed) when non-empty, otherwise the default of the mode. Throws SiteUrlError. */
export function resolveSiteUrls(env: SiteEnv, mode: SiteMode): SiteUrls {
  const defaults = mode === 'production' ? PRODUCTION_SITE_URLS : DEV_SITE_URLS
  const result = {} as Record<SiteKey, SiteOrigin>
  for (const key of SITE_KEYS) {
    const value = env[siteUrlEnvName(key)]
    const trimmed = typeof value === 'string' ? value.trim() : ''
    result[key] = trimmed === '' ? defaults[key] : validateOrigin(key, trimmed, mode)
  }
  return result
}

/** 'https://goods.ezbook.ru' -> 'goods.ezbook.ru' (port is kept). */
export function displayHost(origin: SiteOrigin): string {
  return new URL(origin).host
}

/**
 * Address of the "all products" page for the SPA footers, or null. Never throws.
 * dev -> always; otherwise only when VITE_PRODUCTS_PAGE_LINK === 'on'. Returns origin + '/'.
 */
export function productsPageHref(env: SiteEnv): string | null {
  try {
    if (env.DEV === true) return `${resolveSiteUrls(env, 'development').about}/`
    if (env.VITE_PRODUCTS_PAGE_LINK === 'on') return `${resolveSiteUrls(env, 'production').about}/`
    return null
  } catch {
    return null
  }
}
