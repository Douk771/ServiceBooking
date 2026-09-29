import { useQuery } from '@tanstack/react-query'
import { Modal } from '../ui/Modal'
import { platformNoticesApi } from '../../api/platformNotices'

/**
 * API_CONTRACT_CYCLE20.md §434.3 (Т20-02 п. 5, Р11) — the future-edition HTML snapshot. Fetched
 * through `api` (carries the auth token) as plain text and shown ONLY inside
 * `<iframe sandbox="" srcdoc>` — never `dangerouslySetInnerHTML` into the app's own DOM, and never
 * `sandbox="allow-scripts"`. This is the ONLY place in the app that's allowed to render this kind of
 * content at all.
 */
export function PlatformNoticeAttachment({
  noticeId,
  title,
  onClose,
  fetchHtml = platformNoticesApi.getAttachment,
}: {
  noticeId: string
  title: string
  onClose: () => void
  /** Defaults to the addressee route (`GET /api/legal/notices/{id}/attachment`); SuperAdmin's own
   *  preview of what was published uses the sibling admin route instead (§434.3/§434.7) — same
   *  rendering rules (iframe sandbox, no HTML in the app's own DOM), different endpoint. */
  fetchHtml?: (noticeId: string) => Promise<string>
}) {
  const { data: html, isLoading, isError } = useQuery({
    queryKey: ['platform-notice-attachment', noticeId, fetchHtml === platformNoticesApi.getAttachment ? 'addressee' : 'admin'],
    queryFn: () => fetchHtml(noticeId),
  })

  return (
    <Modal title={title} onClose={onClose}>
      {isLoading ? (
        <div className="h-96 bg-cream-deep rounded-xl animate-pulse" />
      ) : isError || !html ? (
        <p className="text-sm text-danger">Не удалось загрузить документ.</p>
      ) : (
        <iframe title={title} sandbox="" srcDoc={html} className="w-full h-[70vh] rounded-xl border border-line bg-white" />
      )}
    </Modal>
  )
}
