import { useQuery } from '@tanstack/react-query'
import { stayLegalTextsApi } from '../api/legalTexts'
import { resolveStayText, type StayText, type StayTextKey } from '../utils/stayTexts'

/**
 * The legal microcopy for `key`: the server's text when it exists, the fallback when the server says 404, is unreachable or still
 * loading (a screen never waits for, or breaks on, a missing legal text). `version` is the server's version, or `fallback`.
 */
export function useStayText(key: StayTextKey): StayText & { version: string; isLoading: boolean } {
  const q = useQuery({
    queryKey: ['legal-text', key],
    queryFn: () => stayLegalTextsApi.get(key),
    staleTime: 5 * 60 * 1000,
    retry: false, // 404 = «not written yet» — do not hammer it
  })
  const text = resolveStayText(key, q.data?.contentHtml)
  return { ...text, version: q.data?.version ?? 'fallback', isLoading: q.isLoading }
}
