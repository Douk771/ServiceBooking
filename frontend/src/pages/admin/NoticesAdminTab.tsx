import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import {
  adminNoticesApi,
  type AdminPlatformNoticeDto,
  type PlatformNoticeCreatableKind,
  type PlatformNoticeCreateInput,
  type NoticeAudienceType,
  type TermsChangeDocumentType,
} from '../../api/adminNotices'
import { adminBillingApi } from '../../api/adminBilling'
import { plansApi } from '../../api/plans'
import { getAdminNoticeErrorMessage, getAdminNoticeRevokeErrorMessage } from '../../utils/noticeError'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Modal } from '../../components/ui/Modal'
import { Icon } from '../../components/ui/Icon'
import { Pagination } from '../../components/ui/Pagination'
import { PlatformNoticeAttachment } from '../../components/legal/PlatformNoticeAttachment'

function fmtDate(d: string) {
  return format(parseISO(d), 'd MMM yyyy', { locale: ru })
}
function fmtDateTime(d: string) {
  return format(parseISO(d), 'd MMM yyyy, HH:mm', { locale: ru })
}

const KIND_LABEL: Record<PlatformNoticeCreatableKind, string> = {
  PriceChange: 'Изменение цены',
  TermsChange: 'Новая редакция документа',
  Suspension: 'Приостановление',
  NewProcessor: 'Новый обработчик',
  Other: 'Другое',
}

const AUDIENCE_LABEL: Record<NoticeAudienceType, string> = {
  AllOwners: 'Все владельцы',
  OwnersOnPlans: 'Владельцы на тарифах',
  BillingAccount: 'Один биллинг-аккаунт',
  AllClients: 'Все клиенты',
}

/**
 * API_CONTRACT_CYCLE20.md §434.5 (US-20-03) — the publish/preview form follows the matrix EXACTLY:
 * which fields are shown/required depends on `kind`. The server is still the source of truth for
 * every rule (this is UX, not validation the backend trusts) — every 400 the matrix can produce is
 * still shown verbatim via `getAdminNoticeErrorMessage`.
 */
