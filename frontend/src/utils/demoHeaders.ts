import type { AxiosError } from 'axios'

/** The text API_CONTRACT_CYCLE28.md §599 sends with the restriction; used only if the body arrives empty. */
export const DEMO_RESTRICTED_FALLBACK = 'В демо-версии это действие недоступно.'

/** Plain-object and `AxiosHeaders` both work: axios lower-cases names, tests may not. */
function readHeader(headers: unknown, name: string): string | undefined {
  if (!headers || typeof headers !== 'object') return undefined
  const get = (headers as { get?: (n: string) => unknown }).get
  if (typeof get === 'function') {
    const v = get.call(headers, name)
    if (v !== undefined && v !== null) return String(v)
  }
  const key = Object.keys(headers).find((k) => k.toLowerCase() === name)
  if (key === undefined) return undefined
  const v = (headers as Record<string, unknown>)[key]
  return v === undefined || v === null ? undefined : String(v)
}

/**
 * API_CONTRACT_CYCLE28.md §599 — the ONE 403 in the project that carries a body: a demo role tried an action the demo
 * forbids. It is recognised by the `X-Demo-Restricted` header (never by the status or the text alone: every other 403
 * is empty and keeps its old wording). Returns the text to show as the form error, or `null` if this is not that refusal.
 */
export function getDemoRestrictedMessage(error: unknown): string | null {
  const res = (error as AxiosError | undefined)?.response
  if (res?.status !== 403) return null
  if (readHeader(res.headers, 'x-demo-restricted') === undefined) return null
  const body = typeof res.data === 'string' ? res.data.trim() : ''
  return body || DEMO_RESTRICTED_FALLBACK
}

/** §600a — 503 + `X-Demo-Resetting` = "the demo is being reset"; a plain 503 is an ordinary outage. */
export function isDemoResetting(error: unknown): boolean {
  const res = (error as AxiosError | undefined)?.response
  return res?.status === 503 && readHeader(res.headers, 'x-demo-resetting') !== undefined
}
