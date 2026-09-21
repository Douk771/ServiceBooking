import { useEffect, useRef, useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { AxiosError } from 'axios'
import { notificationsApi } from '../../api/notifications'
import { useLegalText } from '../../hooks/useLegalText'
import { findSection, splitLegalSections } from '../../utils/legalSections'
import { findHitMarkers } from '../../utils/templateMarkers'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Icon } from '../../components/ui/Icon'
import { TemplateAcknowledgementModal } from './TemplateAcknowledgementModal'
import { getNotificationErrorMessage } from '../../utils/notificationError'
import type { NotificationType } from '../../types'

const TYPE_LABELS: Record<NotificationType, string> = {
  BookingConfirmed: 'Подтверждение записи',
  Reminder: 'Напоминание о визите',
  BookingCancelled: 'Отмена записи',
  BookingRescheduled: 'Перенос записи',
  StaffBookingCreated: 'Новая запись (персоналу)',
  StaffBookingCancelled: 'Отмена записи (персоналу)',
}
const EDITABLE_TYPES: NotificationType[] = ['BookingConfirmed', 'Reminder', 'BookingCancelled', 'BookingRescheduled']

/** Server body of the second 400 case in §47.2 — markers hit and `confirmedDespiteMarkers` wasn't set. */
interface MarkersHitError {
  markersHit: string[]
  message: string
}

function isMarkersHitError(data: unknown): data is MarkersHitError {
  return !!data && typeof data === 'object' && Array.isArray((data as MarkersHitError).markersHit)
}

