import { useRef, type KeyboardEvent, type ReactNode } from 'react'
import { nextRadioIndex } from '../../utils/pickup'

export interface RadioChipOption<T extends string> {
  value: T
  label: ReactNode
  /** Accessible name when `label` is not plain text. */
  ariaLabel?: string
  disabled?: boolean
  hint?: string
}

interface Props<T extends string> {
  /** Accessible name of the group (read by screen readers). */
  label: string
  options: readonly RadioChipOption<T>[]
  value: T | null
  onChange: (v: T) => void
  className?: string
}

/**
 * A real WAI-ARIA radiogroup: one tab stop (roving tabindex), arrows/Home/End move AND select, disabled options are skipped.
 * Used for the date and the slot choice (SPEC §6: keyboard and screen reader; API_CONTRACT_CYCLE24.md §490).
 */
export function RadioChips<T extends string>({ label, options, value, onChange, className = '' }: Props<T>) {
  const refs = useRef<(HTMLButtonElement | null)[]>([])
  const enabled = options.map((o, i) => (o.disabled ? -1 : i)).filter((i) => i >= 0)
  const focusable = value !== null && enabled.includes(options.findIndex((o) => o.value === value)) ? options.findIndex((o) => o.value === value) : (enabled[0] ?? -1)

  const onKeyDown = (e: KeyboardEvent<HTMLButtonElement>, index: number) => {
    const pos = enabled.indexOf(index)
    const nextPos = nextRadioIndex(e.key, pos < 0 ? 0 : pos, enabled.length)
    if (nextPos === null) return
    e.preventDefault()
    const target = enabled[nextPos]
    refs.current[target]?.focus()
    onChange(options[target].value)
  }

  return (
    <div role="radiogroup" aria-label={label} className={`flex flex-wrap gap-2 ${className}`}>
      {options.map((o, i) => {
        const checked = o.value === value
        return (
          <button
            key={o.value}
            ref={(el) => {
              refs.current[i] = el
            }}
            type="button"
            role="radio"
            aria-checked={checked}
            aria-label={o.ariaLabel}
            disabled={o.disabled}
            tabIndex={i === focusable ? 0 : -1}
            onClick={() => onChange(o.value)}
            onKeyDown={(e) => onKeyDown(e, i)}
            title={o.hint}
            className={`min-h-[44px] rounded-full border px-4 py-2 text-sm font-semibold transition-colors text-left ${
              checked ? 'bg-ink text-cream border-ink' : 'bg-white text-ink border-line hover:border-line-strong'
            } disabled:opacity-45 disabled:cursor-not-allowed disabled:hover:border-line`}
          >
            {o.label}
          </button>
        )
      })}
    </div>
  )
}
