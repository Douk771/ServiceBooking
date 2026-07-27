export interface Company {
  id: string
  name: string
  slug: string
  description?: string
  logoUrl?: string
  address?: string
  phone?: string
  email?: string
  allowSelfBooking: boolean
  requirePrepayment?: boolean
  /**
   * Whether online self-service booking is available (requires a paid, active subscription).
   * On the Free plan online booking is blocked for both guests and authenticated clients —
   * only staff manual bookings work.
   */
  onlineBookingEnabled?: boolean
  /** Whether the owner's effective tariff plan includes analytics (gates GET /api/reports/masters). */
  allowAnalytics?: boolean
  /** Whether the owner's effective tariff plan includes mailing (gates POST /api/companies/{id}/mail). */
  allowMailing?: boolean
  /** Owner's own opt-in to the public directory (independent of the tariff gate). */
  showInPublicListing?: boolean
  /** Computed: listed in the public directory (owner opt-in AND tariff allows it). */
  publicListingEnabled?: boolean
  /** Computed: prepayment actually enforced (owner toggle AND tariff allows online payment). */
  prepaymentEnabled?: boolean
}

export interface Service {
  id: string
  companyId: string
  name: string
  description?: string
  durationMinutes: number
  price: number
  imageUrl?: string
}

export interface Master {
  id: string
  firstName: string
  lastName: string
  avatarUrl?: string
  bio?: string
  role: string
}

export interface TimeSlot {
  start: string
  end: string
}

export interface Booking {
  id: string
  companyId: string
  companyName?: string
  companySlug?: string
  serviceId: string
  serviceName: string
  masterId: string
  masterName: string
  clientId?: string
  clientName: string
  clientPhone?: string
  clientEmail?: string
  date: string
  startTime: string
  endTime: string
  status: BookingStatus
  paymentStatus?: PaymentStatus
  price?: number
  notes?: string
  createdAt: string
}

export type BookingStatus = 'Pending' | 'Confirmed' | 'Cancelled' | 'Completed' | 'NoShow'
export type PaymentStatus = 'NotRequired' | 'Pending' | 'Paid'

export interface AuthResponse {
  token: string
  userId: string
  phone: string
  email?: string | null
  firstName: string
  lastName: string
  roles: string[]
}

export interface User {
  id: string
  phone: string
  email?: string | null
  firstName: string
  lastName: string
  roles: string[]
}
