export const MIN_ACTUAL_GRAMS = 1
export const MAX_ACTUAL_GRAMS = 100_000

/** Actual weight typed in grams → integer 1…100 000, else null. A step is NOT required (scales show any weight, §396.4). */
export function parseActualGrams(text: string): number | null {
  const t = text.trim()
  if (!/^\d+$/.test(t)) return null
  const n = Number(t)
  return n >= MIN_ACTUAL_GRAMS && n <= MAX_ACTUAL_GRAMS ? n : null
}
