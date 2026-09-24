import type { PlanConfig } from '../../api/plans'

/**
 * ARCHITECTURE_CYCLE15.md §255.2 — the pair of flags gives exactly three states (0-bis П3), and the
 * label is a PURE FUNCTION of them, computed here and nowhere else. A second copy of this table
 * (e.g. inline JSX) is a regression.
 */
export type PlanStateTone = 'public' | 'hidden' | 'archived'

export interface PlanStateInfo {
  label: string
  tone: PlanStateTone
}

/**
 * | isActive | isPublic | label            | meaning |
 * |----------|----------|------------------|---------|
 * | true     | true     | «На витрине»     | sold on /pricing, assignable |
 * | true     | false    | «Скрыт с витрины»| legacy: gone from /pricing, existing subscribers keep
 * |          |          |                  | renewing, only a superadmin can assign it by hand |
 * | false    | —        | «Архивный»       | assignable to nobody; /pricing never shows it either way |
 */
export function planStateLabel(plan: Pick<PlanConfig, 'isActive' | 'isPublic'>): PlanStateInfo {
  if (!plan.isActive) return { label: 'Архивный', tone: 'archived' }
  if (!plan.isPublic) return { label: 'Скрыт с витрины', tone: 'hidden' }
  return { label: 'На витрине', tone: 'public' }
}
