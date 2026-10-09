// RestrictedItemFilter.Match (bani-vectors.json `restrictedItems`): soft filter, the owner confirms before saving.
export const RESTRICTED_STEMS: readonly string[] = [
  'пив', 'вин', 'водк', 'коньяк', 'виски', 'сидр', 'медовух', 'шампанск', 'алкогол', 'кальян', 'табак', 'сигар', 'вейп', 'снюс',
]

const normalize = (s: string) => s.toLowerCase().replace(/ё/g, 'е')
const isLetter = (ch: string) => /\p{L}/u.test(ch)

/** Stems found at the START of a word (not preceded by a letter), in the order of RESTRICTED_STEMS, without repeats. */
export function matchRestrictedItems(text: string): string[] {
  const t = normalize(text)
  return RESTRICTED_STEMS.filter((stem) => {
    const s = normalize(stem)
    for (let i = t.indexOf(s); i >= 0; i = t.indexOf(s, i + 1)) {
      if (i === 0 || !isLetter(t[i - 1])) return true
    }
    return false
  })
}
