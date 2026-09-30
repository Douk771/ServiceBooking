import type { ReactNode } from 'react'

/** ARCHITECTURE_CYCLE32.md §32.4.7 — the one form-level error box (shared by ezbook and goods). */
export function InlineError({ children }: { children: ReactNode }) {
  return (
    <div role="alert" className="bg-danger-bg text-danger text-sm px-4 py-2.5 rounded-xl">
      {children}
    </div>
  )
}
