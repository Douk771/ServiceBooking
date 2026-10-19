import { useQuery } from '@tanstack/react-query'
import { adminBillingApi } from '../../api/adminBilling'

// Cycle 43 (ARCHITECTURE_CYCLE43.md §43.9): one request and one cache entry shared by the price panel and the hints next to it.
export const MESSENGER_OPTIONS_QUERY_KEY = ['admin', 'messenger-options'] as const

export function useMessengerOptions() {
  return useQuery({ queryKey: MESSENGER_OPTIONS_QUERY_KEY, queryFn: () => adminBillingApi.messengerOptions() })
}
