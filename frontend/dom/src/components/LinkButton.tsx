import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'

/** A router link that looks like the primary `Button` — a `<button>` inside an `<a>` is invalid HTML and
 *  confuses keyboard and screen-reader users. 44 px touch target. */
export function LinkButton({
  to,
  children,
  className = '',
  variant = 'primary',
}: {
  to: string
  children: ReactNode
  className?: string
  variant?: 'primary' | 'secondary'
}) {
  const look =
    variant === 'primary'
      ? 'bg-ink !text-cream hover:bg-ink/90'
      : 'bg-white !text-ink border border-line hover:bg-cream-deep'
  return (
    <Link
      to={to}
      className={`inline-flex min-h-[44px] items-center justify-center gap-2 rounded-full px-5 py-2.5 text-sm font-semibold transition-colors ${look} ${className}`}
    >
      {children}
    </Link>
  )
}
