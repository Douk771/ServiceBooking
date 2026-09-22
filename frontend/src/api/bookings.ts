import { api } from './client'
import type { Booking, TimeSlot } from '../types'

export interface CreateBookingPayload {
  companyId: string
  serviceId: string
  masterId: string
  date: string
  startTime: string
  notes?: string
  guestName?: string
  guestPhone?: string
  guestEmail?: string
  captchaToken?: string
}

/** API_CONTRACT_CYCLE6.md §41.2/§45.1 — server sends only these three; "past" is a client-side concept. */
export type DayAvailabilityStatus = 'Available' | 'FullyBooked' | 'DayOff'

export interface DayAvailability {
  date: string
  status: DayAvailabilityStatus
  lastFreeSlotStart: string | null
}

export interface AvailabilityResponse {
  from: string
  to: string
  totalDurationMinutes: number
  stepMinutes: number
  horizonDays: number
  horizonLastDate: string
  days: DayAvailability[]
}

export const bookingsApi = {
  getAvailability: (companyId: string, masterId: string, serviceId: string, from: string, to: string, manual = false) =>
    api
      .get<AvailabilityResponse>('/bookings/availability', {
        params: { companyId, masterId, serviceId, from, to, manual: manual || undefined },
      })
      .then((r) => r.data),

  getSlots: (companyId: string, masterId: string, serviceId: string, date: string, manual = false) =>
    api
      .get<TimeSlot[]>('/bookings/slots', {
        params: { companyId, masterId, serviceId, date, manual: manual || undefined },
      })
      .then((r) => r.data),

  create: (data: CreateBookingPayload) =>
    api
      .post<Booking>('/bookings', {
        ...data,
        startTime: data.startTime.length === 5 ? `${data.startTime}:00` : data.startTime,
      })
      .then((r) => r.data),

  getOccupied: (masterId: string, date: string) =>
    api.get<{ start: string; end: string }[]>('/bookings/occupied', { params: { masterId, date } }).then((r) => r.data),

  getMasterBookings: (date?: string, to?: string) =>
    api.get<Booking[]>('/bookings/master', { params: { date, to } }).then((r) => r.data),

  reschedule: (id: string, date: string, startTime: string) =>
    api.patch(`/bookings/${id}/reschedule`, {
      date,
      startTime: startTime.length === 5 ? `${startTime}:00` : startTime,
    }),

  complete: (id: string) => api.patch(`/bookings/${id}/complete`),
  noShow: (id: string) => api.patch(`/bookings/${id}/noshow`),
  markPaid: (id: string) => api.patch(`/bookings/${id}/mark-paid`),

  cancel: (id: string, reason?: string) => api.patch(`/bookings/${id}/cancel`, reason ? JSON.stringify(reason) : null),

  getClientBookings: (status?: string) =>
    api.get<import('../types').Booking[]>('/bookings/client', { params: status ? { status } : {} }).then((r) => r.data),
}
