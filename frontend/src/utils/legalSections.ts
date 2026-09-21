/**
 * Some `GET /api/legal/texts/{key}` documents (D5, D7) are authored as several `<h2>`-delimited
 * sections meant for *different* places in the UI — e.g. `TemplateAdWarning` (D7) has one section for
 * the persistent field warning, one for the heightened warning on a marker hit, one for the save
 * confirmation, and a reference block (API_CONTRACT_CYCLE5.md §39.3, ARCHITECTURE_CYCLE5.md T5-F7).
 * The manifest doesn't split these into separate fields, so the frontend does it by heading text.
 *
 * This is deliberately forgiving: `findSection` returns `null` on no match rather than throwing, and
 * every call site falls back to showing the *whole* document when its expected section isn't found —
 * showing the lawyer's text in the wrong spot is a lesser defect than silently dropping it because a
 * heading was reworded during legal review.
 */
export interface LegalSection {
  heading: string
  html: string
}

/** Splits `contentHtml` on `<h2>...</h2>` boundaries. Content before the first `<h2>` (title/meta
 *  paragraphs) is discarded — it is implementer commentary, not UI copy (see the discrepancy noted in
 *  the cycle report). Case-sensitive on tag name only; heading text is returned as authored. */
export function splitLegalSections(html: string): LegalSection[] {
  const sections: LegalSection[] = []
  const re = /<h2[^>]*>(.*?)<\/h2>/gis
  const matches = [...html.matchAll(re)]

  for (let i = 0; i < matches.length; i++) {
    const match = matches[i]
    const heading = match[1].replace(/<[^>]+>/g, '').trim()
    const start = (match.index ?? 0) + match[0].length
    const end = i + 1 < matches.length ? (matches[i + 1].index ?? html.length) : html.length
    sections.push({ heading, html: html.slice(start, end).trim() })
  }

  return sections
}

/** Case/whitespace-insensitive substring match on heading text — legal text is edited by hand, so an
 *  exact match would break on a stray space or a rephrased word. */
export function findSection(sections: LegalSection[], headingSubstring: string): LegalSection | null {
  const needle = headingSubstring.trim().toLowerCase()
  return sections.find((s) => s.heading.toLowerCase().includes(needle)) ?? null
}
