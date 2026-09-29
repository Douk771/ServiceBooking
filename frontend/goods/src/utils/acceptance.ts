import type { PauseDuration, ShopAcceptanceDto } from '../types'

export const PAUSE_OPTIONS: readonly { value: PauseDuration; label: string }[] = [
  { value: 'Minutes15', label: '15 минут' },
  { value: 'Minutes30', label: '30 минут' },
  { value: 'Hour1', label: '1 час' },
  { value: 'EndOfDay', label: 'До конца дня' },
]

/** «осталось 12 мин» for a running pause, from the server's `pausedUntilUtc`; null when not paused or already over. */
export function pauseRemainingText(acceptance: Pick<ShopAcceptanceDto, 'mode' | 'pausedUntilUtc'>, serverNowMs: number): string | null {
  if (acceptance.mode !== 'Paused' || !acceptance.pausedUntilUtc) return null
  const ms = new Date(acceptance.pausedUntilUtc).getTime() - serverNowMs
  if (!Number.isFinite(ms) || ms <= 0) return null
  const minutes = Math.ceil(ms / 60000)
  if (minutes < 60) return `осталось ${minutes} мин`
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  return m === 0 ? `осталось ${h} ч` : `осталось ${h} ч ${m} мин`
}

/** Banner tone for the orders screen: the shop is not accepting orders right now (owner's wording comes from the server). */
export type LimitTone = 'none' | 'warning' | 'reached'
export function limitTone(level: 'None' | 'Warning80' | 'Reached' | string | undefined): LimitTone {
  return level === 'Reached' ? 'reached' : level === 'Warning80' ? 'warning' : 'none'
}
