import { useQuery } from '@tanstack/react-query'
import type { PricingOptionDto, PublicPricingDto } from '../../types/pricing'

/**
 * «Линейка» тарифов сервиса (ARCHITECTURE_CYCLE38.md §38.7.1): общий вид сетки для «Записи» и «Заказов».
 * Под ключом линейки в кеше TanStack Query всегда лежит PricingGridView — не «сырой» DTO.
 */
export interface PricingPlanView {
  id: string
  name: string
  description: string | null
  pricePerMonth: number
  highlights: readonly string[]
  sortOrder: number
  isFree: boolean
  isTrial: boolean
  /** Готовые строки лимитов для карточки: «до 3 магазинов», «Заказы без ограничений»… — формирует линейка. */
  limitLines: readonly string[]
}

export interface PricingGridView {
  plans: readonly PricingPlanView[]
  /** У «Заказов» всегда []. */
  options: readonly PricingOptionDto[]
  notice: string
  legalNotice: string | null
  /** Cycle 40 (Т40-L-11): серверные строки мессенджеров линейки «Записи»; у «Заказов» нет. */
  messengerAddons?: PublicPricingDto['messengerAddons']
  messengerAddonsNote?: PublicPricingDto['messengerAddonsNote']
}

export interface PricingLine {
  /** ['public-pricing', 'services'] | ['public-pricing', 'orders'] — одна линейка = один ключ кеша. */
  queryKey: readonly ['public-pricing', string]
  /** null = 404 (сетки нет) — ожидаемое состояние, не ошибка. */
  fetchGrid: () => Promise<PricingGridView | null>
}

export function usePricingGrid(line: PricingLine) {
  return useQuery({ queryKey: line.queryKey, queryFn: line.fetchGrid, retry: false, staleTime: 20_000 })
}
