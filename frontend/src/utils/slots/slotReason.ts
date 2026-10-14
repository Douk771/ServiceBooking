/** The reason of an owner action on a booking/session. It is shown to the GUEST — hence the wording (API_CONTRACT_CYCLE37.md §37.30.3). */
export const REASON_MAX = 300
export const REASON_REQUIRED_TEXT = 'Укажите причину — гость её увидит'

export function reasonProblem(reason: string): string | null {
  const r = reason.trim()
  return r.length >= 1 && r.length <= REASON_MAX ? null : REASON_REQUIRED_TEXT
}
