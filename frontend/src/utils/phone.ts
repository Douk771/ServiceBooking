/**
 * Formats a canonical phone number (digits only, as stored server-side after US-26 normalization) for
 * display to a human.
 *
 * - 11 digits starting with `7` → `+7 (999) 000-00-00`.
 * - Anything else (other lengths/countries) → `+<digits>` as-is, no further formatting attempted —
 *   international numbering plans vary too much to guess a layout. This branch stays even after
 *   US-61 makes *new* registrations Russian-only (§48.4): legacy non-Russian numbers already in the
 *   database still need to be shown readably, not blanked out.
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

/**
 * ARCHITECTURE_CYCLE15.md §254 — builds a `tel:` URI from whatever canonical/free-form phone the
 * company card has. Keeps only digits and a leading `+`, so `+7 (900) 000-00-00` → `tel:+79000000000`.
 * Lives here, not as a second phone module, per §254 ("there already is one").
 *
 * Empty/missing input → empty string, mirroring {@link formatPhone}, so callers can build
 * `href={telHref(phone) || undefined}` without a separate `if`.
 */
export function telHref(raw: string | null | undefined): string {
  if (!raw) return ''
  const trimmed = raw.trim()
  const hasLeadingPlus = trimmed.startsWith('+')
  const digits = trimmed.replace(/\D/g, '')
  if (!digits) return ''
  return `tel:${hasLeadingPlus ? '+' : ''}${digits}`
}

/**
 * US-61 (ARCHITECTURE_CYCLE6.md §48.1): only the Russian number space is accepted for *new* input —
 * 11 digits with a leading `7`. This is the frontend mirror of the server-side predicate
 * `PhoneNormalizer.IsRussian`; it exists only to give instant feedback in the form, the server check
 * is what actually enforces the rule (§48.2).
 */
export function isRussianPhone(canonical: string): boolean {
  const digits = canonical.replace(/\D/g, '')
  return digits.length === 11 && digits[0] === '7'
}

/**
 * Extracts the "running" canonical digit string from whatever the user just typed or pasted into a
 * masked phone field, enforcing the Russian-only country policy (Q4, §48.1) at input time — not just
 * on submit.
 *
 * - A leading `8` is folded into `7` (the classic Russian dialing convention).
 * - Any other leading digit is treated as a local number missing its country code and gets a `7`
 *   prepended (so typing starts working from the very first digit, e.g. `9`).
 * - An explicit `+` followed by anything other than `7`/`8` is a different country's code — rejected
 *   outright (empty string), so the field never "quietly" accepts `+380…` (Q4, RESOLVED: no foreign
 *   numbers for now).
 * - Result is capped at 11 digits (the full Russian number), so extra typing/pasted digits beyond
 *   that are simply ignored rather than corrupting the mask.
 */
export function toCanonicalPhone(raw: string): string {
  const trimmed = raw.trim()
  let digits = trimmed.replace(/\D/g, '')
  if (!digits) return ''

  if (trimmed.startsWith('+') && digits[0] !== '7' && digits[0] !== '8') return ''

  if (digits[0] === '8') digits = '7' + digits.slice(1)
  else if (digits[0] !== '7') digits = '7' + digits

  return digits.slice(0, 11)
}

/**
 * Whether raw (possibly partial) input typed into a phone field still "looks Russian" — i.e. either
 * has no explicit country code yet (no leading `+`, so it will fall back to `toCanonicalPhone`'s
 * bare-local-number/`8`-folding rules), or has a `+` but either no digits after it yet (still
 * ambiguous — the user just pressed `+`) or a digit sequence starting with `7`/`8` (Russia's own
 * codes). Anything else — a `+` immediately followed by a different country's digit, e.g. `+380…` —
 * is a foreign number and is NOT Russian-shaped.
 *
 * Used by {@link PhoneInput} (§48.4) to decide, per keystroke, whether to keep applying the live
 * `+7 (900) 000-00-00` mask or to fall back to unmangled raw text — see that component for why a
 * naive controlled mask can't tell "not yet typed" apart from "a foreign code" without this check.
 */
export function looksRussian(raw: string): boolean {
  const trimmed = raw.trim()
  if (!trimmed.startsWith('+')) return true
  const digits = trimmed.replace(/\D/g, '')
  if (!digits) return true // bare "+", country code not typed yet — undecided, don't reject
  return digits[0] === '7' || digits[0] === '8'
}

/**
 * Formats a (possibly incomplete) canonical digit string as the user types, growing the mask
 * `+7 (999) 000-00-00` one group at a time instead of waiting for all 11 digits.
 */
function formatPartial(digits: string): string {
  if (!digits) return ''
  const rest = digits.slice(1) // digits after the leading 7
  let out = '+7'
  if (rest.length > 0) {
    out += ` (${rest.slice(0, 3)}`
    if (rest.length >= 3) out += ')'
  }
  if (rest.length > 3) out += ` ${rest.slice(3, 6)}`
  if (rest.length > 6) out += `-${rest.slice(6, 8)}`
  if (rest.length > 8) out += `-${rest.slice(8, 10)}`
  return out
}

/**
 * What a masked phone `<input>` should display for arbitrary raw input (typed one character at a
 * time, pasted in bulk, with stray spaces/dashes/parens, slitno, etc.) — always converging on the
 * same `+7 (900) 000-00-00` shape regardless of how the digits arrived (US-61).
 */
export function maskPhoneInput(raw: string): string {
  return formatPartial(toCanonicalPhone(raw))
}
