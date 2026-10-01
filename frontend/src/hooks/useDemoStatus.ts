import { useQuery } from '@tanstack/react-query'
import { demoApi } from '../api/demo'
import { useDemoProduct } from '../components/demo/DemoProductContext'

export const DEMO_STATUS_POLL_MS = 15_000

/**
 * `GET /api/demo/status` (API_CONTRACT_CYCLE35.md §35.21). The product (whose roles to ask for) comes from
 * `DemoProductProvider`, not from the page address; without a provider it is `services` and the request is the cycle-28
 * one. The answer is cached for the whole session
 * (`staleTime: Infinity`): it only says "is this the demo stand" and which roles exist. `poll: true` (used only by the
 * "Демо обновляется" screen) re-asks every 15 s to learn when the reset is over.
 *
 * `isDemo` is `false` while the first answer is in flight, on 404 (production) and on any failure — the banner and the
 * role buttons then simply do not appear, production never shows a half-demo UI.
 */
export function useDemoStatus(options?: { poll?: boolean }) {
  const product = useDemoProduct()
  const query = useQuery({
    queryKey: ['demo-status', product],
    queryFn: () => demoApi.getStatus(product),
    staleTime: Infinity,
    retry: 2,
    refetchInterval: options?.poll ? DEMO_STATUS_POLL_MS : false,
  })
  const status = query.data ?? null
  return { ...query, status, isDemo: status?.demoMode === true }
}
