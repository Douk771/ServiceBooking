/**
 * US-65/Q5 (API_CONTRACT_CYCLE6.md §41.4): "how many days ahead a client may book" is a per-company
 * setting, sent as `bookingHorizonDays` on `PUT /api/companies/{id}`.
 *
 * Contract rules:
 * - empty/blank input → sent as `0`, which the SERVER treats as "reset to the default (90)", never
 *   as "booking closed";
 * - 1..365 → the new value;
 * - anything else (negative, >365, non-numeric) is rejected before the request is even sent, with the
 *   same wording the server itself would use for a 400.
 */
const MIN_HORIZON_DAYS = 1
const MAX_HORIZON_DAYS = 365
export const HORIZON_OUT_OF_RANGE_MESSAGE = 'Горизонт записи — от 1 до 365 дней'

export interface ParsedBookingHorizon {
  /** What to send on the wire: 0 (blank/default) or a validated 1..365 value. */
  value: number
  /** Set when the input can't be sent as-is; `value` is meaningless in that case. */
  error?: string
}

/**
 * §41.2 — a `GET /api/bookings/availability` request whose `to` lands beyond
 * `today + Company.bookingHorizonDays` is rejected with 400 and this exact bare-string message
 * (server fills in N): «Записаться можно не дальше чем на N дней вперёд».
 *
 * `BookingCalendar` needs this because it learns the company's horizon FROM the availability
 * response — so the very first request of a freshly opened calendar (built from `endOfMonth`, with
 * no known horizon yet) can legitimately overshoot on a company that set a short horizon. Rather
 * than guess a safe range up front, the calendar requests the natural month range and, on this
 * specific 400, extracts N and retries once with `to` clamped to it.
 */
export function parseHorizonExceededDays(message: string): number | null {
  const match = message.match(/на (\d+) дн/)
  return match ? Number(match[1]) : null
}

export function parseBookingHorizonInput(raw: string): ParsedBookingHorizon {
  const trimmed = raw.trim()
  if (trimmed === '') return { value: 0 }

  const n = Number(trimmed)
  if (!Number.isFinite(n) || !Number.isInteger(n)) return { value: 0, error: HORIZON_OUT_OF_RANGE_MESSAGE }
  if (n === 0) return { value: 0 }
  if (n < MIN_HORIZON_DAYS || n > MAX_HORIZON_DAYS) return { value: 0, error: HORIZON_OUT_OF_RANGE_MESSAGE }

  return { value: n }
}
