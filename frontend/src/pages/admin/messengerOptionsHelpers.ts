import type { AdminMessengerOption, MessengerPriceChange, MessengerPriceInput, UpdateMessengerPriceInput } from '../../types'

// Cycle 43 (API_CONTRACT_CYCLE43.md §43.22–§43.24). Client-side logic of the price form only: parsing the field and shaping the request bodies.
// Everything the admin READS (sale verdict, consequences, texts) comes from the server and is printed as it is.

const MAX_PRICE = 99_999_999.99
export const COMMENT_MAX = 500

export type PriceParse = { ok: true; value: number | null } | { ok: false; error: string }

/** The price field: empty = remove the price (`null`); «490», «490,5», «490.50». Courtesy check only — the server is the gate and its text wins. */
export function parsePriceInput(raw: string): PriceParse {
  const s = raw.trim().replace(/\s+/g, '').replace(',', '.')
  if (s === '') return { ok: true, value: null }
  if (!/^\d+(\.\d+)?$/.test(s)) return { ok: false, error: 'Введите число, например 490 или 490,50' }
  const [, frac = ''] = s.split('.')
  if (frac.length > 2) return { ok: false, error: 'Цена — не больше двух знаков после запятой' }
  const value = Number(s)
  if (value > MAX_PRICE) return { ok: false, error: 'Цена — не больше 99 999 999,99 ₽' }
  return { ok: true, value }
}

/** The text the price field starts with: «490» / «490,5» / «» for a price that is not set. */
export function priceToInput(price: number | null | undefined): string {
  return price == null ? '' : String(price).replace('.', ',')
}

/** `isActive: null` = «не менять» on the wire: send the flag only when the admin actually flipped it. */
export function buildPreviewBody(option: AdminMessengerOption, price: number | null, isActive: boolean): MessengerPriceInput {
  return { pricePerMonth: price, isActive: isActive === option.isActive ? null : isActive }
}

/** Expected values are the ones the consequences were computed from (Д-4): the server answers 409 when another admin got there first. */
export function buildSaveBody(option: AdminMessengerOption, price: number | null, isActive: boolean, comment: string): UpdateMessengerPriceInput {
  return {
    ...buildPreviewBody(option, price, isActive),
    comment: comment.trim(),
    expectedPricePerMonth: option.pricePerMonth,
    expectedIsActive: option.isActive,
  }
}

export function validateComment(comment: string): string | null {
  const t = comment.trim()
  if (t === '') return 'Укажите причину изменения цены'
  if (t.length > COMMENT_MAX) return 'Причина — не длиннее 500 символов'
  return null
}

/** One journal line: «19.10.2026 · Иванов: 490 ₽/мес → 590 ₽/мес; опция активна → неактивна · причина». */
export function historyLine(h: MessengerPriceChange, fmt: (iso: string) => string): string {
  const parts = [`${h.oldPriceText} → ${h.newPriceText}`]
  if (h.oldIsActive !== h.newIsActive) parts.push(h.newIsActive ? 'опция включена' : 'опция выключена')
  const who = h.changedByName ? ` · ${h.changedByName}` : ''
  const why = h.comment ? ` · ${h.comment}` : ''
  return `${fmt(h.changedAt)}${who}: ${parts.join('; ')}${why}`
}

/** Codes of the two messenger options (ARCHITECTURE_CYCLE43.md §43.0a): the fallback when the id list of `GET /api/admin/messenger-options` is not available. */
const MESSENGER_OPTION_CODES = new Set(['notifications.whatsapp', 'notifications.max'])

/**
 * Cycle 43 (§43.5.3, US-43-02 / US-43-03 кр. 6): the plans matrix and the subscription form catalog no longer carry WhatsApp / MAX — they are not a plan
 * feature. `GET /api/admin/options` is frozen (cycle19, `additionalProperties: false`), so the messenger ids come from the messenger-options answer;
 * the code is the second line of defence for the moment that answer is not there (error / not loaded yet).
 */
export function excludeMessengerOptions<T extends { id: string; code?: string | null }>(options: T[], messengerOptionIds: readonly (string | null)[]): T[] {
  const ids = new Set(messengerOptionIds.filter((x): x is string => !!x))
  return options.filter((o) => !ids.has(o.id) && !MESSENGER_OPTION_CODES.has((o.code ?? '').toLowerCase()))
}

/** The «months» field of the account-card dialogs (1…24, SPEC cycle 43): a whole number in range, otherwise `null` and the quote is not requested. */
export function parseMonths(raw: string, max = 24): number | null {
  const t = raw.trim()
  if (!/^\d+$/.test(t)) return null
  const n = Number(t)
  return n >= 1 && n <= max ? n : null
}
