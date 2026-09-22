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
   * How many days ahead a client may book online (API_CONTRACT_CYCLE6.md §41.4/§45.7).
   * 0/null/undefined means "use the server default" (90 days), not "booking closed".
   */
  bookingHorizonDays?: number | null
  /**
   * Server-computed aggregate over ALL of the company's reviews (not just the current page of
   * `GET /api/companies/{id}/reviews`) — cycle C fix for the QA-found regression where the average
   * was computed client-side from `reviewsData.items` and changed when paging through reviews
   * (TEST_CATALOG.md "Регрессии продукта, найденные QA…", item 2). `null`/absent when there are no
   * reviews yet. Field names and shape per API_CONTRACT.md §18.2.
   */
  averageRating?: number | null
  reviewCount?: number
  /** US-30 — city and IANA time zone the reminder schedule is computed in. API_CONTRACT_CYCLE4.md §31.4. */
  cityId?: number | null
  cityName?: string | null
  cityRegion?: string | null
  timeZoneId?: string | null
  timeZoneIsManual?: boolean
  utcOffsetMinutes?: number | null
}

// ── Cycle 4: notifications (WhatsApp channel) — API_CONTRACT_CYCLE4.md §19–§34 ────────────────────

/** §19.4 — exact string values, checked against the backend by grep per §37 п. 1. */
export type ChannelState =
  | 'NotConnected'
  | 'Connecting'
  | 'Connected'
  | 'Disconnected'
  | 'Blocked'
  | 'DisabledByOwner'
  | 'NeedsReconnect'
  | 'Replaced'

export type ChannelPaymentState = 'NotPaid' | 'Paid' | 'Suspended'

export type NotificationType =
  | 'BookingConfirmed'
  | 'Reminder'
  | 'BookingCancelled'
  | 'BookingRescheduled'
  | 'StaffBookingCreated'
  | 'StaffBookingCancelled'

export type NotificationStatus = 'Pending' | 'Sent' | 'Delivered' | 'Failed' | 'Expired' | 'Skipped' | 'Cancelled'

/** §19.5 — server renders stateText itself; the frontend never builds its own copy (§37 п. 2). */
export interface ChannelCompanyRef {
  companyId: string
  companyName: string
  isActive: boolean
}

export interface ChannelDto {
  id: string
  state: ChannelState
  stateText: string
  phoneMasked: string | null
  paymentState: ChannelPaymentState
  paidFrom: string | null
  paidUntil: string | null
  requestedAt: string | null
  connectedAt: string | null
  riskAcceptedAt: string | null
  idleSince: string | null
  idleDeadline: string | null
  replacedByChannelId: string | null
  companies: ChannelCompanyRef[]
  canConnect: boolean
  canReplace: boolean
}

export interface ChannelOffer {
  available: boolean
  pricePerMonth: number | null
  currency: string
  idleDays: number
  planAllows: boolean
  riskTextVersion: string
}

export interface City {
  id: number
  name: string
  region: string
  timeZoneId: string
  utcOffsetMinutes: number
  label: string
}

export interface NotificationSettings {
  enabledTypes: NotificationType[]
  reminderLeadMinutes: number
  minLeadMinutes: number
  planAllowsChannel: boolean
  channel: {
    assigned: boolean
    channelId: string | null
    state: ChannelState | null
    paymentState: ChannelPaymentState | null
    paidUntil: string | null
  } | null
  effectiveEnabled: boolean
  blockedReason: string | null
}

export interface NotificationPlaceholder {
  token: string
  description: string
  types: string[]
}

export interface NotificationTemplate {
  type: NotificationType
  body: string
  isDefault: boolean
  defaultBody: string
  updatedAt: string
}

export interface NotificationTemplatesResponse {
  placeholders: NotificationPlaceholder[]
  unsubscribeLine: string
  templates: NotificationTemplate[]
}

export interface NotificationLogEntry {
  id: string
  createdAt: string
  type: NotificationType
  typeText: string
  recipientName: string
  recipientPhoneMasked: string
  status: NotificationStatus
  statusText: string
  bookingId: string | null
  visitStart: string | null
  sentAt: string | null
  channelId: string
}

export interface NotificationLogSummary {
  days: number
  sent: number
  delivered: number
  read: number
  failed: number
  skipped: number
  expired: number
  channelPaidUntil: string | null
  byCompany: (NotificationLogSummary & { companyId: string; companyName: string })[] | null
}

/** US-32 п. 6 — additive field on GET /api/bookings/master and /api/bookings/{id}. */
export interface ReminderStatus {
  status: NotificationStatus
  text: string
}

export interface AdminChannelDto {
  id: string
  ownerName: string
  ownerPhoneMasked: string
  state: ChannelState
  stateText: string
  paymentState: ChannelPaymentState
  paidFrom: string | null
  paidUntil: string | null
  companyCount: number
  idleSince: string | null
  requestedAt: string | null
}

export interface AdminChannelSummary {
  connected: number
  connecting: number
  disconnected: number
  blocked: number
  needsReconnect: number
  idle: number
  expiringIn7Days: number
  pendingRequests: number
}

export interface PlatformSettings {
  channelPricePerMonth: number | null
  channelIdleDays: number
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

/** US-67 (API_CONTRACT_CYCLE6.md §43.2/§45) — one line item of a multi-service visit. */
export interface BookingServiceLine {
  serviceId: string
  name: string
  durationMinutes: number
  price: number
}

export interface Booking {
  id: string
  companyId: string
  companyName?: string
  companySlug?: string
  serviceId: string
  serviceName: string
  /**
   * US-67 — every service of the visit, always non-empty (bookings created before the cycle come
   * back with a single-element array from the server). `services[0].serviceId === serviceId`.
   */
  services?: BookingServiceLine[]
  /** US-67 — sum of services[].durationMinutes; undefined only for mocks that predate the cycle. */
  totalDurationMinutes?: number
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
  /** US-32 п. 6 — null when the booking has no notifications at all. */
  reminderStatus?: ReminderStatus | null
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
