import type { AxiosError } from 'axios'
import { getDemoRestrictedMessage } from '@/utils/demoHeaders'

/**
 * dom error mapping (API_CONTRACT_CYCLE37.md §37.20). Two shapes only:
 *  - 400/402/429/503 and a few 409 of the shared routes — a bare Russian string (`text/plain`) shown as-is;
 *  - 409 of the «Дома» domain — JSON `{ code, message, … }`: the UI branches on `code`, prints `message`.
 * 401/403/404 have empty bodies, 451 is handled globally by api/client.ts.
 */

export function httpStatus(error: unknown): number | undefined {
  return (error as AxiosError | undefined)?.response?.status
}

/** The 409 JSON body when the error is one, else null. Strings (shared routes) never match. */
export function readConflict<T extends { code: string; message: string }>(error: unknown): T | null {
  const ax = error as AxiosError | undefined
  if (ax?.response?.status !== 409) return null
  const data = ax.response.data as unknown
  if (data && typeof data === 'object' && typeof (data as { code?: unknown }).code === 'string') return data as T
  return null
}

/** The plain-text body (trimmed), '' when the body is not a string. */
export function plainBody(error: unknown): string {
  const data = (error as AxiosError | undefined)?.response?.data
  return typeof data === 'string' ? data.trim() : ''
}

/**
 * Fallback for any failed dom request: the server's text for 400/402/409/429/503, otherwise a fixed wording per status.
 * `fallback` is used for an unexpected 4xx without a body.
 */
export function getStayErrorMessage(error: unknown, fallback = 'Не удалось выполнить действие. Попробуйте ещё раз.'): string {
  const demoRestricted = getDemoRestrictedMessage(error)
  if (demoRestricted) return demoRestricted
  const status = httpStatus(error)
  const conflict = readConflict<{ code: string; message: string }>(error)
  if (conflict?.message) return conflict.message
  const body = plainBody(error)
  if (status === undefined) return 'Нет связи с сервером. Проверьте интернет и попробуйте ещё раз.'
  if ((status === 400 || status === 402 || status === 409 || status === 429 || status === 503) && body) return body
  switch (status) {
    case 401:
      return 'Войдите в аккаунт, чтобы продолжить.'
    case 403:
      return 'Недостаточно прав для этого действия.'
    case 404:
      return 'Не найдено — возможно, это уже удалено или ссылка неверна.'
    case 413:
      return 'Файл слишком большой.'
    case 429:
      return 'Слишком много запросов — подождите минуту.'
    default:
      return status >= 500 ? 'Сервер временно недоступен. Попробуйте позже.' : fallback
  }
}

/** True when the failure is "the thing does not exist" (404 has no body and is indistinguishable from "never existed"). */
export function isNotFound(error: unknown): boolean {
  return httpStatus(error) === 404
}
