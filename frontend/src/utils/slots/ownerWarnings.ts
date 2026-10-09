import type { OwnerTextWarning } from '@/types/slots'

/**
 * The soft warnings about an owner's text (`OwnerTextChecks`, ARCHITECTURE_CYCLE42.md §42.8.2, Т42-12). They never block saving: the
 * text is saved and the owner is told what to look at. Unknown codes (an append-only enum) are ignored, not shown as a raw code.
 */
export const OWNER_WARNING_TEXT: Record<OwnerTextWarning, string> = {
  CancellationTermsInText: 'В тексте есть условия отмены. Условия отмены задаются на вкладке «Правила» — в описании они не действуют.',
  MandatoryExtraCharge: 'В тексте упомянуты обязательные доплаты. Всё, что гость обязан оплатить, должно входить в цену, которую он видит.',
  HealthClaim: 'В тексте есть обещание пользы для здоровья. Уберите его: платформа не проверяет такие утверждения.',
  Passport: 'В тексте упомянут паспорт. Не просите у гостей паспортные данные через описание.',
  PassportNumber: 'В тексте похоже на номер паспорта. Уберите его: это персональные данные.',
  CardNumber: 'В тексте похоже на номер банковской карты. Уберите его: реквизиты для оплаты вносятся в настройках компании.',
}

/** The texts to show for the codes the server sent, in the server's order, without repeats. */
export function ownerWarningTexts(codes: readonly string[] | null | undefined): string[] {
  const out: string[] = []
  for (const code of codes ?? []) {
    const text = (OWNER_WARNING_TEXT as Record<string, string | undefined>)[code]
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
