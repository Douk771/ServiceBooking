import { Icon } from '../ui/Icon'
import { useShowcaseText } from '../../hooks/useShowcaseText'

/**
 * API_CONTRACT_CYCLE28.md §600 [L28-1] — the "this is a fictional example" line: under the company card, in the embed,
 * before the booking confirm button and in a visit card. Text = uiText `ShowcaseNotice`, or the §600 fallback.
 */
export function ShowcaseNotice({ compact = false, className = '' }: { compact?: boolean; className?: string }) {
  const notice = useShowcaseText('ShowcaseNotice')
  return (
    <div
      role="note"
      data-testid="showcase-notice"
      className={`flex items-start gap-2 rounded-xl bg-info-bg text-info leading-[1.5] ${
        compact ? 'px-3 py-2 text-[12px]' : 'px-4 py-3 text-[13px]'
      } ${className}`}
    >
      <Icon name="alert-circle" size={compact ? 13 : 15} strokeWidth={1.8} className="mt-0.5 shrink-0" />
      {notice.html ? (
        <div className="legal-content min-w-0 [&_p]:mb-1.5 [&_p:last-child]:mb-0 [&_a]:underline" dangerouslySetInnerHTML={{ __html: notice.html }} />
      ) : (
        <p className="min-w-0">{notice.text}</p>
      )}
    </div>
  )
}
