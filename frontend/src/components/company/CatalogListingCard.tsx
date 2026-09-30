import { Card } from '../ui/Card'
import { Button } from '../ui/Button'

/** Structural view of the server's catalog-listing DTO (shop cycle 25 and salon cycle 31 share the shape). */
export interface CatalogListingView {
  showInCatalog: boolean
  allowedByPlan: boolean
  visible: boolean
  statusText: string
  notAllowedByPlanText?: string | null
  checklist: { code: string; text: string; done: boolean }[]
}

/**
 * ARCHITECTURE_CYCLE31.md §31.10.1 — the ONE visual block "Каталог …" used by goods (shop) and ezbook (salon).
 * Pure markup on props: no requests, no goods imports (it lives in src/components, which both Tailwind builds
 * scan). The switch is controlled by the SERVER value — no optimistic update, so a failed save "rolls back" by
 * construction once the wrapper refetches.
 */
export function CatalogListingCard({
  title,
  switchLabel,
  headingId,
  headingAs: Heading = 'h2',
  data,
  isLoading,
  loadError,
  onRetry,
  saving,
  saveError,
  onToggle,
}: {
  title: string
  switchLabel: string
  headingId: string
  headingAs?: 'h2' | 'h3'
  data?: CatalogListingView
  isLoading: boolean
  loadError?: string | null
  onRetry: () => void
  saving: boolean
  saveError?: string | null
  onToggle: (next: boolean) => void
}) {
  return (
    <Card className="p-5 sm:p-6" aria-labelledby={headingId} role="region">
      <Heading id={headingId} className="font-serif text-xl text-ink">
        {title}
      </Heading>
      {isLoading ? (
        <div className="mt-3 h-14 bg-cream-deep animate-pulse rounded-xl" role="status" aria-label="Загрузка" />
      ) : !data ? (
        <div
          role="alert"
          className="mt-3 rounded-2xl bg-danger-bg text-danger px-5 py-4 flex items-center justify-between gap-4 flex-wrap"
        >
          <span className="text-sm">{loadError}</span>
          <Button variant="secondary" size="sm" onClick={onRetry}>
            Повторить
          </Button>
        </div>
      ) : (
        <>
          <label
            className={`flex items-start justify-between gap-4 py-3 min-h-[44px] ${data.allowedByPlan ? 'cursor-pointer' : 'opacity-60'}`}
          >
            <span className="text-sm font-medium text-ink">{switchLabel}</span>
            <input
              type="checkbox"
              role="switch"
              className="mt-1 h-5 w-9 shrink-0 accent-[#2B2420]"
              checked={data.showInCatalog}
              disabled={!data.allowedByPlan || saving}
              onChange={(e) => onToggle(e.target.checked)}
            />
          </label>
          {!data.allowedByPlan && data.notAllowedByPlanText && (
            <p className="text-sm text-warning bg-warning-bg rounded-xl px-4 py-2.5" data-testid="listing-not-allowed">
              {data.notAllowedByPlanText}
            </p>
          )}
          <p className="mt-2 text-sm text-ink-soft" role="status" data-testid="listing-status">
            {data.statusText}
          </p>
          {data.checklist.length > 0 && (
            <ul className="mt-3 flex flex-col gap-1.5 text-sm" aria-label="Что нужно для показа в каталоге">
              {data.checklist.map((c) => (
                <li
                  key={c.code}
                  className="flex items-start gap-2"
                  data-testid="listing-check"
                  data-done={c.done ? 'true' : 'false'}
                >
                  <span aria-hidden="true" className={c.done ? 'text-success' : 'text-muted'}>
                    {c.done ? '✓' : '○'}
                  </span>
                  <span className={c.done ? 'text-ink-soft' : 'text-ink'}>
                    {c.text}
                    <span className="sr-only">{c.done ? ' — выполнено' : ' — не выполнено'}</span>
                  </span>
                </li>
              ))}
            </ul>
          )}
          {saveError && (
            <div role="alert" className="mt-3 bg-danger-bg text-danger text-sm px-4 py-2.5 rounded-xl">
              {saveError}
            </div>
          )}
        </>
      )}
    </Card>
  )
}
