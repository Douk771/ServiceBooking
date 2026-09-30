import { AxiosError } from 'axios'

/**
 * ARCHITECTURE_CYCLE31.md §31.10.2 — maps a failed salon catalog-listing GET/PUT to a Russian message.
 * API_CONTRACT_CYCLE31.md §31.22: the tariff refusal is JSON `{code, message}` (message is the server's own
 * sentence), the other 400/409 answers are bare text/plain strings, a foreign or missing company is an empty 404.
 */
export function getCatalogListingErrorMessage(error: unknown, fallback: string): string {
  const ax = error as AxiosError | undefined
  const status = ax?.response?.status
  const data = ax?.response?.data as unknown

  if (data && typeof data === 'object' && typeof (data as { message?: unknown }).message === 'string') {
    return (data as { message: string }).message
  }
  if (typeof data === 'string' && data.trim() !== '' && (status === 400 || status === 409)) return data
  if (status === 404) return 'Компания не найдена'
  return fallback
}
