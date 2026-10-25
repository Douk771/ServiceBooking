// ARCHITECTURE_CYCLE48_STOREFRONT.md §48.3, §48.4, API_CONTRACT_CYCLE48_STOREFRONT.md §48.21, §48.23 — the ONE place in frontend/
// that knows the public requisites of the operator. Pure module (no React, no browser or Node APIs): it is imported by the four SPAs,
// by `about` and by the SSR prerender. The values live in contracts/legal/operator-public.json; no other source file may contain them
// (guard test CY48-02).
import data from '../../../contracts/legal/operator-public.json'
import type { SiteUrls } from '@/utils/publicSites'

export type OperatorKey = keyof typeof data.values
export type StorefrontSite = 'zapis' | 'goods' | 'dom' | 'bani'

export const OPERATOR: Readonly<Record<OperatorKey, string>> = data.values

/** Keys that are also in legal.values.json (compared as a whole by deploy precheck and `legal publish`). */
export const SHARED_WITH_LEGAL_VALUES: readonly string[] = data.sharedWithLegalValues

export class OperatorTemplateError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'OperatorTemplateError'
  }
}

const TEL_RE = /^\+7\d{10}$/

/** 'tel:+79132258915' — '+' and every digit of ТЕЛЕФОН_ОПЕРАТОРА (the derived key ТЕЛЕФОН_TEL, never stored). */
export function telHref(): string {
  return `tel:${telNumber()}`
}

function telNumber(): string {
  const number = `+${OPERATOR['ТЕЛЕФОН_ОПЕРАТОРА'].replace(/\D/g, '')}`
  if (!TEL_RE.test(number)) throw new OperatorTemplateError(`ТЕЛЕФОН_TEL: "${number}" does not match ${TEL_RE.source}`)
  return number
}

const PLACEHOLDER_RE = /\{\{([^{}]*)\}\}/g

/** Replaces every {{KEY}} (and {{ТЕЛЕФОН_TEL}}). An unknown key throws: a template typo must fail a test, not reach the page. */
export function fillOperatorTemplate(template: string): string {
  return template.replace(PLACEHOLDER_RE, (_match, key: string) => {
    if (key === 'ТЕЛЕФОН_TEL') return telNumber()
    if (Object.prototype.hasOwnProperty.call(OPERATOR, key)) return OPERATOR[key as OperatorKey]
    throw new OperatorTemplateError(`unknown placeholder {{${key}}}`)
  })
}

/** The dom and bani sites have no /pricing of their own: the sentence stays, the link leads to the «Записи» site. */
export function resolveStorefrontHref(site: StorefrontSite, href: string, siteUrls: SiteUrls): string {
  if (href === '/pricing' && (site === 'dom' || site === 'bani')) return `${siteUrls.zapis}/pricing`
  return href
}
