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
  /** API_CONTRACT_CYCLE5.md §46.1 (BREAKING № 5) — US-78. Defaults to `false` server-side when
   *  omitted, which is exactly today's behaviour, so this is safe to leave off for staff bookings. */
  bookedForOther?: boolean
  /** Required (400 otherwise) when `bookedForOther` is `true`. */
  guardianConfirmation?: { textVersion: string; confirmed: true }
}

/** API_CONTRACT_CYCLE6.md §41.2/§45.1 — server sends only these three; "past" is a client-side concept. */
export type DayAvailabilityStatus = 'Available' | 'FullyBooked' | 'DayOff'

/**
 * API_CONTRACT_CYCLE10.md §121.3 — the schedule's OWN state, independent of whether the day can be
 * booked (`status`). Only ever non-null when `staffMode: true` — the server withholds it from
 * everyone else on purpose (a non-staff visitor has no business telling "day off" apart from
 * "schedule not filled in yet" for someone else's roster). Never derive this client-side.
 */
export type DayScheduleState = 'Working' | 'DayOff' | 'NoSchedule' | null

export interface DayAvailability {
  date: string
  status: DayAvailabilityStatus
  lastFreeSlotStart: string | null
  /** §121.3 — always present on the wire but only meaningful (non-null) when `staffMode: true`. */
  scheduleState: DayScheduleState
}

export interface AvailabilityResponse {
  from: string
  to: string
  totalDurationMinutes: number
  stepMinutes: number
  horizonDays: number
  horizonLastDate: string
  /**
   * §121.3/§131 — THE ONLY source of truth for "is staff mode on". Never infer this from the JWT
   * role, `authStore`, or the `manual` flag this same request sent — the server checked real
   * membership and this is it saying so.
   */
  staffMode: boolean
  days: DayAvailability[]
}

/** API_CONTRACT_CYCLE10.md §122.1 */
export type BookingEventKind = 'Created' | 'Rescheduled' | 'Cancelled' | 'Completed' | 'NoShow' | 'PaymentMarked'
export type BookingEventActorKind = 'Client' | 'Guest' | 'Staff' | 'SuperAdmin' | 'System'

export interface BookingEventActor {
  kind: BookingEventActorKind
  name: string | null
  role: 'Master' | 'CompanyOwner' | null
  /** Готовая русская строка — собирает сервер, фронт не формулирует вторую версию (§122.1). */
  label: string
}

export interface BookingEvent {
  id: string
  kind: BookingEventKind
  occurredAt: string
  /** Готовый русский заголовок — собирает сервер (§122.1). */
  title: string
  actor: BookingEventActor
  reschedule: { fromDate: string; fromStartTime: string; toDate: string; toStartTime: string } | null
  cancellationReason: string | null
}

export interface BookingHistoryResponse {
  bookingId: string
  /** §122.2 — true when this booking predates the journal (no `Created` event exists for it). */
  precedesJournal: boolean
  /** Server order is ascending `occurredAt` (§122.1); the caller re-sorts for display if needed. */
  events: BookingEvent[]
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
    /** API_CONTRACT_CYCLE10.md §121.1/§121.2 — a REQUEST, not a grant; the server decides based on
     *  real membership and reports back via `staffMode`/`scheduleState`. Ignored by the server
     *  unless `manual` is also true AND the caller is staff of THIS company (or SuperAdmin). */
    extendedHours = false,
  ) => {
    const params = serviceParams(serviceId, extraServiceIds)
    params.append('companyId', companyId)
    params.append('masterId', masterId)
    params.append('from', from)
    params.append('to', to)
    if (manual) params.append('manual', 'true')
    if (extendedHours) params.append('extendedHours', 'true')
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
    // R2 (`SPEC_CYCLE6_BOOKING_FIXES.md` §0.1 Q7 review): reschedule passes the booking being moved so the grid doesn't
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

  /**
   * API_CONTRACT_CYCLE10.md §122 — staff of the booking's company or SuperAdmin only; everyone else
   * (including the client who owns the booking, §122.3 П8) gets a bare 404, same non-oracle rule as
   * `getSlots`. Only call this when `booking.historyEventCount` is a positive number (§123) — the
   * list endpoints never preload it.
   */
  getHistory: (id: string) => api.get<BookingHistoryResponse>(`/bookings/${id}/history`).then((r) => r.data),
}
