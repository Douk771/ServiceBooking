import { useLegalText } from './useLegalText'
import { resolveShowcaseText, type ShowcaseText, type ShowcaseTextKey } from '../utils/showcaseTexts'

/**
 * `GET /api/legal/texts/{key}` for a cycle-28 showcase text with the §600 fallback. Never returns "nothing":
 * while the request is in flight, on 404 (the key is not published yet) and on any error the fallback is
 * shown, so the "this is a fictional salon" disclosure is never missing from the screen. `enabled = false` skips the
 * request altogether (e.g. the booking modal of an ordinary company never needs the closed-showcase text).
 */
export function useShowcaseText(key: ShowcaseTextKey, enabled = true): ShowcaseText {
  const { data } = useLegalText(key, { enabled })
  return resolveShowcaseText(key, data)
}
