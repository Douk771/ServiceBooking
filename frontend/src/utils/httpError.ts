import type { AxiosError } from 'axios'

/** ARCHITECTURE_CYCLE32.md §32.4.6 — HTTP status of a failed request, undefined for a network failure. */
export function httpStatusOf(error: unknown): number | undefined {
  return (error as AxiosError | undefined)?.response?.status
}

/** The plain-text response body (trimmed, capped), or '' when the body is not a string. */
export function plainErrorBody(error: unknown): string {
  const data = (error as AxiosError | undefined)?.response?.data
  return typeof data === 'string' ? data.trim().slice(0, 500) : ''
}
