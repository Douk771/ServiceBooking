import { useEffect, useRef } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { bathsCabinetApi } from '../api/bathsCabinet'
import { REVISION_POLL_MS, revisionMoved, revisionQueryKeys } from './revision'

/** Polls the revision of the company's bookings while the tab is open and refreshes what depends on it when it moves. */
export function useBookingsRevision(companyId: string, enabled: boolean) {
  const qc = useQueryClient()
  const last = useRef<number | null>(null)
  const q = useQuery({
    queryKey: ['baths-revision', companyId],
    queryFn: () => bathsCabinetApi.revision(companyId),
    enabled,
    refetchInterval: REVISION_POLL_MS,
    refetchOnWindowFocus: true, // a tab that becomes visible again asks at once (`visibilitychange`)
    staleTime: 0,
    retry: false,
  })
  const revision = q.data?.revision
  useEffect(() => {
    if (revision == null) return
    if (revisionMoved(last.current, revision)) for (const key of revisionQueryKeys(companyId)) void qc.invalidateQueries({ queryKey: key })
    last.current = revision
  }, [revision, companyId, qc])
}
