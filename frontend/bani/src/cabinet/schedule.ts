import type { BathScheduleSessionDto } from './types'

/** Two weeks, as in the schedule of «Дома» (US-42-19). */
export const SCHEDULE_DAYS = 14

/** «1 гость», «2 гостя», «5 гостей», «21 гость». */
export function guestsText(n: number): string {
  const mod100 = n % 100
  const mod10 = n % 10
  const word = mod100 >= 11 && mod100 <= 14 ? 'гостей' : mod10 === 1 ? 'гость' : mod10 >= 2 && mod10 <= 4 ? 'гостя' : 'гостей'
  return `${n} ${word}`
}

/** «Веник × 2, Чай × 1»; empty when nothing was added. */
export function itemsText(items: BathScheduleSessionDto['items']): string {
  return items.map((i) => `${i.name} × ${i.quantity}`).join(', ')
}

/** The guest line of a card: «Мария · 4 гостя»; the name is optional in the closed form, the count too. */
export function guestLine(s: Pick<BathScheduleSessionDto, 'guestName' | 'guestsCount'>): string {
  const parts = [s.guestName?.trim() || 'Гость']
  if (s.guestsCount != null && s.guestsCount > 0) parts.push(guestsText(s.guestsCount))
  return parts.join(' · ')
}
