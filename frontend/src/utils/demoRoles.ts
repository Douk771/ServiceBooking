import type { DemoRole } from '../api/demo'

/** API_CONTRACT_CYCLE28.md §598 — where each demo role lands after signing in. */
const DEMO_ROLE_HOME: Record<DemoRole, string> = {
  owner: '/cabinet',
  master: '/my-bookings',
  client: '/my-visits',
}

export function demoRoleHome(role: DemoRole): string {
  return DEMO_ROLE_HOME[role]
}
