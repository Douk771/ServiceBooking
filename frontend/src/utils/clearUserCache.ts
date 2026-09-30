import type { QueryClient } from '@tanstack/react-query'

/** The demo-stand flag belongs to the deployment, not to the signed-in user. */
const DEPLOYMENT_QUERY_KEYS = new Set<unknown>(['demo-status'])

/**
 * Drops every cached query of the previous user on sign-in / sign-out. Unlike `qc.clear()` it keeps
 * `['demo-status']`: wiping it makes the demo banner and `noindex` meta disappear until the status is refetched.
 */
export function clearUserCache(qc: QueryClient): void {
  qc.removeQueries({ predicate: (q) => !DEPLOYMENT_QUERY_KEYS.has(q.queryKey[0]) })
}
