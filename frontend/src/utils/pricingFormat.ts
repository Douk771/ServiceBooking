/**
 * Formatting helpers for the public pricing page (ARCHITECTURE_CYCLE5.md §56.2): money always as
 * `toLocaleString('ru-RU')` + ' ₽/мес', included limits as "до N" or "без ограничений" for `null`
 * (API_CONTRACT_CYCLE5.md §39 — `null` = без ограничения).
 */

export function formatMonthlyPrice(pricePerMonth: number): string {
  if (pricePerMonth === 0) return 'Бесплатно'
  return `${pricePerMonth.toLocaleString('ru-RU')} ₽/мес`
}

export function formatIncludedLimit(count: number | null, unitOne: string, unitFew: string, unitMany: string): string {
  if (count === null) return 'без ограничений'
  return `до ${count} ${pluralizeRu(count, unitOne, unitFew, unitMany)}`
}

/** Standard Russian plural-form picker (1 компания / 2 компании / 5 компаний). */
export function pluralizeRu(count: number, one: string, few: string, many: string): string {
  const mod10 = count % 10
  const mod100 = count % 100
  if (mod10 === 1 && mod100 !== 11) return one
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20)) return few
  return many
}
