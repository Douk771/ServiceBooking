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
  /** Raw tariff capability — independent of the owner's own toggle, unlike the *Enabled fields above. */
  planAllowsOnlineBooking?: boolean
  planAllowsOnlinePayment?: boolean
  planAllowsPublicListing?: boolean
  /** Seat cap for company members (owner included). Null/undefined means unlimited. */
  maxEmployees?: number | null
  /**
   * Server-computed aggregate over ALL of the company's reviews (not just the current page of
   * `GET /api/companies/{id}/reviews`) — cycle C fix for the QA-found regression where the average
   * was computed client-side from `reviewsData.items` and changed when paging through reviews
   * (TEST_CATALOG.md "Регрессии продукта, найденные QA…", item 2). `null`/absent when there are no
   * reviews yet. Field names and shape per API_CONTRACT.md §18.2.
   */
  averageRating?: number | null
  reviewCount?: number
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
  /** Reason the booking was cancelled, shown to whichever side didn't cancel it (US-06). Null/absent
   *  when the booking was never cancelled, or was cancelled without a reason. */
  cancellationReason?: string | null
  notes?: string
  createdAt: string
  /** Version of the privacy policy in effect at the moment of a guest booking (US-37 §7.2). Null on
   *  staff-created bookings and on bookings made by an already-registered client. Never rewritten
   *  retroactively — this is why it's the version, not a live reference. */
  consentPrivacyVersion?: string | null
  /** Version of the terms of service in effect at the moment of a guest booking. Same nullability as
   *  consentPrivacyVersion. */
  consentTermsVersion?: string | null
  consentAcceptedAt?: string | null
  /** true when the client who made this booking has since deleted their account — the booking is
   *  anonymized (clientId/guest fields cleared) but kept for the salon's records (ARCHITECTURE.md §5.2). */
  clientDeleted?: boolean
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

/** API_CONTRACT.md §11.1 — the single pagination envelope shared by all four paginated endpoints
 *  in this cycle (admin users, admin companies, company reviews, master clients). `hasNext` is
 *  computed by the server (`page * pageSize < total`) — the frontend never derives it itself. */
export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
  hasNext: boolean
}

export type LegalDocumentType = 'Privacy' | 'Terms'
export type LegalChangeKind = 'Material' | 'Editorial'

/** API_CONTRACT.md §1 — metadata only, no text. Used for the footer/registration links and to
 *  compare versions without paying for the HTML body. */
export interface LegalDocumentMeta {
  type: LegalDocumentType
  title: string
  version: string
  effectiveFrom: string
  isDraft: boolean
  changeKind: LegalChangeKind
}

/** API_CONTRACT.md §2 — metadata plus the HTML fragment for /privacy and /terms. */
export interface LegalDocument extends LegalDocumentMeta {
  contentHtml: string
}

/** API_CONTRACT.md §3 — one entry per document type in GET /api/legal/consent-status. */
export interface ConsentStatusDocument {
  type: LegalDocumentType
  version: string
  acceptedVersion: string | null
  changeKind: LegalChangeKind
}

export interface ConsentStatus {
  requiresAcceptance: boolean
  showBanner: boolean
  documents: ConsentStatusDocument[]
}
