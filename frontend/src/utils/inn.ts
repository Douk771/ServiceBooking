/**
 * API_CONTRACT_CYCLE5.md §50.1 (ПЛ5) — the server's own INN check is formal (length + checksum, no
 * ЕГРЮЛ/ЕГРИП lookup). The frontend mirrors only the length rule, cheap enough to catch a typo before
 * a round trip; the checksum failure message comes back from the server verbatim
 * ("ИНН указан неверно, проверьте цифры") rather than being duplicated here.
 */
export function isPlausibleInn(value: string): boolean {
  const digits = value.replace(/\D/g, '')
  return digits.length === 10 || digits.length === 12
}
