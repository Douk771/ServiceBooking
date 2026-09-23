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
  /**
   * @deprecated Cycle 7 (ARCHITECTURE_CYCLE7.md §56): this now means the AGGREGATE seat cap across
   * the whole billing account's subscription, not a per-company limit. Comparing it against a
   * single company's `members.length` under-counts staff at the account's other companies and will
   * gate "add employee" too early. Do NOT use for that check — use `canAddEmployee` instead. Kept
   * only for display of the raw limit number where explicitly labelled "суммарно".
   */
  maxEmployees?: number | null
  /** Employees used across the WHOLE billing account (all companies), null when not computed (e.g. anonymous catalog). */
  accountSeatsUsed?: number | null
  /** Aggregate seat limit across the whole billing account; null = unlimited. */
  accountSeatsLimit?: number | null
  /** Server-computed per the same rule as the 402 on add-member — use this to gate the "add employee" UI. */
  canAddEmployee?: boolean | null
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

/** Cycle 7 (ChannelDtoCycle7Additions, contracts/cycle7/openapi.yaml): Funded = входит в оплаченные; Unfunded =
 *  заведён сверх оплаченного количества (не отправляет, но сохраняет назначения и состояние
 *  подключения); NotPaid = у аккаунта не оплачено ни одного номера. */
export type ChannelFundingState = 'Funded' | 'Unfunded' | 'NotPaid'

export interface ChannelDto {
  id: string
  state: ChannelState
  stateText: string
  phoneMasked: string | null
  paymentState: ChannelPaymentState
  /** @deprecated Cycle 7: paidFrom is always null now — payment is per-account, not per-number. */
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
  /** Cycle 7 addition — see ChannelFundingState. */
  fundingState: ChannelFundingState
  /** Server-composed (BillingTexts) — the frontend prints this verbatim, never builds its own copy
   *  about payment/funding state (project convention, ARCHITECTURE_CYCLE7.md). */
  fundingText: string
  /** API_CONTRACT_CYCLE5.md §50.2 — shown only to the owner (here) and to SuperAdmin (AdminChannelDto). */
  inn: string | null
  legalEntityForm: LegalEntityForm | null
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
  /** API_CONTRACT_CYCLE5.md §47.1 — the ad-marker dictionary comes from the server; the frontend
   *  never hardcodes or extends it (§56.5 п. 4), so its own highlighting always agrees with the
   *  server's own check on the same text. */
  adMarkers: string[]
  warningTextKey: LegalTextKey
  warningVersion: string
}

