import { useCallback } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { scheduleApi } from '../api/schedule'

export const ORDERING_STATUS_POLL_MS = 60_000

/** `GET …/ordering-status`, polled every 60 s and refetched right after the staff's own `PUT`s (API_CONTRACT_CYCLE24.md §476). */
export function useOrderingStatus(shopId: string) {
  const qc = useQueryClient()
  const query = useQuery({
    queryKey: ['ordering-status', shopId],
    queryFn: () => scheduleApi.orderingStatus(shopId),
    refetchInterval: ORDERING_STATUS_POLL_MS,
    refetchIntervalInBackground: false,
  })
  const refresh = useCallback(() => qc.invalidateQueries({ queryKey: ['ordering-status', shopId] }), [qc, shopId])
  return { ...query, refresh }
}
