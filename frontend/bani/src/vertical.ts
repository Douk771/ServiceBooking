import { createSlotApi } from '@/api/slots'
import type { PushSite } from '@/api/push'

/**
 * `bathsVertical` — the bani description of the slot vertical (ARCHITECTURE_CYCLE42.md §42.12.2).
 * MINIMAL until FE-42-1b ships `SlotVerticalProvider` and its `SlotVertical` type: then this object is typed by it
 * and gets `paths`, legal keys, `words` and `features`. Do not grow it with anything but those fields.
 */
export const bathsVertical = {
  kind: 'Baths',
  /** The one spelling of the brand (header, footer, manifest, <title>, push). */
  brand: 'EZBOOK Бани',
  appName: 'Бани',
  pushSite: 'Baths' as PushSite,
  api: createSlotApi('/baths'),
} as const

export type BathsVertical = typeof bathsVertical
