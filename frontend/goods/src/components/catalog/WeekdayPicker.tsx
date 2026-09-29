import { WEEKDAYS, toggleWeekday } from '../../utils/weekdays'
import type { DayOfWeek } from '../../types'

interface Props {
  legend: string
  value: readonly DayOfWeek[]
  onChange: (next: DayOfWeek[]) => void
  hint?: string
}

/** Seven checkboxes in a labelled group (US-24-10). Each is a real checkbox with a full-day accessible name; empty is valid («только по меню»). */
export function WeekdayPicker({ legend, value, onChange, hint }: Props) {
  return (
    <fieldset>
      <legend className="text-[13px] font-medium text-[#4A4038] mb-2">{legend}</legend>
      <div className="flex flex-wrap gap-1.5">
        {WEEKDAYS.map((d) => {
          const on = value.includes(d.value)
          return (
            <label
              key={d.value}
              className={`min-w-[44px] min-h-[44px] rounded-full border px-3 flex items-center justify-center text-sm font-semibold cursor-pointer transition-colors focus-within:ring-2 focus-within:ring-gold ${
                on ? 'bg-ink text-cream border-ink' : 'bg-white text-ink-soft border-line hover:border-line-strong'
              }`}
            >
              <input type="checkbox" className="sr-only" checked={on} aria-label={d.full} onChange={() => onChange(toggleWeekday(value, d.value))} />
              <span aria-hidden="true">{d.short}</span>
            </label>
          )
        })}
      </div>
      {hint && <p className="text-xs text-muted mt-1.5">{hint}</p>}
    </fieldset>
  )
}
