/**
 * Formatting helpers for the public pricing page (ARCHITECTURE_CYCLE7.md §56.2): money always as
 * `toLocaleString('ru-RU')` + ' ₽/мес', included limits as "до N" or "без ограничений" for `null`
 * (API_CONTRACT_CYCLE7.md §39 — `null` = без ограничения).
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

/**
 * "N день/дня/дней" — the full one/few/many triple (unlike `formatIncludedLimit`'s two-form
 * genitive, this is a bare count in nominative context, so 21 is "21 день" not "21 дня").
 * Used by the cycle-18 trial UI (API_CONTRACT_CYCLE18.md §371 п.7) for `daysLeft` — the only place
 * on the trial screens that isn't already a server-composed sentence.
 */
export function formatDaysLeft(days: number): string {
  const mod10 = days % 10
  const mod100 = days % 100
  let word: string
  if (mod10 === 1 && mod100 !== 11) word = 'день'
  else if (mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20)) word = 'дня'
  else word = 'дней'
  return `${days} ${word}`
}

/**
 * Cycle 28 (API_CONTRACT_CYCLE28.md §601): «Сеть» comes with `includedCompanies`/`includedEmployees` = `null`. Two bare
 * «без ограничений» bullets in a row don't say WHAT is unlimited, so a `null` limit names its noun ("Компании без
 * ограничений"); a number reads exactly as {@link formatIncludedLimit} does.
 */
export function formatIncludedLimitLine(
  count: number | null,
  unitNominativePlural: string,
  unitGenitiveSingular: string,
  unitGenitivePlural: string,
): string {
  if (count === null) return `${unitNominativePlural[0].toUpperCase()}${unitNominativePlural.slice(1)} без ограничений`
  return formatIncludedLimit(count, unitGenitiveSingular, unitGenitivePlural)
}

/**
 * The plan that gets the visual accent on `/pricing`: the first PAID plan by `sortOrder` — neither the free one nor the
 * trial. Since cycle 28 the trial (`isTrial`, price 0, `isFree: false`) sits right after the free plan; without this
 * exclusion the accent would land on the trial instead of the first tariff a customer actually pays for.
 */
export function pickFeaturedPlanId(
  plans: readonly { id: string; isFree: boolean; isTrial?: boolean; sortOrder: number; pricePerMonth: number }[],
): string | undefined {
  return [...plans]
    .sort((a, b) => a.sortOrder - b.sortOrder || a.pricePerMonth - b.pricePerMonth)
    .find((p) => !p.isFree && !p.isTrial)?.id
}
