import { Icon } from '../ui/Icon'
import { useDemoStatus } from '../../hooks/useDemoStatus'
import { useShowcaseText } from '../../hooks/useShowcaseText'

function DemoBannerContent({ compact }: { compact: boolean }) {
  const text = useShowcaseText('DemoBanner')
  return (
    <div
      role="note"
      data-testid="demo-banner"
      className={`flex items-start justify-center gap-2 border-b border-[#E3D3A8] bg-warning-bg text-warning leading-[1.45] print:hidden ${
        compact ? 'px-3 py-1.5 text-[12px]' : 'px-4 py-2 text-[13px]'
      }`}
    >
      <Icon name="alert-circle" size={compact ? 13 : 15} strokeWidth={1.8} className="mt-[2px] shrink-0" />
      {text.html ? (
        <div
          className="legal-content min-w-0 [&_p]:m-0 [&_a]:underline"
          dangerouslySetInnerHTML={{ __html: text.html }}
        />
      ) : (
        <p className="min-w-0">{text.text}</p>
      )}
    </div>
  )
}

/**
 * API_CONTRACT_CYCLE28.md §600 [L28-3] — the "this is a demo" strip above every page, the embed widget included.
 * Renders nothing (and does not even ask for the text) unless `GET /api/demo/status` says this is the demo stand.
 */
export function DemoBanner({ compact = false }: { compact?: boolean }) {
  const { isDemo } = useDemoStatus()
  if (!isDemo) return null
  return <DemoBannerContent compact={compact} />
}
