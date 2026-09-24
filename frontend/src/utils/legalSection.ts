import { findSection, splitLegalSections } from './legalSections'

/**
 * ARCHITECTURE_CYCLE13.md §220.2/§220.5 (R24). `13-public-address-notice.html` is one file with
 * sections meant for very different audiences: the interface text ("Текст для владельца") and two
 * service appendices ("Служебное приложение А/Б") written for the legal/engineering team, never for
 * an end user. `GET /api/legal/texts/PublicAddressNotice` serves the whole file, so the frontend must
 * cut out exactly the owner-facing section itself.
 *
 * Deliberately stricter than the generic `findSection` fallback used elsewhere (e.g.
 * `ClientConsentModal`, which falls back to showing the WHOLE document when its section heading isn't
 * found): for this document that fallback would leak "Служебное приложение" text — the legal
 * qualification and internal commentary — straight into the owner-facing UI. Returning `null` here on
 * a missing anchor is intentional; callers MUST show an error and disable confirmation rather than
 * inventing their own wording for a legal notice (§220.2, R24).
 */
export function ownerFacingSection(html: string): string | null {
  const section = findSection(splitLegalSections(html), 'Текст для владельца')
  return section?.html ?? null
}
