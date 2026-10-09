import { findSection, splitLegalSections } from '@/utils/legalSections'

/** A legal microcopy of a booking vertical: the always-visible part and the optional expandable one. */
export interface SlotText {
  /** Always-visible part (for texts without sections, the whole text). */
  short: string
  /** Expandable part behind «Подробнее», when the text has one. */
  full: string | null
}

/**
 * The text to show: the lawyer's version when the server has one, the fallback otherwise. A server text with «Короткая строка» /
 * «Полный текст» sections is split between the always-visible line and the expandable part; one without sections is shown
 * whole (showing the lawyer's text in the wrong spot beats dropping it because a heading was reworded — same rule as
 * `findSection` everywhere).
 */
export function resolveSlotText<K extends string>(fallbacks: Record<K, SlotText>, key: K, serverHtml: string | null | undefined): SlotText {
  if (!serverHtml || !serverHtml.trim()) return fallbacks[key]
  const sections = splitLegalSections(serverHtml)
  const short = findSection(sections, 'Короткая строка')?.html
  const full = findSection(sections, 'Полный текст')?.html
  if (short) return { short, full: full ?? null }
  return { short: serverHtml, full: null }
}
