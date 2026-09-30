import type { AxiosError } from 'axios'
import { getDemoRestrictedMessage } from '@/utils/demoHeaders'
import type { OrderConflictDto, OrderRefusalDto } from '../types'

/**
 * goods error mapping (API_CONTRACT_CYCLE23.md §406). Two shapes only:
 *  - 400/402/429/503 — a bare Russian string (`text/plain`) that is shown as-is;
 *  - 409 of the order domain — JSON `{ code, message, … }`: the UI branches on `code`, prints `message`.
 * 401/403/404 have empty bodies, 451 is handled globally by api/client.ts. The one 403 with a body is the demo refusal
 * (403 + `X-Demo-Restricted`, API_CONTRACT_CYCLE35.md §35.23): its text is shown, every other 403 keeps its wording.
 */

export function httpStatus(error: unknown): number | undefined {
  return (error as AxiosError | undefined)?.response?.status
}

/** The 409 JSON body when the error is one, else null. Strings (existing routes) never match. */
export function readConflict<T extends { code: string; message: string }>(error: unknown): T | null {
  const ax = error as AxiosError | undefined
  if (ax?.response?.status !== 409) return null
  const data = ax.response.data as unknown
  if (data && typeof data === 'object' && typeof (data as { code?: unknown }).code === 'string') return data as T
  return null
}

function plainBody(error: unknown): string {
  const data = (error as AxiosError | undefined)?.response?.data
  return typeof data === 'string' ? data.trim() : ''
}

/**
 * Fallback for any failed goods request: server text for 400/402/409-string/429/503, otherwise a fixed
 * wording per status. `subject` fills "…не найден" style 404s ("Магазин", "Заказ").
 */
export function getGoodsErrorMessage(error: unknown, fallback = 'Не удалось выполнить действие. Попробуйте ещё раз.'): string {
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

export function readRefusal(error: unknown): OrderRefusalDto | null {
  return readConflict<OrderRefusalDto>(error)
}

export function readOrderConflict(error: unknown): OrderConflictDto | null {
  return readConflict<OrderConflictDto>(error)
}

/** Codes of `OrderRefusalDto` that need the customer to sign in / verify — the checkout screen has a
 *  dedicated call to action for each instead of just a red banner. */
export function isLoginRequired(r: OrderRefusalDto | null): boolean {
  return r?.code === 'LoginRequired'
}
export function isPhoneVerificationRequired(r: OrderRefusalDto | null): boolean {
  return r?.code === 'PhoneVerificationRequired'
}
/** `PriceChanged` is the only refusal that is fixed by confirming, not by editing the cart. */
export function isPriceChangedOnly(r: OrderRefusalDto | null): boolean {
  return r?.code === 'PriceChanged'
}
