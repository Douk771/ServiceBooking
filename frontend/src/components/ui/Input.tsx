import { forwardRef, useId, type InputHTMLAttributes } from 'react'

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string
  error?: string
}

// The label was previously a plain sibling <label> with no `for`, so it wasn't actually associated
// with the input for keyboard/screen-reader users (`getByLabelText` never found it either) — a gap
// that NFR §11.4 (API_CONTRACT_CYCLE5.md/SPEC.md, cycle 5) makes explicit for the registration
// screen. `useId()` generates a stable id when the caller doesn't pass one, so every existing usage
// gets the association for free without any call-site changes.
export const Input = forwardRef<HTMLInputElement, InputProps>(
  ({ label, error, id, className = '', ...props }, ref) => {
    const generatedId = useId()
    const inputId = id ?? generatedId
    return (
      <div className="flex flex-col gap-1.5">
        {label && (
          <label htmlFor={inputId} className="text-[13px] font-medium text-[#4A4038]">
            {label}
          </label>
        )}
        <input
          ref={ref}
          id={inputId}
          className={`rounded-xl border px-4 py-3 text-sm outline-none transition-all bg-white text-ink placeholder:text-muted
          ${error ? 'border-danger focus:ring-2 focus:ring-danger-bg' : 'border-line focus:border-gold focus:ring-[3px] focus:ring-cream-deep'}
          ${className}`}
          {...props}
        />
        {error && <p className="text-xs text-danger">{error}</p>}
      </div>
    )
  },
)
Input.displayName = 'Input'
