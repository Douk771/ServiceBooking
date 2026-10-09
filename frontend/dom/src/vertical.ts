import type { SlotVertical } from '@/components/slots/SlotVerticalContext'
import { serviceOrdersApi } from './api/serviceOrders'
import { publicServicesApi } from './api/publicServices'
import { stayLegalTextsApi } from './api/legalTexts'
import { staysBoardApi } from './api/staysBoard'
import { staysServicesApi } from './api/staysServices'
import { useStaysCompany } from './hooks/useStaysCompany'
import { NotFoundPage } from './pages/NotFoundPage'
import { STAY_FALLBACKS, type StayTextKey } from './utils/stayTexts'

/**
 * The «Дома» vertical for the shared service screens (`src/components/slots`, ARCHITECTURE_CYCLE42.md §42.12.1). Every value gives
 * EXACTLY what dom showed before the move: the routes under `/stays`, the addresses of dom, the keys of `Stay…` texts, the words
 * about houses. `api` points at dom's own thin modules (the same objects the rest of dom uses), so nothing here is a second copy.
 */
export const staysVertical: SlotVertical = {
  kind: 'Stays',
  api: { publicServices: publicServicesApi, orders: serviceOrdersApi, cabinet: staysServicesApi, sessions: staysBoardApi },
  paths: {
    resourcePage: (companySlug, serviceSlug) => `/${companySlug}/uslugi/${serviceSlug}`,
    orderPage: (token) => `/s/${encodeURIComponent(token)}`,
    cabinetServices: (companyId) => `/cabinet/${companyId}/services`,
    cabinetServiceNew: (companyId) => `/cabinet/${companyId}/services/new`,
    cabinetService: (companyId, serviceId) => `/cabinet/${companyId}/services/${serviceId}`,
    cabinetSession: (companyId, sessionId) => `/cabinet/${companyId}/service-sessions/${sessionId}`,
    cabinetDay: (companyId, date) => `/cabinet/${companyId}/service-day/${date}`,
    cabinetBooking: (companyId, bookingId) => `/cabinet/${companyId}/bookings/${bookingId}`,
  },
  legal: {
    keys: {
      serviceBookingNotice: 'StayServiceBookingNotice',
      serviceBookingTerms: 'StayServiceBookingTerms',
      serviceCancellationTerms: 'StayServiceCancellationTerms',
      serviceCommentNotice: 'StayServiceCommentNotice',
      messengerConsent: 'StayMessengerConsent',
      paymentProofNotice: 'StayPaymentProofNotice',
      serviceAddNotice: 'StayServiceAddNotice',
      serviceSafetyOwnerNotice: 'StayServiceSafetyOwnerNotice',
      serviceCancellationOwnerNotice: 'StayServiceCancellationOwnerNotice',
      photoPeopleNotice: null,
      capacityOwnerNotice: null,
    },
    fallbacks: STAY_FALLBACKS,
    // read at call time: a test replaces `stayLegalTextsApi` by a mock module
    fetchText: (key) => stayLegalTextsApi.get(key as StayTextKey),
  },
  words: {
    brandTitle: 'ezbook · Дома',
    brandName: 'ezbook Дома',
    chooseResource: 'Выбрать дом',
    notOrderingFallback: 'Можно добавить к брони дома',
    servicesEmptyText: 'Добавьте первую: название, потом расписание, цены и правила. Опубликованную услугу можно будет добавлять к броням домов.',
    stayBookingLabel: 'Бронь дома',
  },
  features: { stayMode: true, capacity: false, houseBookingsToggle: true, photoPeopleNotice: false },
  NotFound: NotFoundPage,
  useCabinetCompany: useStaysCompany,
}
