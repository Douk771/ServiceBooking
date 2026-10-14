import { Icon } from '@/components/ui/Icon'

interface Props {
  label: string
  hint?: string
  value: number
  min: number
  max: number
  onChange: (value: number) => void
  disabled?: boolean
}

/** A counter with 44×44 buttons; the value is announced as «label: N». */
export function Stepper({ label, hint, value, min, max, onChange, disabled }: Props) {
  return (
    <div className="flex items-center justify-between gap-3 py-1.5" role="group" aria-label={label}>
      <div className="min-w-0">
        <p className="text-sm font-medium text-ink">{label}</p>
        {hint && <p className="text-xs text-muted">{hint}</p>}
      </div>
      <div className="flex items-center gap-1">
        <button
          type="button"
          aria-label={`${label}: меньше`}
          disabled={disabled || value <= min}
          onClick={() => onChange(value - 1)}
          className="flex h-11 w-11 items-center justify-center rounded-full border border-line bg-white text-ink-soft hover:border-line-strong disabled:opacity-35"
        >
          <Icon name="minus" size={16} strokeWidth={1.8} />
        </button>
        <output aria-live="polite" aria-label={`${label}: ${value}`} className="w-8 text-center text-base font-semibold tabular-nums text-ink">
          {value}
        </output>
        <button
          type="button"
          aria-label={`${label}: больше`}
          disabled={disabled || value >= max}
          onClick={() => onChange(value + 1)}
          className="flex h-11 w-11 items-center justify-center rounded-full border border-line bg-white text-ink-soft hover:border-line-strong disabled:opacity-35"
        >
          <Icon name="plus" size={16} strokeWidth={1.8} />
        </button>
      </div>
    </div>
  )
}
