import type { BookingStatus } from '../types'

/**
 * Cycle 22 (ARCHITECTURE_CYCLE22.md §377) — the one table of booking-status captions. Wording is the
 * one masters already saw on "Мои записи" / "Клиенты" (feminine — «запись»); `StatusBadge` and the
 * admin bookings filter read it too, so every screen names a status the same way.
 */
export const BOOKING_STATUS_LABELS: Record<BookingStatus, string> = {
  Pending: 'Ожидает',
  Confirmed: 'Подтверждена',
  Completed: 'Выполнена',
  Cancelled: 'Отменена',
  NoShow: 'Не пришёл',
}

/** Caption for a status that arrives as a plain string (visit-history rows); an unknown value is
 *  shown as-is rather than blank. */
export function bookingStatusLabel(status: string): string {
  return (BOOKING_STATUS_LABELS as Record<string, string>)[status] ?? status
}
