import type { Booking } from '../types'

/**
 * US-67 (API_CONTRACT_CYCLE6.md §43.2/§43.3) — a visit is shown as ONE row listing every service in
 * it, comma-separated, never split into several rows. `services` is always non-empty on bookings
 * from the server (even pre-cycle bookings echo a one-element array) — the fallback to
 * `serviceName` here exists only for call sites/tests that construct a `Booking` without bothering
 * to fill in `services`.
 */
export function formatBookingServiceNames(booking: Pick<Booking, 'serviceName' | 'services'>): string {
  if (booking.services && booking.services.length > 0) {
    return booking.services.map((s) => s.name).join(', ')
  }
  return booking.serviceName
}

/** Sum of durations across all services of the visit — falls back to summing `services` if the
 *  server's echoed `totalDurationMinutes` is missing (defensive; contract guarantees it's present). */
export function bookingTotalDuration(booking: Pick<Booking, 'totalDurationMinutes' | 'services'>): number | undefined {
  if (booking.totalDurationMinutes != null) return booking.totalDurationMinutes
  if (booking.services && booking.services.length > 0) {
    return booking.services.reduce((sum, s) => sum + s.durationMinutes, 0)
  }
  return undefined
}
