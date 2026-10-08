/** MAX linking is polled only while a link is pending and not yet expired (API_CONTRACT_CYCLE25.md §523.2, §538). */
export function shouldPollStaffMax(status: string | undefined, expiresAtUtc: string | null | undefined, nowMs: number): boolean {
  if (status !== 'Pending') return false
  if (!expiresAtUtc) return true
  return new Date(expiresAtUtc).getTime() > nowMs
}
