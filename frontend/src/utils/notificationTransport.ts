import type { NotificationTransport, NotificationType } from '../types'

/**
 * API_CONTRACT_CYCLE9.md §112 — `NotificationTransport` is append-only, so this map is the one place
 * that needs a new line when a third transport shows up; every screen that lists/filters/badges a
 * transport reads from here instead of hardcoding "WhatsApp"/"MAX" strings independently.
 */
export const TRANSPORT_LABELS: Record<NotificationTransport, string> = {
  WhatsApp: 'WhatsApp',
  Max: 'MAX',
}

export const TRANSPORT_FILTER_OPTIONS: { value: NotificationTransport | ''; label: string }[] = [
  { value: '', label: 'Все каналы' },
  { value: 'WhatsApp', label: 'WhatsApp' },
  { value: 'Max', label: 'MAX' },
]

/** Captions of notification types — the company's notification settings and template editor list
 *  the same types and must name them the same way (cycle 22, ARCHITECTURE_CYCLE22.md §377). */
export const NOTIFICATION_TYPE_LABELS: Record<NotificationType, string> = {
  BookingConfirmed: 'Подтверждение записи',
  Reminder: 'Напоминание о визите',
  BookingCancelled: 'Отмена записи',
  BookingRescheduled: 'Перенос записи',
  StaffBookingCreated: 'Новая запись (персоналу)',
  StaffBookingCancelled: 'Отмена записи (персоналу)',
}
