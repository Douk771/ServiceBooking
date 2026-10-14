import type { OwnerTextWarning } from '@/types/slots'

/**
 * The soft warnings about an owner's text (`OwnerTextChecks`, ARCHITECTURE_CYCLE42.md §42.8.2, Т42-12). They never block saving: the
 * text is saved and the owner is told what to look at. Unknown codes (an append-only enum) are ignored, not shown as a raw code.
 */
export const OWNER_WARNING_TEXT: Record<OwnerTextWarning, string> = {
  CancellationTermsInText: 'Условия отмены задаёт выбранный шаблон — другие условия в описании не действуют',
  MandatoryExtraCharge: 'Все обязательные платежи должны быть в цене часов — не требуйте доплат на месте',
  HealthClaim: 'Не обещайте лечебного или оздоровительного эффекта',
  Passport: 'Не просите гостя прислать фото паспорта',
  PassportNumber: 'Похоже на паспортные данные — не публикуйте их',
  CardNumber: 'Похоже на номер карты — не публикуйте данные карт',
}

/** The texts to show for the codes the server sent, in the server's order, without repeats. */
export function ownerWarningTexts(
  codes: readonly string[] | null | undefined,
  /** A vertical's own texts (bani prints `bani-vectors.json` `ownerText.warningTexts` verbatim); the defaults are dom's. */
  overrides?: Partial<Record<string, string>>,
): string[] {
  const out: string[] = []
  for (const code of codes ?? []) {
    const text = overrides?.[code] ?? (OWNER_WARNING_TEXT as Record<string, string | undefined>)[code]
    if (text && !out.includes(text)) out.push(text)
  }
  return out
}

/** The answer 409 `ItemRestrictedConfirmationRequired` of a position that looks like alcohol or tobacco (Т42-05), ready for a dialog. */
export interface RestrictedItemPrompt {
  markers: string[]
  /** The text of the notice, without markup; empty when the server sent none. */
  text: string
}

const stripTags = (html: string) => html.replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ').trim()

/** null when the conflict is something else — the screen then shows the conflict's own message. */
export function restrictedItemPrompt(conflict: { code?: string; markers?: string[] | null; noticeText?: string | null } | null | undefined): RestrictedItemPrompt | null {
  if (!conflict || conflict.code !== 'ItemRestrictedConfirmationRequired') return null
  return { markers: (conflict.markers ?? []).filter((m) => m.trim() !== ''), text: stripTags(conflict.noticeText ?? '') }
}