function PublishNoticeModal({ onClose }: { onClose: () => void }) {
  const qc = useQueryClient()
  const [kind, setKind] = useState<PlatformNoticeCreatableKind>('PriceChange')
  const [audienceType, setAudienceType] = useState<NoticeAudienceType>('AllOwners')
  const [planIds, setPlanIds] = useState<string[]>([])
  const [billingAccountSearch, setBillingAccountSearch] = useState('')
  const [billingAccountId, setBillingAccountId] = useState<string | null>(null)
  const [effectiveFrom, setEffectiveFrom] = useState('')
  const [title, setTitle] = useState('')
  const [body, setBody] = useState('')
  const [linkUrl, setLinkUrl] = useState('')
  const [priceChangePlanId, setPriceChangePlanId] = useState('')
  const [oldPrice, setOldPrice] = useState('')
  const [newPrice, setNewPrice] = useState('')
  const [documentType, setDocumentType] = useState<TermsChangeDocumentType>('TermsOwner')
  const [changesSummary, setChangesSummary] = useState('')
  const [attachmentTitle, setAttachmentTitle] = useState('')
  const [attachmentHtml, setAttachmentHtml] = useState('')
  const [preview, setPreview] = useState<{ title: string; body: string; audienceCount: number } | null>(null)
  const [error, setError] = useState('')

  const { data: plans } = useQuery({ queryKey: ['admin-plans'], queryFn: plansApi.list })
  const { data: accountResults } = useQuery({
    queryKey: ['admin-billing-accounts-search', billingAccountSearch],
    queryFn: () => adminBillingApi.listAccounts({ search: billingAccountSearch || undefined, page: 1, pageSize: 5 }),
    enabled: audienceType === 'BillingAccount' && billingAccountSearch.length > 0,
  })

  const needsFreeText = kind === 'Suspension' || kind === 'NewProcessor' || kind === 'Other'
  const needsEffectiveFrom = kind === 'PriceChange' || kind === 'TermsChange' || kind === 'NewProcessor'
  const needsAttachment = kind === 'TermsChange'

  function buildInput(): PlatformNoticeCreateInput {
    return {
      kind,
      audience: {
        type: audienceType,
        planIds: audienceType === 'OwnersOnPlans' ? planIds : null,
        billingAccountId: audienceType === 'BillingAccount' ? billingAccountId : null,
      },
      effectiveFrom: needsEffectiveFrom ? effectiveFrom || null : null,
      title: needsFreeText ? title : null,
      body: needsFreeText ? body : null,
      linkUrl: linkUrl.trim() || null,
      priceChange: kind === 'PriceChange' ? { planId: priceChangePlanId, oldPricePerMonth: Number(oldPrice) || 0, newPricePerMonth: Number(newPrice) || 0 } : null,
      termsChange: kind === 'TermsChange' ? { documentType, changesSummary } : null,
      attachment: needsAttachment && attachmentTitle && attachmentHtml ? { title: attachmentTitle, html: attachmentHtml } : null,
    }
  }

  const previewMut = useMutation({
    mutationFn: () => adminNoticesApi.preview(buildInput()),
    onSuccess: (data) => {
      setError('')
      setPreview(data)
    },
    onError: (err: unknown) => {
      setPreview(null)
      setError(getAdminNoticeErrorMessage(err))
    },
  })

  const publishMut = useMutation({
    mutationFn: () => adminNoticesApi.publish(buildInput()),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin-notices'] })
      onClose()
    },
    onError: (err: unknown) => setError(getAdminNoticeErrorMessage(err)),
  })

  return (
    <Modal title="Опубликовать уведомление" onClose={onClose}>
      <div className="flex flex-col gap-4">
        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-medium text-[#4A4038]">Вид</label>
          <select
            value={kind}
            onChange={(e) => {
              setKind(e.target.value as PlatformNoticeCreatableKind)
              setPreview(null)
            }}
            className="rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold bg-white text-ink"
          >
            {Object.entries(KIND_LABEL).map(([v, l]) => (
              <option key={v} value={v}>
                {l}
              </option>
            ))}
          </select>
        </div>

        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-medium text-[#4A4038]">Адресат</label>
          <select
            value={audienceType}
            onChange={(e) => setAudienceType(e.target.value as NoticeAudienceType)}
            className="rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold bg-white text-ink"
          >
            {Object.entries(AUDIENCE_LABEL)
              // PriceChange/Suspension never target AllClients (§434.5) — narrowing the dropdown is a
              // UX courtesy only, the server 400 remains authoritative either way.
              .filter(([v]) => !(v === 'AllClients' && (kind === 'PriceChange' || kind === 'Suspension')))
              .filter(([v]) => !(kind === 'Suspension' && v !== 'BillingAccount'))
              .map(([v, l]) => (
                <option key={v} value={v}>
                  {l}
                </option>
              ))}
          </select>
        </div>

        {audienceType === 'OwnersOnPlans' && (
          <div className="flex flex-col gap-1.5">
            <label className="text-[13px] font-medium text-[#4A4038]">Тарифы</label>
            <div className="flex flex-wrap gap-2">
              {(plans ?? []).map((p) => (
                <label key={p.id} className="flex items-center gap-1.5 text-xs bg-cream-deep rounded-full px-3 py-1.5 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={planIds.includes(p.id)}
                    onChange={(e) => setPlanIds((prev) => (e.target.checked ? [...prev, p.id] : prev.filter((id) => id !== p.id)))}
                    className="accent-gold"
                  />
                  {p.name}
                </label>
              ))}
            </div>
          </div>
        )}

        {audienceType === 'BillingAccount' && (
          <div className="flex flex-col gap-1.5">
            <label className="text-[13px] font-medium text-[#4A4038]">Биллинг-аккаунт</label>
            <input
              value={billingAccountSearch}
              onChange={(e) => {
                setBillingAccountSearch(e.target.value)
                setBillingAccountId(null)
              }}
              placeholder="Поиск по владельцу..."
              className="rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold bg-white text-ink"
            />
            {!billingAccountId && (accountResults?.items.length ?? 0) > 0 && (
              <div className="flex flex-col gap-1">
                {accountResults!.items.map((a) => (
                  <button
                    key={a.id}
                    type="button"
                    onClick={() => {
                      setBillingAccountId(a.id)
                      setBillingAccountSearch(a.ownerName)
                    }}
                    className="text-left text-xs px-3 py-2 rounded-lg bg-cream-deep hover:bg-line"
                  >
                    {a.ownerName} · {a.ownerPhoneMasked}
                  </button>
                ))}
              </div>
            )}
            {billingAccountId && <p className="text-xs text-success">Выбран: {billingAccountSearch}</p>}
          </div>
        )}

        {needsEffectiveFrom && (
          <div className="flex flex-col gap-1.5">
            <label className="text-[13px] font-medium text-[#4A4038]">Дата вступления в силу</label>
            <input
              type="date"
              value={effectiveFrom}
              onChange={(e) => setEffectiveFrom(e.target.value)}
              className="rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold bg-white text-ink"
            />
            <p className="text-[11px] text-muted">
              {kind === 'PriceChange' && 'Не раньше чем через 30 дней (§434.5).'}
              {kind === 'TermsChange' && 'Не раньше чем через 15 дней для владельцев / 10 дней для клиентов.'}
              {kind === 'NewProcessor' && 'Не раньше чем через 15 дней для владельческих адресатов (D3 п. 11.9.5); для клиентов не проверяется.'}
            </p>
          </div>
        )}

        {kind === 'PriceChange' && (
          <div className="grid grid-cols-3 gap-3">
            <input
              value={priceChangePlanId}
              onChange={(e) => setPriceChangePlanId(e.target.value)}
              placeholder="ID тарифа"
              className="rounded-xl border border-line px-3 py-2.5 text-sm outline-none focus:border-gold col-span-3"
            />
            <input
              type="number"
              value={oldPrice}
              onChange={(e) => setOldPrice(e.target.value)}
              placeholder="Старая цена"
              className="rounded-xl border border-line px-3 py-2.5 text-sm outline-none focus:border-gold"
            />
            <input
              type="number"
              value={newPrice}
              onChange={(e) => setNewPrice(e.target.value)}
              placeholder="Новая цена"
              className="rounded-xl border border-line px-3 py-2.5 text-sm outline-none focus:border-gold"
            />
          </div>
        )}

        {kind === 'TermsChange' && (
          <>
            <div className="flex flex-col gap-1.5">
              <label className="text-[13px] font-medium text-[#4A4038]">Документ</label>
              <select
                value={documentType}
                onChange={(e) => setDocumentType(e.target.value as TermsChangeDocumentType)}
                className="rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold bg-white text-ink"
              >
                <option value="TermsOwner">Соглашение с компанией</option>
                <option value="TermsClient">Пользовательское соглашение</option>
                <option value="Privacy">Политика обработки персональных данных</option>
              </select>
            </div>
            <div className="flex flex-col gap-1.5">
              <label className="text-[13px] font-medium text-[#4A4038]">Что меняется</label>
              <textarea
                value={changesSummary}
                onChange={(e) => setChangesSummary(e.target.value)}
                rows={2}
                maxLength={1000}
                className="rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold resize-none bg-white text-ink"
                placeholder="Например: срок хранения журнала изменений записи — 3 года"
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <label className="text-[13px] font-medium text-[#4A4038]">Вложение — заголовок редакции</label>
              <input
                value={attachmentTitle}
                onChange={(e) => setAttachmentTitle(e.target.value)}
                placeholder="Соглашение с компанией, редакция от 20.10.2026"
                className="rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold bg-white text-ink"
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <label className="text-[13px] font-medium text-[#4A4038]">Вложение — HTML новой редакции</label>
              <textarea
                value={attachmentHtml}
                onChange={(e) => setAttachmentHtml(e.target.value)}
                rows={3}
                className="rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold resize-none bg-white text-ink font-mono text-xs"
                placeholder="<!doctype html>…"
              />
            </div>
          </>
        )}

        {needsFreeText && (
          <>
            <input
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="Заголовок"
              maxLength={200}
              className="rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold bg-white text-ink"
            />
            <textarea
              value={body}
              onChange={(e) => setBody(e.target.value)}
              rows={3}
              maxLength={4000}
              placeholder="Текст уведомления"
              className="rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold resize-none bg-white text-ink"
            />
          </>
        )}

        <input
          value={linkUrl}
          onChange={(e) => setLinkUrl(e.target.value)}
          placeholder="Ссылка (относительный путь, необязательно) — /billing"
          className="rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold bg-white text-ink"
        />

        {preview && (
          <div className="rounded-xl bg-cream-deep px-4 py-3 text-sm">
            <p className="font-semibold text-ink">{preview.title}</p>
            <p className="text-ink-soft whitespace-pre-wrap mt-1">{preview.body}</p>
            <p className="text-xs text-muted mt-2">Адресатов сейчас: {preview.audienceCount}</p>
          </div>
        )}
        {error && <p className="text-sm text-danger">{error}</p>}

        <div className="flex gap-3 pt-1">
          <Button variant="secondary" className="flex-1" loading={previewMut.isPending} onClick={() => previewMut.mutate()}>
            Предпросмотр
          </Button>
          <Button className="flex-1" loading={publishMut.isPending} onClick={() => publishMut.mutate()}>
            Опубликовать
          </Button>
        </div>
      </div>
    </Modal>
  )
}

function RevokeNoticeModal({ notice, onClose }: { notice: AdminPlatformNoticeDto; onClose: () => void }) {
  const qc = useQueryClient()
  const [reason, setReason] = useState('')
  const mut = useMutation({
    mutationFn: () => adminNoticesApi.revoke(notice.id, reason),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin-notices'] })
      onClose()
    },
  })

  return (
    <Modal title={`Отозвать — ${notice.title}`} onClose={onClose}>
      <div className="flex flex-col gap-4">
        <textarea
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          rows={2}
          maxLength={500}
          placeholder="Причина отзыва"
          className="rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold resize-none bg-white text-ink"
        />
        {mut.isError && <p className="text-sm text-danger">{getAdminNoticeRevokeErrorMessage(mut.error)}</p>}
        <div className="flex gap-3 pt-1">
          <Button variant="secondary" className="flex-1" onClick={onClose}>
            Отмена
          </Button>
          <Button variant="danger" className="flex-1" disabled={!reason.trim()} loading={mut.isPending} onClick={() => mut.mutate()}>
            Отозвать
          </Button>
        </div>
      </div>
    </Modal>
  )
}

