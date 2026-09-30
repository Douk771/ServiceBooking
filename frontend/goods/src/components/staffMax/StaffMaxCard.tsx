import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { staffMaxApi } from '../../api/staffMax'
import { ErrorState, InlineError, LoadingList } from '../StatePanels'
import { getGoodsErrorMessage } from '../../utils/orderError'
import { shouldPollStaffMax } from '../../utils/reports'
import type { StaffMaxLinkSessionDto, StaffMaxStatusDto } from '../../types'

const KEY = ['staff-max']

/**
 * «Заказы в MAX» in the profile's «Устройства и уведомления» (US-25-01/02): staff connect their own MAX chat with a one-time link. The state,
 * its wording, the platform switch and the reason a link cannot be issued are all the server's (`GET /api/staff-max`);
 * the chat id is never in any answer. Polling runs ONLY while `status = Pending` and the link has not expired (§538).
 */
export function StaffMaxCard() {
  const qc = useQueryClient()
  const [session, setSession] = useState<StaffMaxLinkSessionDto | null>(null)
  const [now, setNow] = useState(() => Date.now())
  const [copied, setCopied] = useState(false)

  const q = useQuery({
    queryKey: KEY,
    queryFn: staffMaxApi.status,
    retry: false,
    refetchInterval: (query) => {
      const d = query.state.data as StaffMaxStatusDto | undefined
      const exp = session?.expiresAtUtc ?? d?.pendingSession?.expiresAtUtc
      return d && shouldPollStaffMax(d.status, exp, Date.now()) ? d.pollIntervalSeconds * 1000 : false
    },
  })
  const data = q.data
  const expiresAt = session?.expiresAtUtc ?? data?.pendingSession?.expiresAtUtc ?? null
  const pending = data?.status === 'Pending'

  // Tick once a second while a link is pending, so «Ссылка устарела» appears on time without a request.
  useEffect(() => {
    if (!pending) return
    const t = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(t)
  }, [pending])

  useEffect(() => {
    if (data && data.status !== 'Pending') setSession(null)
  }, [data])

  const link = useMutation({
    mutationFn: staffMaxApi.createLinkSession,
    onSuccess: (s) => {
      setSession(s)
      setNow(Date.now())
      void qc.invalidateQueries({ queryKey: KEY })
    },
  })
  const unlink = useMutation({
    mutationFn: staffMaxApi.unlink,
    onSuccess: () => {
      setSession(null)
      void qc.invalidateQueries({ queryKey: KEY })
    },
  })

  if (q.isLoading)
    return (
      <section aria-labelledby="max-title" className="rounded-2xl border border-line bg-white p-5 sm:p-6 mt-6">
        <h2 id="max-title" className="text-[15px] font-semibold text-ink mb-3">Заказы в MAX</h2>
        <LoadingList rows={1} rowClass="h-14" />
      </section>
    )
  if (q.isError || !data)
    return (
      <section aria-labelledby="max-title" className="rounded-2xl border border-line bg-white p-5 sm:p-6 mt-6">
        <h2 id="max-title" className="text-[15px] font-semibold text-ink mb-3">Заказы в MAX</h2>
        <ErrorState message={getGoodsErrorMessage(q.error, 'Не удалось загрузить состояние MAX.')} onRetry={() => void q.refetch()} />
      </section>
    )
  if (!data.eligible) return null

  const expired = pending && expiresAt !== null && new Date(expiresAt).getTime() <= now
  const showLink = pending && session !== null && !expired
  const copy = async () => {
    if (!session) return
    try {
      await navigator.clipboard.writeText(session.deepLink)
      setCopied(true)
    } catch {
      /* the field below stays selectable */
    }
  }
  const blocked = !data.available || !data.canLink
  const linked = data.status === 'Linked'
  const busy = link.isPending || unlink.isPending

  return (
    <section aria-labelledby="max-title" className="rounded-2xl border border-line bg-white p-5 sm:p-6 mt-6" data-testid="staff-max-card">
      <h2 id="max-title" className="text-[15px] font-semibold text-ink">Заказы в MAX</h2>
      <p className="text-sm text-ink-soft mt-1">Новые заказы и отмены покупателями приходят вам сообщением в MAX. Без имени и телефона покупателя — только номер, время и сумма.</p>

      {blocked && data.unavailableText && (
        <p className="mt-3 rounded-xl bg-cream-deep text-ink-soft text-sm px-4 py-3 flex items-start gap-2.5" data-testid="max-unavailable">
          <Icon name="alert-circle" size={15} strokeWidth={1.8} className="shrink-0 mt-0.5 text-muted" />
          {data.unavailableText}
        </p>
      )}

      <p className="mt-4 text-sm font-medium text-ink" role="status" data-testid="max-status" data-status={data.status}>
        {expired ? 'Ссылка устарела' : data.statusText}
      </p>

      {data.shops.length > 0 && (linked || pending) && (
        <p className="text-xs text-muted mt-1">
          Магазины: {data.shops.map((s) => `${s.name}${s.staffMaxEnabled ? '' : ' (выключено в магазине)'}`).join(', ')}
        </p>
      )}

      {showLink && session && (
        <div className="mt-4">
          <a href={session.deepLink} target="_blank" rel="noreferrer" className="sm:hidden w-full inline-flex items-center justify-center gap-2 font-semibold rounded-full bg-ink hover:bg-ink/90 !text-cream px-5 py-2.5 text-sm mb-3">
            Открыть MAX
          </a>
          {session.qrPngBase64 && (
            <div className="hidden sm:flex flex-col items-start gap-2 mb-3">
              <img src={`data:image/png;base64,${session.qrPngBase64}`} alt="QR-код для перехода к боту MAX и подключения сообщений о заказах" width={180} height={180} className="rounded-2xl border border-line" />
              <p className="text-xs text-muted">Отсканируйте QR-код камерой телефона</p>
            </div>
          )}
          <p className="text-xs text-muted mb-3">
            Пользуетесь MAX в браузере?{' '}
            <a href={session.webLink} target="_blank" rel="noreferrer" className="text-ink underline underline-offset-2 hover:no-underline">Открыть в веб-версии MAX</a>.
          </p>
          <div className="flex items-center gap-2">
            <label htmlFor="staff-max-deep-link" className="sr-only">Ссылка на бота MAX</label>
            <input id="staff-max-deep-link" type="text" readOnly value={session.deepLink} onFocus={(e) => e.currentTarget.select()} className="flex-1 min-w-0 rounded-xl border border-line px-3 py-2 text-xs text-ink-soft bg-cream-deep/40 outline-none" />
            <Button type="button" variant="secondary" size="sm" onClick={() => void copy()} aria-label="Скопировать ссылку">
              <Icon name="copy" size={14} strokeWidth={1.8} />
              {copied ? 'Скопировано' : 'Копировать'}
            </Button>
          </div>
        </div>
      )}

      {(link.isError || unlink.isError) && (
        <div className="mt-3">
          <InlineError>{getGoodsErrorMessage(link.error ?? unlink.error, 'Не удалось выполнить действие с MAX.')}</InlineError>
        </div>
      )}

      <div className="mt-4 flex items-center gap-3 flex-wrap">
        {(!pending || expired || !showLink) && (
          <Button loading={link.isPending} disabled={blocked || unlink.isPending} onClick={() => { setCopied(false); link.mutate() }}>
            {linked ? 'Подключить другой чат' : pending ? 'Получить новую ссылку' : expired ? 'Получить новую' : data.status === 'StoppedInMax' ? 'Подключить снова' : 'Подключить MAX'}
          </Button>
        )}
        {(linked || data.status === 'StoppedInMax' || pending) && (
          <Button variant="secondary" loading={unlink.isPending} disabled={busy && !unlink.isPending} onClick={() => unlink.mutate()}>
            Отключить
          </Button>
        )}
      </div>
    </section>
  )
}
