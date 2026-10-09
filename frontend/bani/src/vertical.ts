import { createSlotApi } from '@/api/slots'
import type { PushSite } from '@/api/push'
import type { SlotVertical } from '@/components/slots/SlotVerticalContext'
import { useBathsCompany } from './cabinet/useBathsCompany'
import { NotFoundPage } from './pages/NotFoundPage'
import { BATH_FALLBACKS } from './utils/baniTexts'

/**
 * `bathsVertical` — the bani description of the slot vertical (ARCHITECTURE_CYCLE42.md §42.12.2): addresses of bani-routes.json,
 * keys of `Bath…` texts, the words about baths. Do not grow it with anything but these fields.
 */
export const bathsVertical: SlotVertical & {
  /** The one spelling of the brand (header, footer, manifest, <title>, push). */
  brand: string
  appName: string
  pushSite: PushSite
} = {
  kind: 'Baths',
  brand: 'EZBOOK Бани',
  appName: 'Бани',
  pushSite: 'Baths' as PushSite,
  api: createSlotApi('/baths'),
  paths: {
    resourcePage: (companySlug, resourceSlug) => `/${companySlug}/${resourceSlug}`,
    orderPage: (token) => `/s/${encodeURIComponent(token)}`,
    cabinetServices: (companyId) => `/cabinet/${companyId}/resources`,
    cabinetServiceNew: (companyId) => `/cabinet/${companyId}/resources/new`,
    cabinetService: (companyId, serviceId) => `/cabinet/${companyId}/resources/${serviceId}`,
    cabinetSession: (companyId, sessionId) => `/cabinet/${companyId}/service-sessions/${sessionId}`,
    cabinetDay: (companyId, date) => `/cabinet/${companyId}/service-day/${date}`,
    cabinetBooking: () => null,
  },
  legal: {
    keys: {
      serviceBookingNotice: 'BathBookingNotice',
      serviceBookingTerms: 'BathBookingTerms',
      serviceCancellationTerms: 'StayServiceCancellationTerms',
      serviceCommentNotice: 'StayServiceCommentNotice',
      messengerConsent: 'StayMessengerConsent',
      paymentProofNotice: 'BathPaymentProofNotice',
      // dom only (a service added to a stay); bani never shows it, so no key of houses is ever asked for
      serviceAddNotice: 'BathBookingNotice',
      serviceSafetyOwnerNotice: 'StayServiceSafetyOwnerNotice',
      serviceCancellationOwnerNotice: 'StayServiceCancellationOwnerNotice',
      photoPeopleNotice: null,
      capacityOwnerNotice: 'BathCapacityOwnerNotice',
    },
    fallbacks: BATH_FALLBACKS,
    // fetchText omitted: the shared reader of `GET /api/legal/texts/{key}`
  },
  words: {
    brandTitle: 'EZBOOK Бани',
    brandName: 'EZBOOK Бани',
    chooseResource: 'Выбрать баню',
    notOrderingFallback: 'Заказывается вместе с баней',
    servicesEmptyText: 'Добавьте первую баню: название, вместимость, расписание, цены и правила. Опубликованная баня появится в каталоге.',
    stayBookingLabel: 'Бронь',
  },
  features: { stayMode: false, capacity: true, houseBookingsToggle: false, photoPeopleNotice: true },
  NotFound: NotFoundPage,
  useCabinetCompany: useBathsCompany,
}

export type BathsVertical = typeof bathsVertical
