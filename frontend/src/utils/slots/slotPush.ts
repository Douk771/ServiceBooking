/** Guest push memory of a booking/session (localStorage): which endpoint this browser registered for which token. */
const KEY_PREFIX = 'stays-booking-push:'
const AT_PREFIX = 'stays-booking-push-at:'
/** The server drops a guest subscription some days after the booking ends; the local memory expires with a margin. */
export const BOOKING_PUSH_TTL_MS = 14 * 24 * 60 * 60 * 1000

/** localStorage key remembering WHICH endpoint this browser registered for a booking (there is no «am I subscribed» route). */
export const bookingPushKey = (token: string) => `${KEY_PREFIX}${token}`
export const bookingPushAtKey = (token: string) => `${AT_PREFIX}${token}`

/** Removes remembered endpoints (and their timestamps) older than the TTL; entries without a timestamp are removed too. */
export function pruneBookingPushStorage(storage: Pick<Storage, 'length' | 'key' | 'getItem' | 'removeItem'>, now: number): void {
  const keys: string[] = []
  for (let i = 0; i < storage.length; i++) {
    const k = storage.key(i)
    if (k) keys.push(k)
  }
  for (const k of keys) {
    if (!k.startsWith(KEY_PREFIX)) continue
    const token = k.slice(KEY_PREFIX.length)
    const at = Number(storage.getItem(bookingPushAtKey(token)))
    if (!Number.isFinite(at) || at <= 0 || now - at > BOOKING_PUSH_TTL_MS) {
      storage.removeItem(k)
      storage.removeItem(bookingPushAtKey(token))
    }
  }
}