/** US-59 — one template per notification type, owned by the company (not the channel). */
export function NotificationTemplatesTab({ companyId }: { companyId: string }) {
  const qc = useQueryClient()
  const [activeType, setActiveType] = useState<NotificationType>('Reminder')
  const [body, setBody] = useState('')
  const [dirty, setDirty] = useState(false)
  const [preview, setPreview] = useState<string | null>(null)
  const [showAck, setShowAck] = useState(false)
  const [ackError, setAckError] = useState('')
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  const { data, isLoading, isError } = useQuery({
    queryKey: ['notification-templates', companyId],
    queryFn: () => notificationsApi.getTemplates(companyId),
  })

  // §47.1 — `warningTextKey`/`warningVersion` come from the templates response itself (today always
  // `TemplateAdWarning`), not hardcoded, so a future second warning text wouldn't need a frontend change.
  const { data: warningText } = useLegalText(data?.warningTextKey ?? 'TemplateAdWarning')
  const warningSections = warningText ? splitLegalSections(warningText.contentHtml) : []
  const fieldWarningHtml = findSection(warningSections, 'Текст у поля ввода')?.html ?? null
  const heightenedHtml = findSection(warningSections, 'Усиленное предупреждение')?.html ?? null
  const confirmationHtml = findSection(warningSections, 'Подтверждение при сохранении')?.html ?? null
  const referenceHtml = findSection(warningSections, 'Справка')?.html ?? null

  const currentTemplate = data?.templates.find((t) => t.type === activeType)
  const markersHit = data ? findHitMarkers(body, data.adMarkers) : []

  useEffect(() => {
    setBody(currentTemplate?.body ?? '')
    setDirty(false)
    setPreview(null)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeType, data])

  const saveMut = useMutation({
    mutationFn: (confirmedDespiteMarkers: boolean) => {
      if (!data) throw new Error('templates not loaded')
      return notificationsApi.updateTemplate(companyId, activeType, body, {
        warningVersion: data.warningVersion,
        accepted: true,
        confirmedDespiteMarkers,
      })
    },
    onSuccess: (updated) => {
      setDirty(false)
      setShowAck(false)
      setAckError('')
      qc.setQueryData(['notification-templates', companyId], (prev: typeof data) =>
        prev ? { ...prev, templates: prev.templates.map((t) => (t.type === activeType ? updated : t)) } : prev,
      )
    },
    onError: (err: unknown) => {
      // §47.2 second 400 — the server's own check hit markers the client-side pass missed (dictionary
      // drift, race). Surface it in the same modal rather than a bare error, so the owner doesn't have
      // to reopen the dialog to see why.
      const ax = err as AxiosError
      if (ax?.response?.status === 400 && isMarkersHitError(ax.response.data)) {
        setAckError(ax.response.data.message)
      } else {
        setAckError(getNotificationErrorMessage(err))
      }
    },
  })

  const previewMut = useMutation({
    mutationFn: () => notificationsApi.previewTemplate(companyId, activeType, body),
    onSuccess: (res) => setPreview(res.rendered),
  })

  const insertPlaceholder = (token: string) => {
    const el = textareaRef.current
    if (!el) {
      setBody((b) => b + token)
      setDirty(true)
      return
    }
    const start = el.selectionStart ?? body.length
    const end = el.selectionEnd ?? body.length
    const next = body.slice(0, start) + token + body.slice(end)
    setBody(next)
    setDirty(true)
    requestAnimationFrame(() => {
      el.focus()
      el.selectionStart = el.selectionEnd = start + token.length
    })
  }

  if (isLoading) return <div className="h-72 bg-cream-deep rounded-2xl animate-pulse" />
  if (isError || !data)
    return (
      <Card className="p-10 text-center text-muted">
        <Icon name="alert-circle" size={28} strokeWidth={1.6} className="mx-auto mb-2" />
        <p>Не удалось загрузить шаблоны сообщений.</p>
      </Card>
    )

  const placeholders = data.placeholders.filter((p) => p.types.includes('*') || p.types.includes(activeType))

  return (
    <div>
      <div className="flex gap-1 bg-cream-deep p-1 rounded-full mb-4 w-fit flex-wrap">
        {EDITABLE_TYPES.map((t) => (
          <button
            key={t}
            onClick={() => setActiveType(t)}
            className={`px-3.5 py-2 rounded-full text-[13px] font-semibold whitespace-nowrap transition-all ${
              activeType === t ? 'bg-white text-ink shadow-sm' : 'text-gold-dark hover:text-ink'
            }`}
          >
            {TYPE_LABELS[t]}
          </button>
        ))}
      </div>

      <Card className="p-6">
        <div className="flex flex-wrap gap-1.5 mb-3">
          {placeholders.map((p) => (
            <button
              key={p.token}
              type="button"
              title={p.description}
              onClick={() => insertPlaceholder(p.token)}
              className="text-xs px-2.5 py-1 rounded-lg bg-cream-deep text-ink-soft hover:bg-gold/20 hover:text-gold-dark transition-colors font-mono"
            >
              {p.token}
            </button>
          ))}
        </div>

        <textarea
          ref={textareaRef}
          rows={6}
          value={body}
          onChange={(e) => {
            setBody(e.target.value)
            setDirty(true)
            setPreview(null)
          }}
          placeholder={currentTemplate?.defaultBody}
          className="w-full rounded-xl border border-line px-4 py-3 text-sm outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep resize-none bg-white text-ink font-mono"
        />

        {markersHit.length > 0 && (
          <p className="mt-1.5 text-xs text-warning">Похоже на рекламу: {markersHit.join(', ')}</p>
        )}

        {/* US-33 п. 4, US-59 п. 3 — non-editable tail, never part of `body`. */}
        <div className="mt-2 rounded-xl bg-cream-deep px-4 py-2.5 text-xs text-muted">
          {data.unsubscribeLine}
          <span className="ml-1.5 italic">(добавляется автоматически, редактированию не подлежит)</span>
        </div>

        {/* API_CONTRACT_CYCLE5.md §47.3 — obligatory, uncollapsible, not in a tooltip/help (US-69 п. 1). */}
        <div className="mt-3 rounded-xl bg-warning-bg text-warning text-xs px-3.5 py-2.5 flex items-start gap-2">
          <Icon name="alert-circle" size={14} strokeWidth={1.8} className="shrink-0 mt-0.5" />
          {fieldWarningHtml ? (
            <div className="legal-content [&_p]:mb-1 last:[&_p]:mb-0" dangerouslySetInnerHTML={{ __html: fieldWarningHtml }} />
          ) : (
            <span>
              Сервисное сообщение о записи согласия не требует. Но любая скидка, акция или приглашение делают
              сообщение рекламным (ст. 18 ФЗ «О рекламе») — ответственность за это несёте вы, а такие сообщения
              заметно повышают риск блокировки номера.
            </span>
          )}
        </div>

        {referenceHtml && (
          <details className="mt-2 text-xs text-muted">
            <summary className="cursor-pointer text-gold hover:text-gold-dark">Что можно и чего нельзя</summary>
            <div className="legal-content mt-2 [&_p]:mb-1.5" dangerouslySetInnerHTML={{ __html: referenceHtml }} />
          </details>
        )}

        {preview && (
          <div className="mt-3 rounded-xl border border-line bg-cream px-4 py-3 text-sm text-ink-soft whitespace-pre-wrap">
            {preview}
          </div>
        )}

        {previewMut.isError && (
          <p className="text-sm text-danger mt-3">{getNotificationErrorMessage(previewMut.error)}</p>
        )}
        {saveMut.isSuccess && !dirty && (
          <p className="text-sm text-success mt-3 flex items-center gap-1.5">
            <Icon name="check" size={14} strokeWidth={2} /> Сохранено
          </p>
        )}

        <div className="flex flex-wrap gap-3 mt-4">
          <Button
            disabled={!dirty}
            onClick={() => {
              setAckError('')
              setShowAck(true)
            }}
          >
            Сохранить
          </Button>
          <Button variant="secondary" loading={previewMut.isPending} onClick={() => previewMut.mutate()}>
            Предпросмотр
          </Button>
          {!currentTemplate?.isDefault && (
            <Button
              variant="ghost"
              onClick={() => {
                setBody('')
                setDirty(true)
                setAckError('')
                setShowAck(true)
              }}
            >
              Вернуть текст платформы
            </Button>
          )}
        </div>
      </Card>

      {showAck && (
        <TemplateAcknowledgementModal
          confirmationHtml={confirmationHtml}
          heightenedHtml={heightenedHtml}
          markersHit={markersHit}
          loading={saveMut.isPending}
          error={ackError}
          onClose={() => setShowAck(false)}
          onConfirm={(confirmedDespiteMarkers) => saveMut.mutate(confirmedDespiteMarkers)}
        />
      )}
    </div>
  )
}
