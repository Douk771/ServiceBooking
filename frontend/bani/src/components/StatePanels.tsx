import { Button } from '@/components/ui/Button'

/** Skeleton block for loading states — same pulse the ezbook pages use. */
export function Skeleton({ className = 'h-24' }: { className?: string }) {
  return <div className={`bg-cream-deep rounded-2xl animate-pulse ${className}`} aria-hidden="true" />
}

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div role="alert" className="rounded-2xl border border-line bg-white/60 px-6 py-10 text-center">
      <p className="text-sm text-ink-soft">{message}</p>
      {onRetry && (
        <Button variant="secondary" className="mt-4 min-h-[44px]" onClick={onRetry}>
          Повторить
        </Button>
      )}
    </div>
  )
}
