/**
 * Auto-refresh by `revision` (US-42-10, ARCHITECTURE_CYCLE42.md §42.12.2): the cabinet polls `GET …/revision` (at most every 30 s,
 * at once when the tab becomes visible again) and re-reads the day, the list, the card and the schedule only when the number moved.
 */
export const REVISION_POLL_MS = 30_000

/** The very first answer is a baseline, not a change; so is the same number again. */
export function revisionMoved(previous: number | null | undefined, next: number | null | undefined): boolean {
  if (previous == null || next == null) return false
  return previous !== next
}

/** Query keys that go stale when the revision moves (the keys the shared screens already use, plus bani's own). */
export function revisionQueryKeys(companyId: string): (readonly string[])[] {
  return [
    ['stays-service-day', companyId],
    ['stays-service-sessions', companyId],
    ['stays-service-session', companyId],
    ['baths-schedule', companyId],
    ['baths-company', companyId],
  ]
}
