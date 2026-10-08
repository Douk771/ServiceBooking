import type { StayBookingStatus } from '../types'

/**
 * Status presets of the owner's list of bookings (API_CONTRACT_CYCLE37.md §37.30.1: `status` is repeatable, the server default is
 * «ожидают проверки оплаты» — the working queue of the owner, oldest file first).
 */
export type BookingPreset = 'awaiting' | 'held' | 'confirmed' | 'closed' | 'all'

export const ALL_STATUSES: StayBookingStatus[] = ['Held', 'AwaitingPaymentCheck', 'Confirmed', 'ExpiredUnpaid', 'PaymentRejected', 'CancelledByGuest', 'CancelledByOwner']

export const PRESETS: { id: BookingPreset; label: string; statuses: StayBookingStatus[]; empty: string }[] = [
  { id: 'awaiting', label: 'Ожидают проверки оплаты', statuses: ['AwaitingPaymentCheck'], empty: 'Нет броней, ожидающих проверки оплаты' },
  { id: 'held', label: 'Ждут оплаты', statuses: ['Held'], empty: 'Нет броней, которые ждут оплаты' },
  { id: 'confirmed', label: 'Подтверждённые', statuses: ['Confirmed'], empty: 'Нет подтверждённых броней' },
  { id: 'closed', label: 'Отменённые и снятые', statuses: ['ExpiredUnpaid', 'PaymentRejected', 'CancelledByGuest', 'CancelledByOwner'], empty: 'Нет отменённых и снятых броней' },
  { id: 'all', label: 'Все', statuses: ALL_STATUSES, empty: 'Броней пока нет' },
]

export const DEFAULT_PRESET: BookingPreset = 'awaiting'

export function presetOf(id: string | null): BookingPreset {
  return PRESETS.some((p) => p.id === id) ? (id as BookingPreset) : DEFAULT_PRESET
}

export const statusesOf = (id: BookingPreset): StayBookingStatus[] => PRESETS.find((p) => p.id === id)!.statuses

/** The queue the owner works through goes oldest-first by the time of the first file (the server sorts; the client only labels it). */
export const isQueue = (id: BookingPreset): boolean => id === 'awaiting'

export const BOOKINGS_PAGE_SIZE = 20
export const BOOKINGS_POLL_MS = 15_000