/** `Suspension`/`Other` never used for §434.5 audience filter above (kept for exhaustiveness). */
export function NoticesAdminTab() {
  const [page, setPage] = useState(1)
  const [publishing, setPublishing] = useState(false)
  const [revoking, setRevoking] = useState<AdminPlatformNoticeDto | null>(null)
  const [attachment, setAttachment] = useState<{ id: string; title: string } | null>(null)

  const { data, isLoading, isError } = useQuery({
    queryKey: ['admin-notices', page],
    queryFn: () => adminNoticesApi.list({ page, pageSize: 20 }),
  })

  return (
    <div>
      <div className="flex justify-end mb-4">
        <Button size="sm" onClick={() => setPublishing(true)}>
          <Icon name="plus" size={14} strokeWidth={2} />
          Опубликовать уведомление
        </Button>
      </div>

      {isLoading ? (
        <div className="grid gap-2">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="h-20 bg-cream-deep rounded-2xl animate-pulse" />
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
        <div className="grid gap-2">
          {data.items.map((n) => (
            <Card key={n.id} className={`p-4 ${n.revokedAt ? 'opacity-60' : ''}`}>
              <div className="flex items-start justify-between gap-3 flex-wrap">
                <div className="min-w-0">
                  <div className="flex items-center gap-2 flex-wrap">
                    <span className="font-medium text-ink text-sm">{n.title}</span>
                    <span className="text-xs text-muted">{KIND_LABEL[n.kind as PlatformNoticeCreatableKind] ?? n.kind}</span>
                    {n.revokedAt && <span className="text-xs px-2 py-0.5 rounded-full bg-line text-ink-soft">Отозвано</span>}
                  </div>
                  <p className="text-xs text-muted mt-0.5">
                    Опубликовано {fmtDateTime(n.publishedAt)}
                    {n.effectiveFrom && ` · вступает в силу ${fmtDate(n.effectiveFrom)}`} · адресатов {n.audienceCount} · прочитали {n.acknowledgedCount}
                  </p>
                </div>
                <div className="flex gap-2 shrink-0">
                  {n.attachment && (
                    <Button size="sm" variant="secondary" onClick={() => setAttachment({ id: n.id, title: n.attachment!.title })}>
                      Вложение
                    </Button>
                  )}
                  {!n.revokedAt && (
                    <Button size="sm" variant="danger" onClick={() => setRevoking(n)}>
                      Отозвать
                    </Button>
                  )}
                </div>
              </div>
            </Card>
          ))}
        </div>
      )}

      {data && <Pagination page={data.page} pageSize={data.pageSize} total={data.total} hasNext={data.hasNext} onPageChange={setPage} />}

      {publishing && <PublishNoticeModal onClose={() => setPublishing(false)} />}
      {revoking && <RevokeNoticeModal notice={revoking} onClose={() => setRevoking(null)} />}
      {attachment && (
        <PlatformNoticeAttachment
          noticeId={attachment.id}
          title={attachment.title}
          fetchHtml={adminNoticesApi.getAttachment}
          onClose={() => setAttachment(null)}
        />
      )}
    </div>
  )
}
