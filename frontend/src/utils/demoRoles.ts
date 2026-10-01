import type { DemoRole } from '../api/demo'

/** API_CONTRACT_CYCLE35.md §35.22 — where each demo role lands after signing in (ezbook: cycle 28, goods: cycle 35). */
const DEMO_ROLE_HOME: Record<DemoRole, string> = {
  owner: '/cabinet',
  master: '/my-bookings',
  client: '/my-visits',
  'shop-owner': '/cabinet',
  'shop-staff': '/cabinet',
  'shop-customer': '/orders',
}

export function demoRoleHome(role: DemoRole): string {
  return DEMO_ROLE_HOME[role]
}
