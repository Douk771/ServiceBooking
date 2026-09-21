/**
 * Formatting helpers for the public pricing page (ARCHITECTURE_CYCLE5.md §56.2): money always as
 * `toLocaleString('ru-RU')` + ' ₽/мес', included limits as "до N" or "без ограничений" for `null`
 * (API_CONTRACT_CYCLE5.md §39 — `null` = без ограничения).
 */

export function formatMonthlyPrice(pricePerMonth: number): string {
  if (pricePerMonth === 0) return 'Бесплатно'
  return `${pricePerMonth.toLocaleString('ru-RU')} ₽/мес`
}

/**
 * `до N X` always puts X in the genitive case (the preposition "до" governs genitive, unlike bare
 * counting which is nominative) — so this takes exactly two genitive forms, not the usual
 * one/few/many triple: genitive singular for counts ending in 1 (except *11), genitive plural
 * otherwise. E.g. formatIncludedLimit(1, 'компании', 'компаний') → "до 1 компании",
 * formatIncludedLimit(21, 'компании', 'компаний') → "до 21 компании" (21 also ends in 1).
 */
export function formatIncludedLimit(count: number | null, unitGenitiveSingular: string, unitGenitivePlural: string): string {
  if (count === null) return 'без ограничений'
  const mod10 = count % 10
  const mod100 = count % 100
  const form = mod10 === 1 && mod100 !== 11 ? unitGenitiveSingular : unitGenitivePlural
  return `до ${count} ${form}`
}

/** Standard Russian plural-form picker (1 компания / 2 компании / 5 компаний). */
export function pluralizeRu(count: number, one: string, few: string, many: string): string {
  const mod10 = count % 10
  const mod100 = count % 100
  if (mod10 === 1 && mod100 !== 11) return one
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20)) return few
  return many
}
