import { useState } from 'react'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { NoticeLink } from '../components/legal/NoticeLink'
import { usePlatformNotices, useAcknowledgeNotice } from '../hooks/usePlatformNotices'
import { PlatformNoticeAttachment } from '../components/legal/PlatformNoticeAttachment'
import { getNoticeAcknowledgeErrorMessage } from '../utils/noticeError'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { Icon } from '../components/ui/Icon'
import type { PlatformNoticeDto } from '../api/platformNotices'

function fmtDate(d: string) {
  return format(parseISO(d), 'd MMM yyyy', { locale: ru })
}

function NoticeCard({
  notice,
  acknowledgeButtonText,
  acknowledgeCaption,
  onOpenAttachment,
}: {
  notice: PlatformNoticeDto
  acknowledgeButtonText: string
  acknowledgeCaption: string
  onOpenAttachment: () => void
}) {
  const ackMut = useAcknowledgeNotice()
  const [error, setError] = useState('')

  return (
    <Card className={`p-5 ${notice.revokedAt ? 'opacity-60' : ''}`}>
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div className="min-w-0">
          <p className="text-sm font-semibold text-ink">{notice.title}</p>
          <p className="text-xs text-muted mt-0.5">
            Опубликовано {fmtDate(notice.publishedAt)}
            {notice.effectiveFrom && ` · вступает в силу ${fmtDate(notice.effectiveFrom)}`}
          </p>
        </div>
        {notice.revokedAt ? (
          <span className="text-xs px-2 py-0.5 rounded-full bg-line text-ink-soft shrink-0">Отозвано</span>
        ) : notice.acknowledged ? (
          <span className="text-xs px-2 py-0.5 rounded-full bg-success-bg text-success shrink-0">Прочитано</span>
        ) : null}
      </div>
      <p className="text-sm text-ink-soft whitespace-pre-wrap mt-3">{notice.body}</p>
      <div className="flex flex-wrap items-center gap-3 mt-3">
        {notice.linkUrl && (
          <NoticeLink to={notice.linkUrl} className="text-xs font-medium text-gold-dark underline hover:no-underline">
            Подробнее
          </NoticeLink>
        )}
        {notice.attachment && (
          <button type="button" onClick={onOpenAttachment} className="text-xs font-medium text-gold-dark underline hover:no-underline">
            Читать новую редакцию
          </button>
        )}
        {!notice.revokedAt && !notice.acknowledged && (
          <Button
            size="sm"
            variant="secondary"
            loading={ackMut.isPending}
            onClick={() => {
              setError('')
              ackMut.mutate(notice.id, { onError: (err) => setError(getNoticeAcknowledgeErrorMessage(err)) })
            }}
          >
            {acknowledgeButtonText}
          </Button>
        )}
      </div>
      {!notice.revokedAt && !notice.acknowledged && <p className="text-[11px] text-muted mt-1">{acknowledgeCaption}</p>}
      {error && <p className="text-xs text-danger mt-1">{error}</p>}
    </Card>
  )
}

/**
 * `/notices` — API_CONTRACT_CYCLE20.md §434.1 (`scope=all`, US-20-03). Everything the caller is/was
 * an addressee of, including already-acknowledged and revoked notices, sorted newest-first by the
 * server. Reachable from `PlatformNoticeBanner`'s "Все уведомления" link.
 */
export function NoticesPage() {
  const { data, isLoading, isError } = usePlatformNotices('all')
  const [attachment, setAttachment] = useState<{ id: string; title: string } | null>(null)

  return (
    <div className="max-w-[720px] mx-auto px-6 pt-11 pb-24">
      <h1 className="font-serif text-[28px] font-medium text-ink mb-6">Уведомления сервиса</h1>

      {isLoading ? (
        <div className="grid gap-3">
          {[1, 2, 3].map((i) => (
            <div key={i} className="h-24 bg-cream-deep rounded-2xl animate-pulse" />
          ))}
        </div>
      ) : isError || !data ? (
        <Card className="p-10 text-center text-muted">
          <Icon name="alert-circle" size={28} strokeWidth={1.6} className="mx-auto mb-2" />
          <p>Не удалось загрузить уведомления.</p>
        </Card>
      ) : data.items.length === 0 ? (
        <Card className="p-10 text-center text-muted">
          <p>Уведомлений пока нет</p>
        </Card>
      ) : (
        <div className="grid gap-3">
          {data.items.map((notice) => (
            <NoticeCard
              key={notice.id}
              notice={notice}
              acknowledgeButtonText={data.acknowledgeButtonText}
              acknowledgeCaption={data.acknowledgeCaption}
              onOpenAttachment={() => setAttachment({ id: notice.id, title: notice.attachment?.title ?? notice.title })}
            />
          ))}
        </div>
      )}

      {attachment && <PlatformNoticeAttachment noticeId={attachment.id} title={attachment.title} onClose={() => setAttachment(null)} />}
    </div>
  )
}
