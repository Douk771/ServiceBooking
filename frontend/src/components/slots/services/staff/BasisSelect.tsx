import type { StayServiceRequestBasis } from '@/types/slots'
import { BASIS_OPTIONS } from '@/utils/slots/serviceForms'

/** «Как гость попросил услугу» — required for a session added by the staff (ЮР39-6, 69-ФЗ): without it the server answers 400. */
export function BasisSelect({ value, onChange }: { value: StayServiceRequestBasis | ''; onChange: (v: StayServiceRequestBasis) => void }) {
  return (
    <fieldset>
      <legend className="mb-2 text-[15px] font-semibold text-ink">Гость попросил услугу *</legend>
      <ul className="flex flex-wrap gap-2">
        {BASIS_OPTIONS.map((o) => (
          <li key={o.value}>
            <label className="flex min-h-[44px] cursor-pointer items-center gap-2 rounded-2xl border border-line bg-white px-4 text-sm text-ink has-[:checked]:border-ink has-[:checked]:bg-cream-deep">
              <input type="radio" name="request-basis" checked={value === o.value} onChange={() => onChange(o.value)} className="h-5 w-5 accent-gold" />
              {o.label}
            </label>
          </li>
        ))}
      </ul>
      {value === '' && <p className="mt-1 text-xs text-muted">Укажите, как гость попросил услугу</p>}
    </fieldset>
  )
}
