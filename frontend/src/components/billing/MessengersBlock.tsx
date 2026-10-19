import { Link } from 'react-router-dom'
import type { NotificationTransport, OwnerMessengerLine, OwnerMessengersBlock } from '../../types'
import { Card } from '../ui/Card'

// Cycle 43 (ARCHITECTURE_CYCLE43.md §43.8.3, API_CONTRACT_CYCLE43.md §43.31): the owner's «Сообщения клиентам в мессенджеры» block on «Ваша подписка».
// Statuses, prices, the total, the footnote and the button (whether and what) are the server's; the site only decides WHERE the button leads.

const STATUS_CLASS: Record<OwnerMessengerLine['status'], string> = {
  Paid: 'bg-success-bg text-success',
  Trial: 'bg-info-bg text-info',
  Ending: 'bg-warning-bg text-warning',
  PendingPayment: 'bg-warning-bg text-warning',
  Expired: 'bg-danger-bg text-danger',
  NotConnected: 'bg-cream-deep text-ink-soft',
}

interface Props {
  block: OwnerMessengersBlock | null | undefined
  /** Where the site's notifications section of a company is; the transport is passed as `?connect=` to open its wizard. */
  notificationsHref: (companyId: string, transport: NotificationTransport) => string
}

function Action({ line, notificationsHref }: { line: OwnerMessengerLine; notificationsHref: Props['notificationsHref'] }) {
  if (!line.action || !line.actionText) return null
  if (!line.targetCompanyId) return line.noCompanyText ? <p className="text-xs text-muted">{line.noCompanyText}</p> : null
  return (
    <Link
      to={notificationsHref(line.targetCompanyId, line.transport)}
      aria-label={`${line.actionText} ${line.label}`}
      className="inline-flex items-center justify-center font-semibold rounded-full px-4 py-2 text-xs bg-white hover:bg-cream-deep text-ink border border-line transition-colors min-h-[36px]"
    >
      {line.actionText}
    </Link>
  )
}

export function MessengersBlock({ block, notificationsHref }: Props) {
  if (!block || block.lines.length === 0) return null
  return (
    <Card className="p-[26px] mb-6" data-testid="messengers-block">
      <h2 className="text-[15.5px] font-semibold text-ink">{block.title}</h2>
      <p className="text-xs text-muted mt-0.5 mb-4">{block.note}</p>
      <ul className="divide-y divide-line">
        {block.lines.map((l) => (
          <li key={l.transport} className="py-3 first:pt-0 flex items-start justify-between gap-3 flex-wrap" data-testid={`messenger-${l.transport}`}>
            <div className="min-w-0">
              <div className="flex items-center gap-2 flex-wrap">
                <span className="text-sm font-medium text-ink">{l.label}</span>
                <span className={`text-xs font-medium px-2 py-0.5 rounded-full ${STATUS_CLASS[l.status]}`}>{l.statusText}</span>
              </div>
              {l.priceText && <p className="text-xs text-ink-soft mt-1">{l.priceText}</p>}
              {l.footnote && <p className="text-xs text-muted mt-1 leading-[1.5] max-w-md">{l.footnote}</p>}
            </div>
            <Action line={l} notificationsHref={notificationsHref} />
          </li>
        ))}
      </ul>
      <div className="mt-3 pt-3 border-t border-line flex items-center justify-between gap-3 flex-wrap">
        <p className="text-sm font-semibold text-ink">{block.monthlyTotalText}</p>
        <Link to={block.conditionsUrl} className="text-xs text-ink-soft underline">{block.conditionsLabel}</Link>
      </div>
    </Card>
  )
}
