import { useOutletContext } from 'react-router-dom'
import type { SlotCabinetCompany, SlotVertical } from '@/components/slots/SlotVerticalContext'
import { bathsVertical } from '../vertical'
import { NotFoundPage } from '../pages/NotFoundPage'
import { CABINET_FALLBACKS, RESOURCE_ONE_PLACE_HINT } from './cabinetTexts'
import type { BathsCompanyManageDto } from './types'

export interface CabinetOutletContext {
  company: BathsCompanyManageDto
  refresh: () => void
}

/** The company of `/cabinet/:companyId/*`, loaded once by `CompanyLayout`. */
export function useBathsCompany(): CabinetOutletContext {
  return useOutletContext<CabinetOutletContext>()
}

const useCabinetCompany = (): SlotCabinetCompany => useBathsCompany()

/**
 * The «Бани» vertical for the shared SERVICE SCREENS OF THE CABINET (resources, «День услуг», card of a booking, manual booking).
 * Built on `bathsVertical` of FE-42-2 (api, brand). Until FE-42-3/4 give `bathsVertical` its own `paths`/`legal`/`words`/`features`, this
 * is the one place that spells them for the cabinet; the guest keys are named as in ARCHITECTURE_CYCLE42.md §42.11.3 and have no
 * fallbacks here because no cabinet screen renders them (FE-42-4/8 own them).
 */
export const bathsCabinetVertical: SlotVertical = {
  kind: 'Baths',
  api: bathsVertical.api,
  paths: {
    resourcePage: (companySlug, serviceSlug) => `/${companySlug}/${serviceSlug}`,
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
      serviceAddNotice: 'BathBookingNotice',
      serviceSafetyOwnerNotice: 'StayServiceSafetyOwnerNotice',
      serviceCancellationOwnerNotice: 'StayServiceCancellationOwnerNotice',
      photoPeopleNotice: 'CompanyPhotoPeopleNotice',
      capacityOwnerNotice: 'BathCapacityOwnerNotice',
      positionsOwnerNotice: 'BathPositionsOwnerNotice',
    },
    fallbacks: CABINET_FALLBACKS,
  },
  words: {
    brandTitle: 'EZBOOK Бани',
    brandName: 'EZBOOK Бани',
    chooseResource: 'К комплексу',
    notOrderingFallback: '',
    servicesEmptyText: 'Добавьте первую баню или купель: название, потом расписание, цены и правила. Опубликованный ресурс увидят гости.',
    stayBookingLabel: '',
    serviceCreateHint: RESOURCE_ONE_PLACE_HINT,
  },
  features: { stayMode: false, capacity: true, houseBookingsToggle: false, photoPeopleNotice: true },
  NotFound: NotFoundPage,
  useCabinetCompany,
}
