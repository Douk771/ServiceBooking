import { useShowcaseText } from '../../hooks/useShowcaseText'
import { SHOWCASE_BADGE_LABEL } from '../../utils/showcaseTexts'

/**
 * API_CONTRACT_CYCLE28.md §591 / ARCHITECTURE_CYCLE28.md §577.2 — the "Пример" mark of a showcase company: catalog
 * card and the `CompanyCard` slot only. The meaning is carried by the TEXT (colour only decorates it), and a screen
 * reader additionally gets the full `ShowcaseNotice` in a visually hidden span.
 */
export function ShowcaseBadge({ className = '' }: { className?: string }) {
  const notice = useShowcaseText('ShowcaseNotice')
  return (
    <span
      className={`inline-flex items-center rounded-full border border-line-strong bg-cream-deep px-2.5 py-[3px] text-xs font-semibold tracking-wide text-ink-soft ${className}`}
    >
      {SHOWCASE_BADGE_LABEL}
      <span className="sr-only">. {notice.text}</span>
    </span>
  )
}
