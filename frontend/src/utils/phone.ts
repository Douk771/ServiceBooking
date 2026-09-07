/**
 * Formats a canonical phone number (digits only, as stored server-side after US-26 normalization) for
 * display to a human.
 *
 * - 11 digits starting with `7` → `+7 (999) 000-00-00`.
 * - Anything else (other lengths/countries) → `+<digits>` as-is, no further formatting attempted —
 *   international numbering plans vary too much to guess a layout.
 * - Empty/missing input → empty string, so callers can interpolate this directly without an `if`.
 *
 * Input is NOT re-normalized here — the server is the single source of truth for the canonical form
 * (API_CONTRACT.md §12). This function only changes how it's displayed.
 */
export function formatPhone(canonical: string | null | undefined): string {
  if (!canonical) return ''
  const digits = canonical.replace(/\D/g, '')
  if (!digits) return ''

  if (digits.length === 11 && digits[0] === '7') {
    return `+7 (${digits.slice(1, 4)}) ${digits.slice(4, 7)}-${digits.slice(7, 9)}-${digits.slice(9, 11)}`
  }

  return `+${digits}`
}
