import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import { findSection, splitLegalSections } from '../utils/legalSections'
import { applyLegalRuntimeValues } from '../utils/legalRuntimeValues'

export interface MessengerLegalText {
  shortHtml: string
  fullHtml: string | null
  /** Версия текста юриста; `null` — показан запасной текст. */
  version: string | null
}

/**
 * Правовой текст галочки/подсказки по ключу (`BookingMessengerConsent`, `OrderMessengerConsent`, `StayMessengerConsent`,
 * `StaffBookingMessengerConsentHint`). Ключи вне `LegalTextKey.All`: 404 — нормальное состояние (текста юриста ещё нет),
 * тогда дословный запасной текст (LEGAL_REVIEW_CYCLE40.md §5.3, §6.2, §7.3). Название компании подставляется при каждом показе.
 */
export function useMessengerLegalText(
  key: string,
  fallback: { shortHtml: string; fullHtml: string | null },
  companyName?: string | null,
  enabled = true,
): MessengerLegalText {
  const { data } = useQuery({
    queryKey: ['legal-text', key],
    queryFn: () =>
      api.get<{ version: string; contentHtml: string }>(`/legal/texts/${key}`).then((r) => r.data),
    staleTime: 5 * 60 * 1000,
    retry: false,
    enabled,
  })
  const render = (html: string) => applyLegalRuntimeValues(html, { companyName: companyName ?? null })

  if (data?.contentHtml) {
    const sections = splitLegalSections(data.contentHtml)
    const short = findSection(sections, 'Короткая строка')
    const full = findSection(sections, 'Полный текст')
    // Нет ожидаемых заголовков — показываем документ целиком в коротком месте: лучше текст юриста не там, чем потерянный.
    return {
      shortHtml: render(short?.html ?? (full ? '' : data.contentHtml)),
      fullHtml: full ? render(full.html) : null,
      version: data.version,
    }
  }
  return { shortHtml: render(fallback.shortHtml), fullHtml: fallback.fullHtml ? render(fallback.fullHtml) : null, version: null }
}
