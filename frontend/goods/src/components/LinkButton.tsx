import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'

/** A router link that looks like the primary `Button` — a `<button>` inside an `<a>` is invalid HTML and
 *  confuses keyboard and screen-reader users. */
export function LinkButton({ to, children, className = '' }: { to: string; children: ReactNode; className?: string }) {
  return (
    <Link
      to={to}
      className={`inline-flex items-center justify-center gap-2 rounded-full bg-ink px-5 py-2.5 text-sm font-semibold !text-cream transition-colors hover:bg-ink/90 ${className}`}
    >
      {children}
    </Link>
  )
}