export interface NotificationLogEntry {
  id: string
  createdAt: string
  type: NotificationType
  typeText: string
  recipientName: string | null
  recipientPhoneMasked: string | null
  status: NotificationStatus
  statusText: string
  bookingId: string | null
  visitStart: string | null
  sentAt: string | null
  channelId: string
  /** API_CONTRACT_CYCLE5.md §51 — the body/recipient fields above were wiped by the retention job.
   *  The frontend decides "text erased by retention" from this flag, never from an empty string. */
  contentRedacted: boolean
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
  /** API_CONTRACT_CYCLE5.md §50.2 — visible to SuperAdmin. */
  inn: string | null
  legalEntityForm: LegalEntityForm | null
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

// pricingPublicEnabled/pricingPublicBlockedReason added cycle 11 (API_CONTRACT_CYCLE11.md §114.1) —
// nullable blockedReason is informational only, the PUT response (409) is the actual gate.
export type PricingPublicBlockedReason = 'OfferIsDraft' | 'LegalUnavailable'

export interface PlatformSettings {
  channelPricePerMonth: number | null
  channelIdleDays: number
  pricingPublicEnabled: boolean
  pricingPublicBlockedReason: PricingPublicBlockedReason | null
}

// Body of PUT /api/admin/platform-settings → 409 (API_CONTRACT_CYCLE11.md §114.2).
export interface PricingPublicBlockedError {
  reason: PricingPublicBlockedReason
  message: string
  documentType: string
  version: string
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
  /** API_CONTRACT_CYCLE5.md §46.2 — version of the ст. 18 notice (D5) shown under the booking button,
   *  filled by the server from the snapshot in effect at booking time. */
  bookingNoticeVersion?: string | null
  /** Whether this booking was made for someone other than the person submitting the form (US-78). */
  bookedForOther?: boolean
  /** When `bookedForOther` is true, when the guardian/representative confirmation (D12) was recorded. */
  guardianConfirmedAt?: string | null
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

// ── Cycle 7: legal documents & consents — API_CONTRACT_CYCLE7.md §38–§53 ───────────────────────────

/** §38.3 — exact string values, five types instead of two. `"Terms"` no longer exists (BREAKING №1);
 *  it became `"TermsClient"`, and the /terms route/URL is unchanged. */
export type LegalDocumentType = 'Privacy' | 'TermsClient' | 'TermsOwner' | 'PdnConsent' | 'ChannelRiskNotice'
export type LegalChangeKind = 'Material' | 'Editorial'
/** §38.3 — which 451 mechanism a document participates in: blocks everything, blocks owner actions
 *  only, or blocks nothing (consent recorded through its own endpoints instead, §41). */
export type LegalGate = 'Global' | 'OwnerScope' | 'None'
/** §39.3 — microcopy documents (not gated, no `changeKind`/`gate`), fetched by key rather than type. */
export type LegalTextKey =
  | 'BookingNotice'
  | 'TemplateAdWarning'
  | 'UnsubscribePage'
  | 'PhotoConsent'
  | 'HealthDataConsent'
  | 'GuardianConfirmation'
export type ConsentPurpose = 'ProviderDelivery' | 'WorkPhotos' | 'HealthData' | 'ChannelOffer'
export type ConsentAct = 'Acknowledged' | 'Accepted' | 'Consented' | 'Confirmed'
export type ConsentSource =
  | 'Registration'
  | 'ReAcceptance'
  | 'Profile'
  | 'CompanyCreation'
  | 'ChannelRequest'
  | 'ChannelLink'
  | 'PhotoForm'
  | 'HealthForm'
  | 'Booking'
  | 'Migrated'
export type SubjectRequestKind = 'Access' | 'Rectification' | 'Erasure' | 'ConsentWithdrawal' | 'Complaint'
export type SubjectRequestStatus = 'Received' | 'InProgress' | 'Answered' | 'Rejected'
export type DueState = 'OnTime' | 'DueSoon' | 'Overdue'
export type LegalEntityForm = 'Ip' | 'Company' | 'SelfEmployed'

/** A named consent purpose as published in the manifest — §39.1. The frontend reads the set of
 *  purposes from here rather than hardcoding an array, so a change to the purpose list doesn't need
 *  a frontend release (API_CONTRACT_CYCLE5.md §39.1, §56.5 п. 2). */
export interface LegalPurposeMeta {
  key: ConsentPurpose
  title: string
}

/** §39.1 — metadata only, no text. Used for the footer/registration links and to compare versions
 *  without paying for the HTML body. `gate`/`purposes` are new in cycle 5. */
export interface LegalDocumentMeta {
  type: LegalDocumentType
  title: string
  version: string
  effectiveFrom: string
  isDraft: boolean
  changeKind: LegalChangeKind
  gate: LegalGate
  url: string
  /** Present only on `PdnConsent`. */
  purposes?: LegalPurposeMeta[]
}

/** §39.1 — one entry per UI microcopy text in the manifest. */
export interface LegalTextMeta {
  key: LegalTextKey
  version: string
  isDraft: boolean
}

/** §39.1 — `GET /api/legal/documents`: an object with two arrays, not a bare array (BREAKING). */
export interface LegalManifest {
  documents: LegalDocumentMeta[]
  uiTexts: LegalTextMeta[]
}

/** §39.2 — metadata plus the HTML fragment for a single document route (/privacy, /terms, …). */
export interface LegalDocument extends LegalDocumentMeta {
  contentHtml: string
}

/** §39.3 — `GET /api/legal/texts/{key}`: same shape as a document, minus `changeKind`/`gate`. */
export interface LegalText {
  key: LegalTextKey
  version: string
  isDraft: boolean
  contentHtml: string
}

/** §39.4 — one entry per gated document type in `GET /api/legal/consent-status`. */
export interface ConsentStatusDocument {
  type: LegalDocumentType
  currentVersion: string
  acceptedVersion: string | null
  changeKind: LegalChangeKind
  gate: LegalGate
}

export interface ConsentStatus {
  /** Blocks the whole app (Global gate, Material change). */
  requiresAcceptance: boolean
  /** Blocks owner-only actions (OwnerScope gate, Material change) — `false` for non-owners. */
  ownerActionBlocked: boolean
  /** An Editorial change exists — informational banner only, nothing is blocked. */
  showBanner: boolean
  documents: ConsentStatusDocument[]
}

/** §38.2 — the JSON body of an owner-scope 451 (Content-Type: application/json), distinct from the
 *  plain-text body of a global 451. Carries what `OwnerTermsGateModal` needs to open itself. */
export interface OwnerGate451 {
  reason: string
  documentType: LegalDocumentType
  version: string
}

/** §38.4 — one row of the consent ledger, used by §41 (profile) and §49 (export). */
export interface ConsentLedgerEntry {
  id: string
  documentKey: string
  documentVersion: string
  purpose: ConsentPurpose | null
  act: ConsentAct
  source: ConsentSource
  companyId: string | null
  grantedAt: string
  revokedAt: string | null
  revokeReason: string | null
}

/** §41.1 — `GET /api/profile/consents`. */
export interface ProfileConsentsResponse {
  document: { type: 'PdnConsent'; version: string; isDraft: boolean; purposes: LegalPurposeMeta[] }
  granted: { purpose: ConsentPurpose; version: string; grantedAt: string; revokedAt: string | null }[]
  versionOutdated: boolean
  history: ConsentLedgerEntry[]
}

/** §41.3 — what revoking a consent purpose actually did/would do (also returned, unchanged, by the
 *  `revoke-preview` dry-run endpoint). */
export interface ConsentRevokeEffects {
  photosDeleted: number
  healthNotesDeleted: number
  profileFieldsCleared: string[]
  queuedNotificationsCancelled: number
}

export interface ConsentRevokeResponse {
  revoked: number
  effects: ConsentRevokeEffects
}

// ── Cycle 5: photo/health consent (§44, §45) ────────────────────────────────────────────────────────

/** §44.1 — `GET /api/companies/{id}/clients/{key}/photo-consent`. */
export interface PhotoConsentStatus {
  granted: boolean
  grantedAt: string | null
  version: string | null
  confirmedBy: string | null
  textVersionOutdated: boolean
  source: ConsentSource | null
}

/** §45.1 — `GET /api/companies/{id}/clients/{key}/health-note`. */
export interface HealthNoteDto {
  value: string | null
  updatedAt?: string
  updatedBy?: string
  consentRequired?: boolean
}

// ── Cycle 5: subject requests (§48) ─────────────────────────────────────────────────────────────────

export interface SubjectRequestDto {
  id: string
  reference: string
  kind: SubjectRequestKind
  status: SubjectRequestStatus
  phoneMasked: string
  contactValue: string
  message: string
  receivedAt: string
  dueAt: string
  dueState: DueState
  answeredAt: string | null
  handlerName: string | null
  resolution: string | null
}

// ── Cycle 11: legal publication readiness — GET /api/admin/legal/readiness ─────────────────────────
// NOTE this matches the ACTUAL backend response (ServiceBooking.API/Controllers/AdminLegalController.cs),
// which is a deliberately reduced version of the full contracts/cycle11/legal-status.schema.json: no
// `root`, `links`, `anchors`, `drift`, or per-item `file`; `placeholders` lives only nested under each
// document/uiText (no top-level summary with `source`/`valuePresent`); `impact` is a flat array, not
// `{ reAcceptanceRequired, note }`. See the commit message on AdminLegalController.cs for why
// (ServiceBooking.LegalKit, the CLI that would produce the full shape, doesn't exist in this tree yet).
// Flagged for architect/backend — schema and endpoint currently disagree; do not "fix" one from the
// other without checking which side cycle 11 actually shipped.

export type LegalBlockerKind =
  | 'UnresolvedPlaceholders'
  | 'MissingValues'
  | 'DraftDocuments'
  | 'BrokenLinks'
  | 'MissingAnchors'
  | 'ArtifactDrift'
  | 'LegalUnavailable'

export interface LegalReadinessBlocker {
  kind: LegalBlockerKind
  detail: string
}

export interface LegalReadinessPlaceholderRef {
  name: string
  count: number
}

export interface LegalReadinessDocument {
  type: LegalDocumentType
  title: string
  version: string
  effectiveFrom: string
  isDraft: boolean
  changeKind: LegalChangeKind
  gate: LegalGate
  url: string
  contentHash: string
  placeholders: LegalReadinessPlaceholderRef[]
}

export interface LegalReadinessUiText {
  key: LegalTextKey
  version: string
  isDraft: boolean
  contentHash: string
  placeholders: LegalReadinessPlaceholderRef[]
}

export interface LegalReadinessImpactEntry {
  documentType: LegalDocumentType
  gate: LegalGate
  users: number
}

export interface LegalReadiness {
  generatedAtUtc: string
  ready: boolean
  blockers: LegalReadinessBlocker[]
  documents: LegalReadinessDocument[]
  uiTexts: LegalReadinessUiText[]
  /** Flat list — non-empty only for gated documents (gate !== 'None') where at least one subject's
   *  latest accepted version differs from the manifest's current version. */
  impact: LegalReadinessImpactEntry[]
  /** Mandatory and non-empty even when `ready: true` — must always be shown, never hidden behind an icon. */
  disclaimer: string
}
