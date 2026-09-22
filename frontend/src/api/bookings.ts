import { api } from './client'
import type { Booking, TimeSlot } from '../types'

export interface CreateBookingPayload {
  companyId: string
  serviceId: string
  /**
   * US-67 (API_CONTRACT_CYCLE6.md §43.1) — 1..5 services for this visit, `serviceIds[0] ===
   * serviceId`. Omitted entirely for the embed widget (§43.3 — deliberately single-service, see
   * `EmbedPage.tsx`), not just left empty, so a pre-cycle server that doesn't know the field yet
   * behaves exactly as before.
   */
  serviceIds?: string[]
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

/**
 * Builds query params for the two "which services" endpoints. `serviceId` (legacy, singular) is
 * always sent. `serviceIds` — repeated once per id (`serviceIds=a&serviceIds=b`, NOT axios's default
 * `serviceIds[]=a&serviceIds[]=b`, which the contract doesn't describe and ASP.NET Core query
 * binding isn't guaranteed to accept) — is sent only when `extraServiceIds` is an array, even an
 * empty one. When it's `undefined` (the embed widget, §43.3), `serviceIds` is omitted entirely: the
 * widget stays deliberately single-service and must not send the new field at all, not just a
 * one-element one.
 */
function serviceParams(serviceId: string, extraServiceIds: string[] | undefined): URLSearchParams {
  const params = new URLSearchParams()
  params.append('serviceId', serviceId)
  if (extraServiceIds !== undefined) {
    ;[serviceId, ...extraServiceIds].forEach((id) => params.append('serviceIds', id))
  }
  return params
}

export const bookingsApi = {
  /**
   * @param extraServiceIds US-67: additional services (beyond `serviceId`) selected for this visit.
   *   `undefined` (embed widget) omits `serviceIds` from the request entirely; `[]` sends `serviceIds`
   *   with just `serviceId` in it.
   */
  getAvailability: (
    companyId: string,
    masterId: string,
    serviceId: string,
    extraServiceIds: string[] | undefined,
    from: string,
    to: string,
    manual = false,
  ) => {
    const params = serviceParams(serviceId, extraServiceIds)
    params.append('companyId', companyId)
    params.append('masterId', masterId)
    params.append('from', from)
    params.append('to', to)
    if (manual) params.append('manual', 'true')
    return api.get<AvailabilityResponse>('/bookings/availability', { params }).then((r) => r.data)
  },

  getSlots: (
    companyId: string,
    masterId: string,
    serviceId: string,
    extraServiceIds: string[] | undefined,
    date: string,
    manual = false,
    extendedHours = false,
    excludeBookingId?: string,
  ) => {
    const params = serviceParams(serviceId, extraServiceIds)
    params.append('companyId', companyId)
    params.append('masterId', masterId)
    params.append('date', date)
    if (manual) params.append('manual', 'true')
    if (extendedHours) params.append('extendedHours', 'true')
    // R2 (SPEC.md §0.1 Q7 review): reschedule passes the booking being moved so the grid doesn't
    // block the booking's own current interval against itself.
    if (excludeBookingId) params.append('excludeBookingId', excludeBookingId)
    return api.get<TimeSlot[]>('/bookings/slots', { params }).then((r) => r.data)
  },

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
