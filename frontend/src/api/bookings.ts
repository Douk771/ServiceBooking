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

export const bookingsApi = {
  getSlots: (masterId: string, serviceId: string, date: string) =>
    api
      .get<TimeSlot[]>('/bookings/slots', { params: { masterId, serviceId, date } })
      .then((r) => r.data),

  create: (data: CreateBookingPayload) =>
    api.post<Booking>('/bookings', data).then((r) => r.data),

  getMyBookings: () => api.get<Booking[]>('/bookings/my').then((r) => r.data),

  getMasterBookings: (date?: string) =>
    api.get<Booking[]>('/bookings/master', { params: { date } }).then((r) => r.data),

  cancel: (id: string, reason?: string) =>
    api.patch(`/bookings/${id}/cancel`, reason ? JSON.stringify(reason) : null),
}
