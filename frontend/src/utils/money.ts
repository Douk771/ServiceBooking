/**
 * Cycle 22 (ARCHITECTURE_CYCLE22.md §377) — the one home for "N ₽". `toLocaleString('ru-RU')` groups
 * thousands with a non-breaking space, which is what every screen already showed; the helpers only
 * stop each page from re-typing the template. Per-month plan prices go through
 * `formatMonthlyPrice` (utils/pricingFormat.ts) instead.
 */

/** Amount as the API sent it, kopecks included when present: 1500 → "1 500 ₽", 99.5 → "99,5 ₽". */
export function formatRub(value: number): string {
  return `${value.toLocaleString('ru-RU')} ₽`
}

/** Whole rubles — the admin billing screens (formerly `formatRub` in
 *  pages/admin/billingAccountsHelpers.ts): 999.6 → "1 000 ₽". */
export function formatRubRounded(value: number): string {
  return formatRub(Math.round(value))
}
