import { findSection, splitLegalSections } from './legalSections'
import type { LegalText, LegalTextKey } from '../types'

/**
 * API_CONTRACT_CYCLE28.md §600 [L28-1] — the showcase/demo uiTexts live OUTSIDE the server's `LegalTextKey.All`,
 * so `GET /api/legal/texts/{key}` answers 404 until the text is published in the live legal.json. Until then
 * the screen shows the fallback below, VERBATIM from §600 (do not reword it: it is the customer-approved
 * placeholder, and the "delete within a day" clause mirrors `Retention:ShowcaseVisitorBookingHours = 24`).
 */
export type ShowcaseTextKey = Extract<LegalTextKey, 'ShowcaseNotice' | 'ShowcaseBookingClosed' | 'DemoBanner'>

export const SHOWCASE_FALLBACK_TEXTS: Record<ShowcaseTextKey, string> = {
  ShowcaseNotice:
    'Это пример страницы салона: компания вымышленная и показана, чтобы продемонстрировать возможности сервиса. ' +
    'Запись здесь не означает настоящего визита, салон не свяжется с вами. ' +
    'Данные такой записи удаляются автоматически в течение суток.',
  ShowcaseBookingClosed: 'Это пример страницы салона: компания вымышленная, запись к ней не принимается.',
  // [L28-3] — the demo stand banner (same "live text or verbatim §600 fallback" rule; shown only in demo mode).
  DemoBanner: 'Демо-версия. Данные удаляются каждую ночь. Не вводите настоящие имена и телефоны.',
}

/** The visible label of the badge — a frontend constant, not a legal text (§600). */
export const SHOWCASE_BADGE_LABEL = 'Пример'

export interface ShowcaseText {
  /** Plain text — for `sr-only`, for the refusal line and as the value the tests compare against. */
  text: string
  /** Sanitised-by-the-server HTML fragment when the live text exists, otherwise `null` (render `text`). */
  html: string | null
  /** `true` = the text came from `GET /api/legal/texts/{key}`, `false` = the §600 fallback. */
  isLive: boolean
}

/** Decodes HTML entities (`&laquo;`, `&mdash;`, `&amp;`, numeric ones) without executing or attaching anything:
 *  `DOMParser` builds an inert document, and only the text of an already tag-free string is read from it. */
function decodeEntities(text: string): string {
  if (!text.includes('&')) return text
  return new DOMParser().parseFromString(text, 'text/html').body.textContent ?? text
}

function stripTags(html: string): string {
  return decodeEntities(html.replace(/<[^>]+>/g, ' '))
    .replace(/\u00a0/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
}

/**
 * Live text wins; a missing key (404), an error, or a live text that turns out to be empty falls back to §600.
 * Same "Текст" section convention as the other uiTexts (`GuestDataGateNotice`): the manifest file may carry a
 * service note before the section, which must not leak into the UI; no such heading → the whole fragment.
 */
export function resolveShowcaseText(key: ShowcaseTextKey, live: LegalText | null | undefined): ShowcaseText {
  if (live?.contentHtml) {
    const section = findSection(splitLegalSections(live.contentHtml), 'Текст')
    const html = (section?.html ?? live.contentHtml).trim()
    const text = stripTags(html)
    if (text) return { text, html, isLive: true }
  }
  return { text: SHOWCASE_FALLBACK_TEXTS[key], html: null, isLive: false }
}
