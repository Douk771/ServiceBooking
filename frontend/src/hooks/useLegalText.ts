import { useQuery } from '@tanstack/react-query'
import { legalApi } from '../api/legal'
import type { LegalTextKey } from '../types'

/** `GET /api/legal/texts/{key}` (API_CONTRACT_CYCLE5.md §39.3), cached the same 5 minutes as the
 *  server itself caches it. Shared by every screen that shows one of the D5/D7/D8/D10/D11/D12
 *  microcopy texts, so the query key/caching behaviour doesn't drift between call sites. */
export function useLegalText(key: LegalTextKey) {
  return useQuery({
    queryKey: ['legal-text', key],
    queryFn: () => legalApi.getText(key),
    staleTime: 5 * 60 * 1000,
  })
}
