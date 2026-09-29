/**
 * API_CONTRACT_CYCLE20.md §432.4/§441 item 2 (US-20-01) — the printable paper-form page substitutes
 * an underline for every runtime value the system doesn't have, so the printed sheet keeps a line for
 * the client/salon to fill in by hand instead of the field silently vanishing (`data-legal-when`
 * blocks would otherwise just disappear for a `null` value, per `applyLegalRuntimeValues`).
 *
 * 32 underscores — long enough to write a full name or a short address on the printed A4 line, short
 * enough not to wrap oddly inside the form's existing table/paragraph layout.
 */
export const BLANK_LINE = '_'.repeat(32)

/** Turns `{ operatorInn: null, clientFullName: 'Мария Иванова' }` into the object
 *  `applyLegalRuntimeValues` expects, with every `null` replaced by {@link BLANK_LINE}. Never touches
 *  a present (non-null) value — including an accidental empty string, which stays empty rather than
 *  being "upgraded" to a blank line (an explicit empty string is a different fact than "no data"). */
export function blankLineForPrint(values: Record<string, string | null | undefined>): Record<string, string> {
  return Object.fromEntries(Object.entries(values).map(([key, value]) => [key, value ?? BLANK_LINE]))
}

/**
 * ARCHITECTURE_CYCLE20.md §432.4 (US-20-01) — "фронт запоминает последний напечатанный `formId`" is a
 * page-to-page handoff (print page → mark-consent dialog back on the client card), not a server
 * concept: `GET …/health-consent-form` issues a FRESH `formId` on every call and never persists it.
 *
 * §441 item 2 is explicit that `formId` must never be kept "anywhere except the print page's own
 * memory" — that rules out `sessionStorage`/`localStorage` (inspectable via devtools, and survives a
 * full reload of the tab), and it rules out keying anything by the raw `clientKey`, which for a guest
 * client is a `phone:7999…` string that would otherwise sit in browser storage. This is a plain
 * in-memory cache (a module-level `Map`, not a Web Storage API): it lives only for the lifetime of
 * this browser tab's current page load, is invisible to devtools' Application/Storage panel, never
 * touches the wire, and is gone on the next full reload — the closest an SPA can get to "print page's
 * own memory" while still letting the mark-consent dialog on a *different* route read what was just
 * printed without a server round trip.
 */
const lastPrintedForms = new Map<string, LastPrintedHealthForm>()

function cacheKey(companyId: string, clientKey: string): string {
  return `${companyId}:${clientKey}`
}

export interface LastPrintedHealthForm {
  formId: string | null
  textVersion: string
}

export function saveLastPrintedHealthForm(companyId: string, clientKey: string, form: LastPrintedHealthForm): void {
  lastPrintedForms.set(cacheKey(companyId, clientKey), form)
}

export function getLastPrintedHealthForm(companyId: string, clientKey: string): LastPrintedHealthForm | null {
  return lastPrintedForms.get(cacheKey(companyId, clientKey)) ?? null
}

/** Test-only: the in-memory cache above is intentionally module-scoped (not Web Storage), so tests
 *  that rely on a clean slate between cases need an explicit reset instead of `sessionStorage.clear()`. */
export function clearLastPrintedHealthFormsForTests(): void {
  lastPrintedForms.clear()
}
