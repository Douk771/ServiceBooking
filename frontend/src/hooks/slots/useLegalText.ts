import { useQuery } from '@tanstack/react-query'
import { api } from '@/api/client'
import { resolveSlotText, type SlotText } from '@/utils/slots/slotTexts'

export interface ServerLegalText {
  version: string
  contentHtml: string
}

/**
 * The legal microcopy for `key` of a booking vertical: the server's text (`GET /api/legal/texts/{key}`) when it exists, the
 * fallback when the server says 404, is unreachable or still loading (a screen never waits for, or breaks on, a missing legal
 * text). `version` is the server's version, or `fallback`. Same query key and caching as the dom hook it was moved from.
 */
const fetchLegalText = (key: string) => api.get<ServerLegalText>(`/legal/texts/${key}`).then((r) => r.data)

export function useLegalText<K extends string>(
  key: K,
  fallbacks: Record<K, SlotText>,
  /** The reader of the server text; a vertical may pass its own (dom keeps `stayLegalTextsApi.get`). */
  fetchText: (key: K) => Promise<ServerLegalText> = fetchLegalText,
): SlotText & { version: string; isLoading: boolean } {
  const q = useQuery({
    queryKey: ['legal-text', key],
    queryFn: () => fetchText(key),
    staleTime: 5 * 60 * 1000,
    retry: false, // 404 = «not written yet» — do not hammer it
  })
  const text = resolveSlotText(fallbacks, key, q.data?.contentHtml)
  return { ...text, version: q.data?.version ?? 'fallback', isLoading: q.isLoading }
}
