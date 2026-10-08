import { useId, type ReactNode } from 'react'

/** Small form building blocks of the cabinet (44 px targets, labels always real <label>s, errors tied with aria-describedby). */

export function SavedNote({ show }: { show: boolean }) {
  return show ? (
    <span role="status" className="text-sm font-medium text-success">
      Сохранено
    </span>
  ) : null
}

export function SectionCard({ title, description, children, id }: { title: string; description?: ReactNode; children: ReactNode; id?: string }) {
  return (
    <section id={id} aria-labelledby={`${id ?? title}-h`} className="rounded-3xl border border-line bg-white p-5 sm:p-7">
      <h2 id={`${id ?? title}-h`} className="font-serif text-[22px] leading-snug text-ink">
        {title}
      </h2>
      {description && <div className="mt-1 text-sm text-ink-soft">{description}</div>}
      <div className="mt-5 flex flex-col gap-5">{children}</div>
    </section>
  )
}

export function SwitchRow({
  label,
  hint,
  checked,
  onChange,
  disabled,
}: {
  label: string
  hint?: ReactNode
  checked: boolean
  onChange: (v: boolean) => void
  disabled?: boolean
}) {
  const id = useId()
  return (
    <div className="flex items-start justify-between gap-4">
      <div className="min-w-0">
        <label htmlFor={id} className="text-sm font-medium text-ink">
          {label}
        </label>
        {hint && <div className="mt-0.5 text-xs text-muted">{hint}</div>}
      </div>
      <button
        id={id}
        type="button"
        role="switch"
        aria-checked={checked}
        disabled={disabled}
        onClick={() => onChange(!checked)}
        className="flex h-11 w-14 shrink-0 items-center justify-center disabled:opacity-50"
      >
        <span className={`relative block h-7 w-12 rounded-full transition-colors ${checked ? 'bg-ink' : 'bg-line-strong'}`}>
          <span className={`absolute left-1 top-1 h-5 w-5 rounded-full bg-white transition-transform ${checked ? 'translate-x-5' : ''}`} />
        </span>
      </button>
    </div>
  )
}

export function NumberField({
  label,
  hint,
  error,
  value,
  onChange,
  suffix,
  min,
  max,
}: {
  label: string
  hint?: string
  error?: string
  value: number
  onChange: (v: number) => void
  suffix?: string
  min?: number
  max?: number
}) {
  const id = useId()
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-[13px] font-medium text-[#4A4038]">
        {label}
      </label>
      <div className="flex items-center gap-2">
        <input
          id={id}
          type="number"
          inputMode="numeric"
          min={min}
          max={max}
          value={Number.isNaN(value) ? '' : String(value)}
          aria-invalid={!!error}
          aria-describedby={error ? `${id}-e` : hint ? `${id}-h` : undefined}
          onChange={(e) => onChange(e.target.value === '' ? NaN : Number(e.target.value))}
          className={`min-h-[44px] w-full max-w-[160px] rounded-xl border bg-white px-3 text-sm text-ink outline-none focus:ring-[3px] ${
            error ? 'border-danger focus:ring-danger-bg' : 'border-line focus:border-gold focus:ring-cream-deep'
          }`}
        />
        {suffix && <span className="text-sm text-ink-soft">{suffix}</span>}
      </div>
      {error ? (
        <p id={`${id}-e`} className="text-xs text-danger">
          {error}
        </p>
      ) : (
        hint && (
          <p id={`${id}-h`} className="text-xs text-muted">
            {hint}
          </p>
        )
      )}
    </div>
  )
}

export function SelectField({
  label,
  value,
  onChange,
  options,
  error,
  hint,
}: {
  label: string
  value: string
  onChange: (v: string) => void
  options: { value: string; label: string }[]
  error?: string
  hint?: string
}) {
  const id = useId()
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-[13px] font-medium text-[#4A4038]">
        {label}
      </label>
      <select
        id={id}
        value={value}
        aria-invalid={!!error}
        aria-describedby={error ? `${id}-e` : undefined}
        onChange={(e) => onChange(e.target.value)}
        className={`min-h-[44px] w-full rounded-xl border bg-white px-3 text-sm text-ink outline-none focus:ring-[3px] ${
          error ? 'border-danger focus:ring-danger-bg' : 'border-line focus:border-gold focus:ring-cream-deep'
        }`}
      >
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
      {error ? (
        <p id={`${id}-e`} className="text-xs text-danger">
          {error}
        </p>
      ) : (
        hint && <p className="text-xs text-muted">{hint}</p>
      )}
    </div>
  )
}

export function TextArea({
  label,
  value,
  onChange,
  rows = 3,
  maxLength,
  error,
  hint,
}: {
  label: string
  value: string
  onChange: (v: string) => void
  rows?: number
  maxLength?: number
  error?: string
  hint?: ReactNode
}) {
  const id = useId()
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-[13px] font-medium text-[#4A4038]">
        {label}
      </label>
      <textarea
        id={id}
        rows={rows}
        maxLength={maxLength}
        value={value}
        aria-invalid={!!error}
        aria-describedby={error ? `${id}-e` : undefined}
        onChange={(e) => onChange(e.target.value)}
        className={`rounded-xl border bg-white px-4 py-3 text-sm text-ink outline-none focus:ring-[3px] ${
          error ? 'border-danger focus:ring-danger-bg' : 'border-line focus:border-gold focus:ring-cream-deep'
        }`}
      />
      {error && (
        <p id={`${id}-e`} className="text-xs text-danger">
          {error}
        </p>
      )}
      {hint}
    </div>
  )
}
