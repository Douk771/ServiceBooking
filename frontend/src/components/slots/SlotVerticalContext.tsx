import { createContext, useContext, type ComponentType, type ReactNode } from 'react'
import type { createSlotApi } from '@/api/slots'
import { useLegalText, type ServerLegalText } from '@/hooks/slots/useLegalText'
import type { SlotText } from '@/utils/slots/slotTexts'

/** The routes of one vertical: dom builds them with `createSlotApi('/stays')`, bani with `createSlotApi('/baths')`. */
export type SlotApi = ReturnType<typeof createSlotApi>

/**
 * The legal microcopy the shared service screens show, by role. The values are the vertical's own keys (dom: `StayService…`, bani:
 * `Bath…`) — a shared screen never spells a vertical's key.
 */
export interface SlotLegalKeys {
  /** Under the order button, with a link to the conditions. */
  serviceBookingNotice: string
  serviceBookingTerms: string
  serviceCancellationTerms: string
  /** Under the comment field. */
  serviceCommentNotice: string
  /** Under the «notify me in a messenger» tick. */
  messengerConsent: string
  /** Next to the upload of the proof of payment. */
  paymentProofNotice: string
  /** Under the picker that adds a service to a stay (dom only). */
  serviceAddNotice: string
  /** Owner hints of the cabinet. */
  serviceSafetyOwnerNotice: string
  serviceCancellationOwnerNotice: string
  /** «No people in the frame» next to the photo upload; null when the vertical has none (`features.photoPeopleNotice`). */
  photoPeopleNotice: string | null
  /** Under the capacity field of the cabinet; null when the vertical has no capacity. */
  capacityOwnerNotice: string | null
}

export interface SlotCabinetCompany {
  company: { id: string; myPermissions: readonly string[] }
  /** Re-reads the company (after a save that changes the checklist, the gate, the plan or the rights). */
  refresh: () => void
}

/** Where the shared screens link to — each vertical has its own address plan. */
export interface SlotPaths {
  resourcePage(companySlug: string, serviceSlug: string): string
  orderPage(token: string): string
  cabinetServices(companyId: string): string
  cabinetServiceNew(companyId: string): string
  cabinetService(companyId: string, serviceId: string): string
  cabinetSession(companyId: string, sessionId: string): string
  cabinetDay(companyId: string, date: string): string
  /** The card of a stay booking a session belongs to; null when the vertical has no stays. */
  cabinetBooking(companyId: string, bookingId: string): string | null
}

/** The words that differ between verticals. Only what a shared screen cannot say neutrally. */
export interface SlotWords {
  /** `document.title` when no entity is loaded, and the suffix of an entity title. */
  brandTitle: string
  /** «<service> — <company> · <brandName>» */
  brandName: string
  /** Link to the list of places of the company on the page of a service that is not ordered on its own. */
  chooseResource: string
  /** The «not ordered separately» line (server text first). */
  notOrderingFallback: string
  /** Empty list of the cabinet. */
  servicesEmptyText: string
  /** Label of the link from a session to the stay booking. */
  stayBookingLabel: string
}

export interface SlotFeatures {
  /** Sessions can belong to a stay booking (dom): the link to the booking, «available for house bookings». */
  stayMode: boolean
  /** The place has a capacity: «up to N people», the guests field on the order form. */
  capacity: boolean
  houseBookingsToggle: boolean
  /** «No people in the frame» next to the photos of the place. */
  photoPeopleNotice: boolean
}

export interface SlotVertical {
  kind: 'Stays' | 'Baths'
  api: SlotApi
  paths: SlotPaths
  legal: {
    keys: SlotLegalKeys
    fallbacks: Record<string, SlotText>
    /** The reader of the server text; dom passes its own, bani uses the shared one. */
    fetchText?: (key: string) => Promise<ServerLegalText>
  }
  words: SlotWords
  features: SlotFeatures
  /** The «not found» screen of the app. */
  NotFound: ComponentType<{ title?: string; hint?: string }>
  /** The company of `/cabinet/:companyId/*` (a hook of the app's layout). */
  useCabinetCompany: () => SlotCabinetCompany
}

const Ctx = createContext<SlotVertical | null>(null)

export function SlotVerticalProvider({ value, children }: { value: SlotVertical; children: ReactNode }) {
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}

/** The vertical the shared screens run in. Without a provider it is a programming error, not a state to render. */
export function useSlotVertical(): SlotVertical {
  const v = useContext(Ctx)
  if (!v) throw new Error('useSlotVertical must be used inside <SlotVerticalProvider> (dom: staysVertical, bani: bathsVertical)')
  return v
}

/** The legal microcopy `key` of the current vertical (server text or its fallback). */
export function useSlotText(key: string) {
  const { legal } = useSlotVertical()
  return useLegalText(key, legal.fallbacks, legal.fetchText)
}

/** The company of the cabinet the screen is in. */
export function useCabinetCompany(): SlotCabinetCompany {
  return useSlotVertical().useCabinetCompany()
}

/**
 * Wraps a shared component so it renders in the given vertical without a provider above it. dom's thin re-exports use it: an old
 * import path (`components/StayNotice`) keeps working in a test that renders a single component.
 */
export function inVertical<P extends object>(value: SlotVertical, Component: ComponentType<P>): ComponentType<P> {
  const Wrapped = (props: P) => (
    <SlotVerticalProvider value={value}>
      <Component {...props} />
    </SlotVerticalProvider>
  )
  Wrapped.displayName = `inVertical(${Component.displayName ?? Component.name})`
  return Wrapped
}
