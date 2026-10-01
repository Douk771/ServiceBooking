import { Icon } from '../ui/Icon'
import { useDemoStatus } from '../../hooks/useDemoStatus'
import { useShowcaseText } from '../../hooks/useShowcaseText'
import { useDemoProduct } from './DemoProductContext'
import { neighbourDemoLink } from '../../utils/demoNeighbour'
import type { DemoStatusDto } from '../../api/demo'

function DemoBannerContent({
  compact,
  siteUrls,
}: {
  compact: boolean
  siteUrls: DemoStatusDto['siteUrls'] | undefined
}) {
  const text = useShowcaseText('DemoBanner')
  const product = useDemoProduct()
  // The compact strip belongs to the embed widget of a salon, which sits on someone else's page: no cross-links there.
  const neighbour = compact ? null : neighbourDemoLink(product, siteUrls)
  return (
    <div
      role="note"
      data-testid="demo-banner"
      className={`flex flex-wrap items-start justify-center gap-x-2 border-b border-[#E3D3A8] bg-warning-bg text-warning leading-[1.45] print:hidden ${
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
      {neighbour && (
        <a
          href={neighbour.href}
          data-testid="demo-neighbour-link"
          className="inline-flex min-h-[44px] items-center gap-1 font-semibold underline underline-offset-2 hover:text-ink sm:min-h-0"
        >
          {neighbour.label}
          <Icon name="arrow-right" size={13} strokeWidth={1.8} />
        </a>
      )}
    </div>
  )
}

/**
 * API_CONTRACT_CYCLE28.md §600 [L28-3] — the "this is a demo" strip above every page, the embed widget included.
 * Renders nothing (and does not even ask for the text) unless `GET /api/demo/status` says this is the demo stand.
 */
export function DemoBanner({ compact = false }: { compact?: boolean }) {
  const { isDemo, status } = useDemoStatus()
  if (!isDemo) return null
  return <DemoBannerContent compact={compact} siteUrls={status?.siteUrls} />
}
