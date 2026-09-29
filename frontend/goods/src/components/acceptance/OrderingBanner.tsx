import { Icon } from '@/components/ui/Icon'
import { limitTone } from '../../utils/acceptance'
import type { ShopOrderingStatusDto } from '../../types'

/**
 * The orders-screen strip from `ordering-status` (API_CONTRACT_CYCLE24.md §476): why the shop does NOT accept orders right
 * now (owner's wording), the open/closed state in words, and the monthly-limit warning at 80 % / 100 %. All texts are the
 * server's; nothing is derived here.
 */
export function OrderingBanner({ status }: { status: ShopOrderingStatusDto }) {
  const tone = limitTone(status.orderLimit.warningLevel)
  return (
    <div className="flex flex-col gap-2 mb-4" data-testid="ordering-banner">
      {!status.acceptingOrders && status.ownerText && (
        <div role="alert" className="rounded-xl bg-warning-bg text-warning text-sm font-medium px-4 py-3 flex items-start gap-2">
          <Icon name="alert-circle" size={16} strokeWidth={1.8} className="mt-0.5 shrink-0" />
          <span>{status.ownerText}</span>
        </div>
      )}
      {tone !== 'none' && status.orderLimit.text && (
        <div role="status" className={`rounded-xl text-sm font-medium px-4 py-3 ${tone === 'reached' ? 'bg-danger-bg text-danger' : 'bg-warning-bg text-warning'}`} data-testid="limit-banner" data-level={status.orderLimit.warningLevel}>
          {status.orderLimit.text}
        </div>
      )}
      <p className="text-xs text-ink-soft flex items-center gap-1.5" data-testid="open-state">
        <Icon name="clock" size={13} strokeWidth={1.8} />
        {status.openState.text}
      </p>
    </div>
  )
}
