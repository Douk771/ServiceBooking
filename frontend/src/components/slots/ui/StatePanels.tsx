import type { ReactNode } from 'react'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'

/** Skeleton block for loading states — same pulse the ezbook pages use. */
export function Skeleton({ className = 'h-24' }: { className?: string }) {
  return <div className={`bg-cream-deep rounded-2xl animate-pulse ${className}`} aria-hidden="true" />
}

export function LoadingList({ rows = 3, rowClass = 'h-20' }: { rows?: number; rowClass?: string }) {
  return (
    <div className="flex flex-col gap-3" role="status" aria-label="Загрузка">
      {Array.from({ length: rows }).map((_, i) => (
        <Skeleton key={i} className={rowClass} />
      ))}
    </div>
  )
}

export function EmptyState({ title, text, action }: { title: string; text?: string; action?: ReactNode }) {
  return (
    <div className="rounded-2xl border border-dashed border-line-strong bg-white/60 px-6 py-12 text-center">
      <Icon name="home" size={30} strokeWidth={1.4} className="mx-auto mb-3 text-muted" />
      <p className="text-base font-medium text-ink">{title}</p>
      {text && <p className="mt-1 text-sm text-ink-soft max-w-[420px] mx-auto">{text}</p>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  )
}

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div role="alert" className="rounded-2xl bg-danger-bg text-danger px-5 py-4 flex items-center justify-between gap-4 flex-wrap">
      <span className="text-sm">{message}</span>
      {onRetry && (
        <Button variant="secondary" size="sm" onClick={onRetry}>
          Повторить
        </Button>
      )}
    </div>
  )
}

export { InlineError } from '@/components/ui/InlineError'
