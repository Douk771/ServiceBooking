import { useQuery } from '@tanstack/react-query'
import { companiesApi } from '../api/companies'
import { useAuthStore } from '../store/authStore'

/**
 * Cycle 49 (ARCHITECTURE_CYCLE49 §49.5.1, US-49-05): true when the signed-in user owns at least one active salon.
 * `CompanyOwner` is in the token for owners of every line, so the answer comes from `GET /companies/my` (salons only, shared
 * cache key with the cabinet). No request without the role; while loading or failed — false.
 */
export function useOwnsSalon(): boolean {
  const { isAuthenticated, hasRole } = useAuthStore()
  const enabled = isAuthenticated() && hasRole('CompanyOwner')
  const { data } = useQuery({ queryKey: ['my-companies'], queryFn: () => companiesApi.getMy(), enabled, staleTime: 5 * 60_000 })
  return enabled && (data?.length ?? 0) > 0
}
