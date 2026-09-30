import type { AxiosError } from 'axios'

/**
 * API_CONTRACT_CYCLE28.md §592 — `POST /api/bookings` into a closed showcase company answers 409 with an
 * `application/json` body `{ code: "ShowcaseBookingClosed", message }`. The slot-taken 409 is a bare
 * `text/plain` string, so the two are told apart by the body itself (an object with that `code`), never by status alone.
 *
 * Returns the server's fallback `message` ('' when it is missing), or `null` when this is not that refusal.
 */
export function getShowcaseRefusalMessage(error: unknown): string | null {
  const ax = error as AxiosError | undefined
  if (ax?.response?.status !== 409) return null
  const data = ax.response.data as unknown
  if (!data || typeof data !== 'object') return null
  const body = data as { code?: unknown; message?: unknown }
  if (body.code !== 'ShowcaseBookingClosed') return null
  return typeof body.message === 'string' ? body.message : ''
}
