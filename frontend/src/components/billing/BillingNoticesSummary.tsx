import { Link } from 'react-router-dom'
import { usePlatformNotices } from '../../hooks/usePlatformNotices'
import { Card } from '../ui/Card'
import { Icon } from '../ui/Icon'

/**
 * ARCHITECTURE_CYCLE20.md §404.5 — "блок на странице «Ваша подписка» для держателя". A compact
 * summary (not a duplicate of `/notices`): unread count plus the two most recent, always linking to
 * the full list. Renders nothing while there's nothing to show, rather than an empty "0 уведомлений" card.
 */
export function BillingNoticesSummary() {
  const { data } = usePlatformNotices('all')
  if (!data || data.items.length === 0) return null

  const unreadCount = data.items.filter((n) => !n.acknowledged && !n.revokedAt).length
  const recent = data.items.slice(0, 2)

  return (
    <Card className="p-[26px] mb-[18px]">
      <div className="flex items-center justify-between mb-3">
        <h2 className="text-[15.5px] font-semibold text-ink flex items-center gap-2">
          Уведомления сервиса
          {unreadCount > 0 && (
            <span className="text-[11px] font-semibold px-2 py-0.5 rounded-full bg-info-bg text-info">{unreadCount} новых</span>
          )}
        </h2>
        <Link to="/notices" className="text-xs text-gold-dark underline hover:no-underline">
          Все уведомления
        </Link>
      </div>
      <div className="flex flex-col gap-2">
        {recent.map((n) => (
          <div key={n.id} className="flex items-start gap-2 text-sm">
            <Icon name="alert-circle" size={13} strokeWidth={1.8} className="text-muted shrink-0 mt-1" />
            <p className="text-ink-soft">{n.title}</p>
          </div>
        ))}
      </div>
    </Card>
  )
}
