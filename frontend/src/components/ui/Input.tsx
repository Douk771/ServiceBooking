import { forwardRef, type InputHTMLAttributes } from 'react'

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string
  error?: string
}

export const Input = forwardRef<HTMLInputElement, InputProps>(({ label, error, className = '', ...props }, ref) => (
  <div className="flex flex-col gap-1.5">
    {label && <label className="text-[13px] font-medium text-[#4A4038]">{label}</label>}
    <input
      ref={ref}
      className={`rounded-xl border px-4 py-3 text-sm outline-none transition-all bg-white text-ink placeholder:text-muted
          ${error ? 'border-danger focus:ring-2 focus:ring-danger-bg' : 'border-line focus:border-gold focus:ring-[3px] focus:ring-cream-deep'}
          ${className}`}
      {...props}
    />
    {error && <p className="text-xs text-danger">{error}</p>}
  </div>
))
Input.displayName = 'Input'
