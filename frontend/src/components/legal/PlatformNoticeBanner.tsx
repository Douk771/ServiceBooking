import { useState } from 'react'
import { Link } from 'react-router-dom'
import { NoticeLink } from './NoticeLink'
import { usePlatformNotices, useAcknowledgeNotice } from '../../hooks/usePlatformNotices'
import { PlatformNoticeAttachment } from './PlatformNoticeAttachment'
import { getNoticeAcknowledgeErrorMessage } from '../../utils/noticeError'
import { Icon } from '../ui/Icon'

/**
 * API_CONTRACT_CYCLE20.md §434.1/§441 item 7 (US-20-03, Т20-02). One `GET /api/legal/notices?scope=pending`
 * per session (react-query `staleTime` 5 min, shared with `/notices`), rendered next to
 * `LegalUpdateBanner` in `App.tsx` for any authenticated user. NOT a modal — it never blocks the rest
 * of the app, and it shows EVERY unread notice, not just the first (§404.5: "показывает все
 * непрочитанные"). `role="status"` per item — a live region announcing what's newly visible, not an
 * alert dialog.
 */
export function PlatformNoticeBanner() {
  const { data } = usePlatformNotices('pending')
  const ackMut = useAcknowledgeNotice()
  const [ackError, setAckError] = useState<Record<string, string>>({})
  const [attachmentFor, setAttachmentFor] = useState<{ id: string; title: string } | null>(null)

  if (!data || data.items.length === 0) return null

  return (
    <div className="flex flex-col gap-2 bg-info-bg px-4 py-3">
      {data.items.map((notice) => (
        <div key={notice.id} role="status" className="max-w-[900px] mx-auto w-full flex items-start gap-3 flex-wrap">
          <Icon name="alert-circle" size={15} strokeWidth={1.8} className="text-info shrink-0 mt-0.5" />
          <div className="min-w-0 flex-1">
            <p className="text-sm font-semibold text-info">{notice.title}</p>
            {/* body is a flat-text snapshot composed server-side — printed verbatim, never re-derived. */}
            <p className="text-sm text-info whitespace-pre-wrap">{notice.body}</p>
            <div className="flex flex-wrap items-center gap-3 mt-1.5">
              {notice.linkUrl && (
                <NoticeLink to={notice.linkUrl} className="text-xs font-medium text-info underline hover:no-underline">
                  Подробнее
                </NoticeLink>
              )}
              {notice.attachment && (
                <button
                  type="button"
                  onClick={() => setAttachmentFor({ id: notice.id, title: notice.attachment!.title })}
                  className="text-xs font-medium text-info underline hover:no-underline"
                >
                  Читать новую редакцию
                </button>
              )}
              <Link to="/notices" className="text-xs text-info/80 underline hover:no-underline">
                Все уведомления
              </Link>
            </div>
          </div>
          <div className="flex flex-col items-end gap-1 shrink-0">
            <button
              type="button"
              disabled={ackMut.isPending}
              onClick={() => {
                setAckError((e) => ({ ...e, [notice.id]: '' }))
                ackMut.mutate(notice.id, { onError: (err) => setAckError((e) => ({ ...e, [notice.id]: getNoticeAcknowledgeErrorMessage(err) })) })
              }}
              className="font-semibold text-sm text-info hover:opacity-70 transition-opacity disabled:opacity-50"
            >
              {data.acknowledgeButtonText}
            </button>
            <p className="text-[11px] text-info/80 max-w-[220px] text-right">{data.acknowledgeCaption}</p>
            {ackError[notice.id] && <p className="text-[11px] text-danger">{ackError[notice.id]}</p>}
          </div>
        </div>
      ))}

      {attachmentFor && (
        <PlatformNoticeAttachment noticeId={attachmentFor.id} title={attachmentFor.title} onClose={() => setAttachmentFor(null)} />
      )}
    </div>
  )
}
