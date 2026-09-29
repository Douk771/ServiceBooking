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
 * `sessionStorage` (not `localStorage`) is deliberate — this is scratch state for "the form I just
 * printed in this browser tab", not something that should survive after the tab closes or leak across
 * devices/sessions; it also never touches the wire, so it carries no privacy weight of its own.
 */
function storageKey(companyId: string, clientKey: string): string {
  return `health-consent-form:${companyId}:${clientKey}`
}

export interface LastPrintedHealthForm {
  formId: string | null
  textVersion: string
}

export function saveLastPrintedHealthForm(companyId: string, clientKey: string, form: LastPrintedHealthForm): void {
  try {
    sessionStorage.setItem(storageKey(companyId, clientKey), JSON.stringify(form))
  } catch {
    // Private-browsing/storage-full failures are a UX inconvenience (the mark dialog just opens with
    // an empty formId, same as "used the salon's own form"), never a reason to break printing.
  }
}

export function getLastPrintedHealthForm(companyId: string, clientKey: string): LastPrintedHealthForm | null {
  try {
    const raw = sessionStorage.getItem(storageKey(companyId, clientKey))
    if (!raw) return null
    const parsed = JSON.parse(raw) as Partial<LastPrintedHealthForm>
    if (typeof parsed.textVersion !== 'string') return null
    return { formId: typeof parsed.formId === 'string' ? parsed.formId : null, textVersion: parsed.textVersion }
  } catch {
    return null
  }
}
