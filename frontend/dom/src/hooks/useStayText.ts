import { useLegalText } from '@/hooks/slots/useLegalText'
import { stayLegalTextsApi } from '../api/legalTexts'
import { STAY_FALLBACKS, type StayText, type StayTextKey } from '../utils/stayTexts'

/** The legal microcopy for `key` on dom (rules: shared `useLegalText`). */
export function useStayText(key: StayTextKey): StayText & { version: string; isLoading: boolean } {
  return useLegalText(key, STAY_FALLBACKS, stayLegalTextsApi.get)
}
