import { SHOWCASE_FILTERS, type ShowcaseFilter } from '../../utils/showcaseFilter'

/** «Все / Без витрины / Только витрина» — the same segmented control as the company-kind filter, shared by the three admin tabs. */
export function ShowcaseFilterControl({
  value,
  onChange,
}: {
  value: ShowcaseFilter
  onChange: (value: ShowcaseFilter) => void
}) {
  return (
    <div className="flex gap-1 bg-cream-deep p-1 rounded-full w-fit" role="group" aria-label="Витринные данные">
      {SHOWCASE_FILTERS.map((f) => (
        <button
          key={f.value}
          type="button"
          aria-pressed={value === f.value}
          onClick={() => onChange(f.value)}
          className={`px-4 py-1.5 rounded-full text-sm font-medium transition-colors ${value === f.value ? 'bg-white text-ink shadow-soft' : 'text-ink-soft'}`}
        >
          {f.label}
        </button>
      ))}
    </div>
  )
}
