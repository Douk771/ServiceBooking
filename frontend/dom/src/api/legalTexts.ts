import { api } from '@/api/client'
import type { StayTextKey } from '../utils/stayTexts'

/**
 * `GET /api/legal/texts/{key}` for the 13 `Stay*` keys. The keys are kept out of ezbook's `LegalTextKey` union (they live
 * outside the server's `LegalTextKey.All` until the lawyer's text exists, ARCHITECTURE_CYCLE37.md §37.13.4), so dom reads them
 * here instead of widening the shared type. A 404 is a normal state, not an error — see `useStayText`.
 */
export interface StayLegalText {
  version: string
  contentHtml: string
}

export const stayLegalTextsApi = {
  get: (key: StayTextKey) => api.get<StayLegalText>(`/legal/texts/${key}`).then((r) => r.data),
}
