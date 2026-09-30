import type { City } from '../../types'
import { formatUtcOffset } from '../../utils/timezone'

/** ARCHITECTURE_CYCLE32.md §32.6.2 — what to send about city/zone and whether to ask for confirmation. */
export interface ZoneBaseline {
  cityId: number | null
  zoneId: string | null
  offsetMinutes: number | null
  isManual: boolean
}
export interface ZoneDraft {
  city: City | null
  manual: boolean
  manualZoneId: string
}
export interface ZonePlan {
  /** Present iff the city changed. */
  cityId?: number
  /** Present iff the salon changed the manual-zone state (never for shops). `null` = reset to the city zone. */
  timeZoneId?: string | null
  confirm: { from: string; to: string } | null
}

export function planProfileZone(
  base: ZoneBaseline,
  draft: ZoneDraft,
  manualAllowed: boolean,
  offsetOf: (zoneId: string) => number | null,
): ZonePlan {
  const plan: ZonePlan = { confirm: null }
  const cityChanged = draft.city != null && draft.city.id !== base.cityId
  if (cityChanged && draft.city) plan.cityId = draft.city.id

  if (manualAllowed) {
    const z = draft.manual ? draft.manualZoneId.trim() : ''
    if (z !== '' && (!base.isManual || z !== base.zoneId)) plan.timeZoneId = z
    else if (z === '' && base.isManual) plan.timeZoneId = null
  }

  let offset: number | null = null
  if (typeof plan.timeZoneId === 'string') {
    offset = offsetOf(plan.timeZoneId)
  } else if (plan.timeZoneId === null) {
    if (cityChanged && draft.city) offset = draft.city.utcOffsetMinutes
    else {
      // The city's own zone is unknown to the client (§32.3, variant В): always ask, without a number.
      plan.confirm = { from: fromText(base), to: `пояс города ${draft.city?.label ?? ''}`.trim() }
      return plan
    }
  } else if (cityChanged && !base.isManual && draft.city) {
    offset = draft.city.utcOffsetMinutes
  }

  if (offset != null && offset !== base.offsetMinutes) {
    plan.confirm = { from: fromText(base), to: formatUtcOffset(offset) }
  }
  return plan
}

function fromText(base: ZoneBaseline): string {
  return base.offsetMinutes != null ? formatUtcOffset(base.offsetMinutes) : (base.zoneId ?? '—')
}
