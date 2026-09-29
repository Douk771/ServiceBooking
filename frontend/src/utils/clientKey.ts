/**
 * API_CONTRACT_CYCLE5.md §44.1, §45.1 — `clientKey` is a `userId` for a registered client, or the
 * canonical `phone:79991234567` form for a guest. Shared so every call site (health note, photo
 * consent) builds the same key from the same client shape.
 */
export function getClientKey(client: { clientId: string | null; guestPhone: string | null }): string {
  if (client.clientId) return client.clientId
  return `phone:${client.guestPhone ?? ''}`
}

/** ARCHITECTURE_CYCLE20.md §414 — the one route both `HealthNoteCard` (link out) and
 *  `HealthConsentFormPrintPage` (route match, via `useParams`) must agree on. `clientKey` can contain
 *  a `phone:79991234567` guest key, hence the encode/decode pair — never build or parse this path
 *  ad hoc at a call site. */
export function healthConsentFormPrintPath(companyId: string, clientKey: string): string {
  return `/companies/${companyId}/clients/${encodeURIComponent(clientKey)}/health-consent-form`
}
