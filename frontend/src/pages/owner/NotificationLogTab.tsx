import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { notificationsApi } from '../../api/notifications'
import { Card } from '../../components/ui/Card'
import { Icon } from '../../components/ui/Icon'
import { Pagination } from '../../components/ui/Pagination'
import type { NotificationStatus } from '../../types'

const STATUS_LABEL: Record<NotificationStatus, string> = {
  Pending: 'в очереди',
  Sent: 'отправлено',
  Delivered: 'доставлено',
  Failed: 'не доставлено',
  Expired: 'протухло',
  Skipped: 'пропущено',
  Cancelled: 'отменено',
}

const STATUS_FILTERS: { value: string; label: string }[] = [
  { value: '', label: 'Все статусы' },
  { value: 'Delivered', label: 'Доставлено' },
  { value: 'Sent', label: 'Отправлено' },
  { value: 'Failed', label: 'Не доставлено' },
  // §30.1: Expired is a separate filter from Failed on purpose — "we didn't make it" vs "WhatsApp
  // didn't deliver" must stay distinguishable in the log (US-32 п. 2).
  { value: 'Expired', label: 'Протухло (не успели)' },
  { value: 'Skipped', label: 'Пропущено' },
  { value: 'Cancelled', label: 'Отменено' },
]

/** US-32 — delivery log, the only source of hard data on whether the channel actually works (P1). */
export function NotificationLogTab({ companyId }: { companyId: string }) {
  const [page, setPage] = useState(1)
  const [status, setStatus] = useState('')

  const { data: summary } = useQuery({
    queryKey: ['notification-log-summary', companyId],
    queryFn: () => notificationsApi.getLogSummary(companyId, 30),
  })

  const { data, isLoading, isError } = useQuery({
    queryKey: ['notification-log', companyId, page, status],
    queryFn: () => notificationsApi.getLog(companyId, { page, pageSize: 20, status: status || undefined }),
  })

  return (
    <div>
      {summary && (
        <Card className="p-5 mb-4">
          <div className="flex items-center justify-between flex-wrap gap-2 mb-3">
            <p className="text-sm font-semibold text-ink">Сводка за {summary.days} дней</p>
            {summary.channelPaidUntil && (
              <p className="text-xs text-muted">канал оплачен до {format(parseISO(summary.channelPaidUntil), 'd MMM yyyy', { locale: ru })}</p>
            )}
          </div>
          <div className="grid grid-cols-3 sm:grid-cols-6 gap-3 text-center">
            {[
              { label: 'Отправлено', value: summary.sent },
              { label: 'Доставлено', value: summary.delivered },
              { label: 'Прочитано', value: summary.read },
              { label: 'Не доставлено', value: summary.failed },
              { label: 'Пропущено', value: summary.skipped },
              { label: 'Протухло', value: summary.expired },
            ].map((t) => (
              <div key={t.label}>
                <p className="text-lg font-bold text-ink">{t.value}</p>
                <p className="text-[11px] text-muted">{t.label}</p>
              </div>
            ))}
          </div>
          {summary.byCompany && summary.byCompany.length > 0 && (
            <div className="mt-4 pt-3 border-t border-line flex flex-col gap-1.5">
              <p className="text-xs font-medium text-muted uppercase tracking-wide mb-1">По компаниям на этом канале</p>
              {summary.byCompany.map((c) => (
                <div key={c.companyId} className="flex justify-between text-xs text-ink-soft">
                  <span>{c.companyName}</span>
                  <span>
                    отправлено {c.sent} · доставлено {c.delivered} · не доставлено {c.failed}
                  </span>
                </div>
              ))}
            </div>
          )}
        </Card>
      )}

      <div className="mb-4">
        <select
          value={status}
          onChange={(e) => {
            setStatus(e.target.value)
            setPage(1)
          }}
          className="rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold bg-white text-ink"
        >
          {STATUS_FILTERS.map((f) => (
            <option key={f.value} value={f.value}>
              {f.label}
            </option>
          ))}
        </select>
      </div>

      {isLoading ? (
        <div className="grid gap-2">
          {Array.from({ length: 5 }).map((_, i) => (
            <div key={i} className="h-14 bg-cream-deep rounded-2xl animate-pulse" />
          ))}
        </div>
      ) : isError || !data ? (
        <Card className="p-10 text-center text-muted">
          <Icon name="alert-circle" size={28} strokeWidth={1.6} className="mx-auto mb-2" />
          <p>Не удалось загрузить журнал доставки.</p>
        </Card>
      ) : data.items.length === 0 ? (
        <Card className="p-10 text-center text-muted">
          <Icon name="megaphone" size={28} strokeWidth={1.6} className="mx-auto mb-2" />
          <p>Уведомлений пока нет</p>
        </Card>
      ) : (
        <div className="grid gap-2">
          {data.items.map((n) => (
            <Card key={n.id} className="p-3.5 flex items-center justify-between gap-3 flex-wrap">
              <div className="min-w-0">
                <div className="flex items-center gap-2 flex-wrap">
                  {/* API_CONTRACT_CYCLE7.md §51 — "text erased by retention" is read from
                      `contentRedacted`, never inferred from an empty string (a genuinely nameless
                      guest booking also has an empty `recipientName`, and must not look the same). */}
                  {n.contentRedacted ? (
                    <span className="text-xs text-muted italic">текст удалён по сроку хранения</span>
                  ) : (
                    <>
                      <span className="font-medium text-ink text-sm">{n.recipientName || 'Клиент'}</span>
                      <span className="text-xs text-muted">{n.recipientPhoneMasked}</span>
                    </>
                  )}
                  <span className="text-xs text-muted">{n.typeText}</span>
                </div>
                <p className="text-xs text-muted mt-0.5">{format(parseISO(n.createdAt), 'd MMM yyyy, HH:mm', { locale: ru })}</p>
              </div>
              <span
                className={`text-xs font-medium px-2.5 py-1 rounded-full shrink-0 ${
                  n.status === 'Delivered'
                    ? 'bg-success-bg text-success'
                    : n.status === 'Failed' || n.status === 'Cancelled'
                      ? 'bg-danger-bg text-danger'
                      : n.status === 'Expired' || n.status === 'Skipped'
                        ? 'bg-warning-bg text-warning'
                        : 'bg-cream-deep text-ink-soft'
                }`}
                title={STATUS_LABEL[n.status]}
              >
                {n.statusText}
              </span>
            </Card>
          ))}
        </div>
      )}

      {data && (
        <Pagination page={data.page} pageSize={data.pageSize} total={data.total} hasNext={data.hasNext} onPageChange={setPage} />
      )}
    </div>
  )
}
