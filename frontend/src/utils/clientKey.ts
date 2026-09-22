/**
 * API_CONTRACT_CYCLE5.md §44.1, §45.1 — `clientKey` is a `userId` for a registered client, or the
 * canonical `phone:79991234567` form for a guest. Shared so every call site (health note, photo
 * consent) builds the same key from the same client shape.
 */
export function getClientKey(client: { clientId: string | null; guestPhone: string | null }): string {
  if (client.clientId) return client.clientId
  return `phone:${client.guestPhone ?? ''}`
}
