import { RadioChips } from './RadioChips'
import { ErrorState, Skeleton } from '../StatePanels'
import type { PickupSlotsDto } from '../../types'

interface Props {
  /** Result of `pickup-slots` for the date; `undefined` while loading. */
  data: PickupSlotsDto | undefined
  isLoading: boolean
  error: string | null
  onRetry: () => void
  selectedStartUtc: string | null
  onSelect: (slot: { startUtc: string; label: string }) => void
  groupLabel: string
}

/** Slots of one date as a radiogroup with loading, empty (server's `reasonText`) and error states. The frontend never derives a slot. */
export function SlotList({ data, isLoading, error, onRetry, selectedStartUtc, onSelect, groupLabel }: Props) {
  if (isLoading) return <Skeleton className="h-11 w-full" />
  if (error) return <ErrorState message={error} onRetry={onRetry} />
  if (!data || data.slots.length === 0)
    return (
      <p className="text-sm text-ink-soft" data-testid="no-slots">
        {data?.reasonText ?? 'На эту дату свободного времени нет — выберите другой день.'}
      </p>
    )
  const byStart = new Map(data.slots.map((s) => [s.startUtc, s]))
  return (
    <RadioChips
      label={groupLabel}
      value={selectedStartUtc && byStart.has(selectedStartUtc) ? selectedStartUtc : null}
      onChange={(v) => {
        const s = byStart.get(v)
        if (s) onSelect({ startUtc: s.startUtc, label: s.label })
      }}
      options={data.slots.map((s) => ({ value: s.startUtc, label: s.label }))}
    />
  )
}
