import { useEffect, useRef, useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { notificationsApi } from '../../api/notifications'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Icon } from '../../components/ui/Icon'
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

/** US-59 — one template per notification type, owned by the company (not the channel). */
export function NotificationTemplatesTab({ companyId }: { companyId: string }) {
  const qc = useQueryClient()
  const [activeType, setActiveType] = useState<NotificationType>('Reminder')
  const [body, setBody] = useState('')
  const [dirty, setDirty] = useState(false)
  const [preview, setPreview] = useState<string | null>(null)
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  const { data, isLoading, isError } = useQuery({
    queryKey: ['notification-templates', companyId],
    queryFn: () => notificationsApi.getTemplates(companyId),
  })

  const currentTemplate = data?.templates.find((t) => t.type === activeType)

  useEffect(() => {
    setBody(currentTemplate?.body ?? '')
    setDirty(false)
    setPreview(null)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeType, data])

  const saveMut = useMutation({
    mutationFn: (text: string) => notificationsApi.updateTemplate(companyId, activeType, text),
    onSuccess: (updated) => {
      setDirty(false)
      qc.setQueryData(['notification-templates', companyId], (prev: typeof data) =>
        prev
          ? { ...prev, templates: prev.templates.map((t) => (t.type === activeType ? updated : t)) }
          : prev,
      )
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

        {/* US-33 п. 4, US-59 п. 3 — non-editable tail, never part of `body`. */}
        <div className="mt-2 rounded-xl bg-cream-deep px-4 py-2.5 text-xs text-muted">
          {data.unsubscribeLine}
          <span className="ml-1.5 italic">(добавляется автоматически, редактированию не подлежит)</span>
        </div>

        {/* R6 — mandatory, next to the field, not tucked into help. */}
        <div className="mt-3 rounded-xl bg-warning-bg text-warning text-xs px-3.5 py-2.5 flex items-start gap-2">
          <Icon name="alert-circle" size={14} strokeWidth={1.8} className="shrink-0 mt-0.5" />
          <span>
            Сервисное сообщение о записи согласия не требует. Но любая скидка, акция или приглашение делают
            сообщение рекламным (ст. 18 ФЗ «О рекламе») — ответственность за это несёте вы, а такие сообщения
            заметно повышают риск блокировки номера.
          </span>
        </div>

        {preview && (
          <div className="mt-3 rounded-xl border border-line bg-cream px-4 py-3 text-sm text-ink-soft whitespace-pre-wrap">
            {preview}
          </div>
        )}

        {(saveMut.isError || previewMut.isError) && (
          <p className="text-sm text-danger mt-3">{getNotificationErrorMessage(saveMut.error ?? previewMut.error)}</p>
        )}
        {saveMut.isSuccess && !dirty && (
          <p className="text-sm text-success mt-3 flex items-center gap-1.5">
            <Icon name="check" size={14} strokeWidth={2} /> Сохранено
          </p>
        )}

        <div className="flex flex-wrap gap-3 mt-4">
          <Button loading={saveMut.isPending} disabled={!dirty} onClick={() => saveMut.mutate(body)}>
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
                saveMut.mutate('')
              }}
            >
              Вернуть текст платформы
            </Button>
          )}
        </div>
      </Card>
    </div>
  )
}
