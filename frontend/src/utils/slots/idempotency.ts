/**
 * Idempotency key of `POST …/houses/{id}/bookings` (API_CONTRACT_CYCLE37.md §37.24): generated when the form is opened and kept
 * until the booking succeeds, so a double tap, a retry after a lost answer or a page refresh can never create a second booking.
 * `crypto.randomUUID` exists only in secure contexts; a phone opening the dev server over plain http still needs a key.
 */
export function newIdempotencyKey(): string {
  const c = globalThis.crypto
  if (c && typeof c.randomUUID === 'function') return c.randomUUID()
  const bytes = new Uint8Array(16)
  if (c && typeof c.getRandomValues === 'function') c.getRandomValues(bytes)
  else for (let i = 0; i < 16; i++) bytes[i] = Math.floor(Math.random() * 256)
  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80
  const h = Array.from(bytes, (b) => b.toString(16).padStart(2, '0'))
  return `${h.slice(0, 4).join('')}-${h.slice(4, 6).join('')}-${h.slice(6, 8).join('')}-${h.slice(8, 10).join('')}-${h.slice(10).join('')}`
}

const storageKey = (houseId: string) => `dom:booking-key:${houseId}`

/** The key of the booking being made on this house: read from `sessionStorage` (survives a refresh), created on first use. */
export function bookingKeyFor(houseId: string, storage: Pick<Storage, 'getItem' | 'setItem'> | null = safeSession()): string {
  try {
    const existing = storage?.getItem(storageKey(houseId))
    if (existing) return existing
  } catch {
    // storage blocked (private mode): the key lives in memory only
  }
  const key = newIdempotencyKey()
  try {
    storage?.setItem(storageKey(houseId), key)
  } catch {
    // see above
  }
  return key
}

/** After a successful booking: the next one on the same house starts with a fresh key. */
export function forgetBookingKey(houseId: string, storage: Pick<Storage, 'removeItem'> | null = safeSession()): void {
  try {
    storage?.removeItem(storageKey(houseId))
  } catch {
    // nothing to forget
  }
}

function safeSession(): Storage | null {
  try {
    return typeof sessionStorage === 'undefined' ? null : sessionStorage
  } catch {
    return null
  }
}
