/**
 * ARCHITECTURE_CYCLE15.md §256 — per-render substitution of values that vary PER BOOKING (the
 * company name), which is why they can't be `{{PLACEHOLDER}}`s resolved at `legal publish` time
 * (one value for the whole product) or on the server (the `GET /api/legal/texts/{key}` response is
 * shared across everyone for 5 minutes, §256.1).
 *
 * The lawyer marks the substitution spot with HTML attributes, not curly braces, so
 * `LegalDocumentProvider.PlaceholderPattern`/`PlaceholderScanner` never see it and publication is
 * unaffected (§256.2):
 *
 *   <span data-legal-value="companyName"></span>        — replaced with the escaped value
 *   <span data-legal-when="companyName">…</span>         — kept only when the value is known/non-empty
 *   <span data-legal-unless="companyName">…</span>       — kept only when the value is NOT known
 *
 * This is the ONLY place that performs this substitution (§256.3) — a pure string→string function,
 * no DOM, applied fresh on every render (the cached legal text itself is never mutated), which is
 * what makes "two different companies in the same session" safe by construction rather than by
 * discipline (R2).
 *
 * Resolution order: `data-legal-value` spans are resolved first (they're always empty and may sit
 * nested inside a `when`/`unless` block's text), which turns any such block back into plain text
 * before the `when`/`unless` regexes run — so those never have to reason about nested tags.
 */
export const LEGAL_RUNTIME_VALUE_NAMES = ['companyName'] as const
export type LegalRuntimeValueName = (typeof LEGAL_RUNTIME_VALUE_NAMES)[number]

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
}

/** Matches `<span data-legal-when="name">…inner…</span>` (or `data-legal-unless`), non-greedy on the
 *  inner content, single level — the markup never nests `when`/`unless` inside one another. */
function conditionalPattern(attr: 'data-legal-when' | 'data-legal-unless'): RegExp {
  return new RegExp(`<span ${attr}="([a-zA-Z]+)">([\\s\\S]*?)</span>`, 'g')
}

const VALUE_PATTERN = /<span data-legal-value="([a-zA-Z]+)"><\/span>/g

export function applyLegalRuntimeValues(
  html: string,
  values: Partial<Record<LegalRuntimeValueName, string | null | undefined>>,
): string {
  const isKnown = (name: string) => {
    const v = (values as Record<string, string | null | undefined>)[name]
    return v != null && v !== ''
  }

  // `data-legal-value` spans are always empty (`<span data-legal-value="x"></span>`) and can nest
  // inside a `when`/`unless` block's text — resolving them FIRST turns any such block back into
  // plain text, so the `when`/`unless` regexes below never have to worry about nested tags.
  let out = html.replace(VALUE_PATTERN, (match, name: string) => {
    const v = (values as Record<string, string | null | undefined>)[name]
    // Unknown value name (not in `values` at all) is left untouched — front doesn't break on it,
    // `RuntimeValueScanner` (§256.5) catches an unknown/misspelled name at build time instead.
    if (!(name in values)) return match
    return v != null ? escapeHtml(v) : ''
  })
  out = out.replace(conditionalPattern('data-legal-when'), (_match, name: string, inner: string) =>
    isKnown(name) ? inner : '',
  )
  out = out.replace(conditionalPattern('data-legal-unless'), (_match, name: string, inner: string) =>
    isKnown(name) ? '' : inner,
  )
  return out
}
