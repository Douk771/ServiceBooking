import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { platformNoticesApi } from '../api/platformNotices'

/** API_CONTRACT_CYCLE20.md §434.1 — shared by the banner (`scope: 'pending'`) and `/notices`/the
 *  billing-page block (`scope: 'all'`), so the two never drift on caching/staleness behaviour. */
export function usePlatformNotices(scope: 'pending' | 'all') {
  return useQuery({
    queryKey: ['platform-notices', scope],
    queryFn: () => platformNoticesApi.getNotices(scope),
    staleTime: 5 * 60 * 1000,
  })
}

/** §434.2 — idempotent on the server; invalidates BOTH scopes (an acknowledged notice leaves
 *  `pending` and its `acknowledged` flag flips in `all`) rather than trying to patch either cache by
 *  hand. */
export function useAcknowledgeNotice() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => platformNoticesApi.acknowledge(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['platform-notices'] }),
  })
}
