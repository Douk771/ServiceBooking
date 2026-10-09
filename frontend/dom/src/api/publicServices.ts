import { slotApiStays } from './slotApiStays'

export type { StayRangeParams } from '@/api/slots'

/** Anonymous routes of a service, policy `stays-public` (API_CONTRACT_CYCLE39.md §39.22). */
export const publicServicesApi = slotApiStays.publicServices
