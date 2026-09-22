/**
 * API_CONTRACT_CYCLE5.md §47.1, §56.5 п. 4 — `adMarkers` always comes from the server response
 * (`GET /api/companies/{id}/notification-templates`), never hardcoded here, so the client-side
 * highlight and the server's own check agree on the same text without a frontend release when the
 * dictionary changes.
 */

/** Case-insensitive substring match, same rule the server description implies (§51.2: simple
 *  dictionary substrings, e.g. "скидк" matches "скидка"/"скидки"). Returns the markers that hit, in
 *  the order they were given, without duplicates. */
export function findHitMarkers(text: string, markers: string[]): string[] {
  const lower = text.toLowerCase()
  return markers.filter((m) => lower.includes(m.toLowerCase()))
}
