import { useMemo, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { InlineError } from '@/components/ui/InlineError'
import { Modal } from '@/components/ui/Modal'
import { useDebouncedValue } from '@/hooks/useDebouncedValue'
import { fmtDateTime } from '@/utils/dateFormat'
import { staysCompaniesApi } from '../../api/staysCompanies'
import type { ArrivalReminderSettingsDto, StaysServiceConflictDto } from '../../types'
import {
  REMINDER_DROP_TEXT,
  REMINDER_ERROR_TEXT,
  REMINDER_PUSH_ACK_TEXT,
  REMINDER_TIME_OPTIONS,
  REMINDER_WARNING_TEXT,
  insertAtCursor,
  isDefaultDraft,
  templateForSave,
} from '../../utils/reminderTemplate'
import { getStayErrorMessage, httpStatus, plainBody, readConflict } from '../../utils/stayError'
import { ErrorState, Skeleton } from '../StatePanels'
import { StayNotice } from '../StayNotice'
import { SavedNote, SectionCard, SelectField, SwitchRow } from './formParts'

const reminderKey = (companyId: string) => ['stays-arrival-reminder', companyId] as const

/**
 * «Напоминание о заезде» (US-39-19/20, API_CONTRACT_CYCLE39.md §39.33). The owner edits the time, the text with buttons of
 * placeholders and the switch of the text in push. Text under the field: `StayReminderTemplateOwnerNotice`; under the push
 * switch: `StayReminderPushOwnerNotice` + an acknowledgement that goes into the request with the notice's version. The preview shows the
 * three channels as the server renders them, the lines dropped from push with their reason, the soft warnings and the errors. Signs of
 * access codes in the text (409 `ReminderConfirmationRequired`) are not forbidden: a dialog asks and the save is repeated with the
 * confirmation (ЮР39-4). Whether the reminder is sent at all is the switch of «Информация к заселению» above (read-only here).
 */
export function ArrivalReminderCard({ companyId }: { companyId: string }) {
  const query = useQuery({ queryKey: reminderKey(companyId), queryFn: () => staysCompaniesApi.arrivalReminder(companyId), staleTime: 0 })
  if (query.isLoading) return <Skeleton className="h-64" />
  if (query.isError || !query.data) {
    return <ErrorState message={getStayErrorMessage(query.error, 'Не удалось загрузить настройки напоминания.')} onRetry={() => void query.refetch()} />
  }
  // The editor is keyed by what the server stores, so a saved state re-initialises the draft.
  const s = query.data
  return <ReminderEditor key={`${s.time}|${s.template ?? ''}|${s.pushTextEnabled}`} companyId={companyId} settings={s} />
}

function ReminderEditor({ companyId, settings }: { companyId: string; settings: ArrivalReminderSettingsDto }) {
  const qc = useQueryClient()
  const areaRef = useRef<HTMLTextAreaElement>(null)
  const [time, setTime] = useState(settings.time)
  const [draft, setDraft] = useState(settings.effectiveTemplate)
  const [pushText, setPushText] = useState(settings.pushTextEnabled)
  const [pushAck, setPushAck] = useState(settings.pushTextEnabled)
  const [saved, setSaved] = useState(false)
  const [formError, setFormError] = useState('')
  const [fieldError, setFieldError] = useState('')
  const [confirm, setConfirm] = useState<StaysServiceConflictDto | null>(null)
  const [historyOpen, setHistoryOpen] = useState(false)

  const template = templateForSave(draft, settings.defaultTemplate)
  const debounced = useDebouncedValue({ template, pushText }, 400)
  const preview = useQuery({
    queryKey: ['stays-arrival-reminder-preview', companyId, debounced],
    queryFn: () => staysCompaniesApi.previewArrivalReminder(companyId, { template: debounced.template, pushTextEnabled: debounced.pushText }),
    placeholderData: (prev) => prev,
    staleTime: 0,
  })
  const p = preview.data

  const history = useQuery({
    queryKey: ['stays-arrival-reminder-history', companyId],
    queryFn: () => staysCompaniesApi.arrivalReminderHistory(companyId),
    enabled: historyOpen,
  })

  const save = useMutation({
    mutationFn: (confirmCodeMarkers: boolean) =>
      staysCompaniesApi.saveArrivalReminder(companyId, {
        time,
        template,
        pushTextEnabled: pushText,
        confirmCodeMarkers,
        ownerNoticeVersion: settings.ownerNotice.version,
        pushNoticeVersion: pushText ? settings.pushNotice.version : null,
      }),
    onSuccess: (next) => {
      qc.setQueryData(reminderKey(companyId), next)
      void qc.invalidateQueries({ queryKey: ['stays-arrival-reminder-history', companyId] })
      setSaved(true)
      setConfirm(null)
      setFormError('')
      setFieldError('')
    },
    onError: (err) => {
      const conflict = readConflict<StaysServiceConflictDto>(err)
      if (conflict?.code === 'ReminderConfirmationRequired') {
        setConfirm(conflict)
        return
      }
      setConfirm(null)
      const text = plainBody(err)
      if (httpStatus(err) === 400 && text) setFieldError(text)
      else setFormError(getStayErrorMessage(err, 'Не удалось сохранить напоминание.'))
    },
  })

  const dirty = time !== settings.time || template !== settings.template || pushText !== settings.pushTextEnabled
  const blockedByErrors = !!p && p.errors.length > 0
  const pushNeedsAck = pushText && !settings.pushTextEnabled && !pushAck

  const insert = (token: string) => {
    const el = areaRef.current
    const { text, cursor } = insertAtCursor(draft, el?.selectionStart ?? draft.length, el?.selectionEnd ?? draft.length, token)
    setDraft(text)
    setSaved(false)
    requestAnimationFrame(() => {
      el?.focus()
      el?.setSelectionRange(cursor, cursor)
    })
  }

  const timeOptions = useMemo(() => REMINDER_TIME_OPTIONS.map((t) => ({ value: t, label: t })), [])

  return (
    <SectionCard
      id="arrival-reminder"
      title="Напоминание о заезде"
      description={
        settings.enabled
          ? 'Гость получит напоминание накануне заезда. Включить или выключить его можно в блоке «Информация к заселению» выше.'
          : 'Сейчас напоминание выключено. Включить его можно переключателем «Напоминание накануне заезда» в блоке выше — текст можно подготовить заранее.'
      }
    >
      <form
        noValidate
        className="flex flex-col gap-5"
        onSubmit={(e) => {
          e.preventDefault()
          setFormError('')
          setFieldError('')
          if (pushNeedsAck || blockedByErrors) return
          save.mutate(false)
        }}
      >
        <SelectField
          label="Время отправки"
          value={time}
          options={timeOptions}
          onChange={(v) => {
            setTime(v)
            setSaved(false)
          }}
          hint="Накануне заезда, по времени Шерегеша: с 08:00 до 22:00"
        />

        <div className="flex flex-col gap-2">
          <label htmlFor="reminder-template" className="text-[13px] font-medium text-[#4A4038]">
            Текст напоминания
          </label>
          <textarea
            id="reminder-template"
            ref={areaRef}
            rows={7}
            maxLength={settings.limits.templateMaxLength}
            value={draft}
            aria-describedby="reminder-notice reminder-counter"
            onChange={(e) => {
              setDraft(e.target.value)
              setSaved(false)
            }}
            className="rounded-xl border border-line bg-white px-4 py-3 text-sm text-ink outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep"
          />
          <div className="flex flex-wrap items-center justify-between gap-2">
            <p id="reminder-counter" className="text-xs text-muted">
              {draft.length} из {settings.limits.templateMaxLength}
            </p>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="min-h-[44px]"
              disabled={isDefaultDraft(draft, settings.defaultTemplate)}
              onClick={() => {
                setDraft(settings.defaultTemplate)
                setSaved(false)
              }}
            >
              Вернуть по умолчанию
            </Button>
          </div>
          {fieldError && (
            <p role="alert" className="text-sm text-danger">
              {fieldError}
            </p>
          )}
          <StayNotice textKey="StayReminderTemplateOwnerNotice" id="reminder-notice" />
          <div>
            <p className="mb-1.5 text-xs font-medium text-ink-soft">Вставить подстановку</p>
            <ul className="flex flex-wrap gap-2">
              {settings.placeholders.map((ph) => (
                <li key={ph.token}>
                  <button
                    type="button"
                    title={ph.description}
                    aria-label={`Вставить ${ph.token}: ${ph.description}`}
                    onClick={() => insert(ph.token)}
                    className="min-h-[44px] rounded-full border border-line bg-white px-3.5 text-xs font-medium text-ink hover:border-line-strong"
                  >
                    {ph.token}
                  </button>
                </li>
              ))}
            </ul>
          </div>
        </div>

        <div className="flex flex-col gap-3">
          <SwitchRow
            label="Показывать текст напоминания в push"
            hint="Выключено: в push приходит «Завтра заезд — откройте бронь»"
            checked={pushText}
            onChange={(v) => {
              setPushText(v)
              setSaved(false)
            }}
          />
          <StayNotice textKey="StayReminderPushOwnerNotice" />
          {pushText && !settings.pushTextEnabled && (
            <label className="flex min-h-[44px] cursor-pointer items-start gap-3 text-sm text-ink">
              <input type="checkbox" checked={pushAck} onChange={(e) => setPushAck(e.target.checked)} className="mt-0.5 h-5 w-5 shrink-0 accent-gold" />
              <span>{REMINDER_PUSH_ACK_TEXT}</span>
            </label>
          )}
        </div>

        <div aria-live="polite" className="flex flex-col gap-3" data-testid="reminder-preview">
          <h3 className="text-[15px] font-semibold text-ink">Как увидит гость</h3>
          {preview.isError && !p ? (
            <ErrorState message={getStayErrorMessage(preview.error, 'Не удалось построить предпросмотр.')} onRetry={() => void preview.refetch()} />
          ) : !p ? (
            <Skeleton className="h-40" />
          ) : (
            <div className={`flex flex-col gap-3 ${preview.isFetching ? 'opacity-60 transition-opacity' : ''}`}>
              {p.errors.length > 0 && (
                <ul role="alert" className="rounded-2xl bg-danger-bg px-4 py-3 text-sm text-danger">
                  {p.errors.map((e) => (
                    <li key={e.code}>{e.message || REMINDER_ERROR_TEXT[e.code]}</li>
                  ))}
                </ul>
              )}
              {p.warnings.length > 0 && (
                <ul className="rounded-2xl bg-warning-bg px-4 py-3 text-sm text-warning">
                  {p.warnings.map((w) => (
                    <li key={w}>{REMINDER_WARNING_TEXT[w]}</li>
                  ))}
                </ul>
              )}
              {p.confirmationRequired && (
                <p className="rounded-2xl bg-warning-bg px-4 py-3 text-sm text-warning" data-testid="reminder-markers-hint">
                  В тексте есть похожее на код доступа{p.markers.length > 0 ? `: ${p.markers.join(', ')}` : ''}. При сохранении мы спросим подтверждение.
                </p>
              )}
              <PreviewBox title="Мессенджер" text={p.messenger.text} length={p.messenger.length} />
              <PreviewBox title="Страница брони" text={p.page.text} length={p.page.length} />
              <PreviewBox
                title="Push"
                text={p.push.text}
                length={p.push.length}
                note={
                  !pushText
                    ? 'Текст в push выключен — гость увидит фиксированную фразу.'
                    : p.push.usesFixedText
                      ? 'Ничего не подошло для push — гость увидит фиксированную фразу.'
                      : undefined
                }
              >
                {pushText && p.push.dropped.length > 0 && (
                  <ul className="mt-2 text-xs text-ink-soft" data-testid="push-dropped">
                    {p.push.dropped.map((d) => (
                      <li key={`${d.line}-${d.reason}`}>
                        Строка {d.line} «{d.text}» не попадёт в push: {REMINDER_DROP_TEXT[d.reason]}
                      </li>
                    ))}
                  </ul>
                )}
              </PreviewBox>
            </div>
          )}
        </div>

        {formError && <InlineError>{formError}</InlineError>}
        <div className="flex flex-wrap items-center gap-3">
          <Button type="submit" size="lg" loading={save.isPending} disabled={!dirty || pushNeedsAck || blockedByErrors} className="min-h-[44px]">
            Сохранить напоминание
          </Button>
          <SavedNote show={saved} />
        </div>

        <div>
          <button type="button" aria-expanded={historyOpen} onClick={() => setHistoryOpen((v) => !v)} className="min-h-[44px] text-sm font-semibold text-gold-dark underline">
            История изменений
          </button>
          {historyOpen &&
            (history.isLoading ? (
              <Skeleton className="mt-2 h-24" />
            ) : history.isError ? (
              <ErrorState message={getStayErrorMessage(history.error, 'Не удалось загрузить историю.')} onRetry={() => void history.refetch()} />
            ) : history.data && history.data.length > 0 ? (
              <ul className="mt-2 flex flex-col gap-2 text-sm text-ink-soft" data-testid="reminder-history">
                {history.data.map((h) => (
                  <li key={h.changedAtUtc} className="rounded-xl border border-line px-4 py-2">
                    <span className="font-medium text-ink">{fmtDateTime(h.changedAtUtc)}</span>, {h.changedByName}: {h.previousTime} → {h.newTime}
                    {h.previousTemplate !== h.newTemplate && ', текст изменён'}
                    {h.previousPushText !== h.newPushText && (h.newPushText ? ', push с текстом включён' : ', push с текстом выключен')}
                    {h.codeMarkersConfirmed && h.codeMarkersHit ? `, подтверждено: ${h.codeMarkersHit}` : ''}
                  </li>
                ))}
              </ul>
            ) : (
              <p className="mt-2 text-sm text-muted">Изменений пока не было.</p>
            ))}
        </div>
      </form>

      {confirm && (
        <Modal title="Похоже на код доступа" onClose={() => setConfirm(null)} dismissible={!save.isPending}>
          <div className="flex flex-col gap-3 text-sm text-ink-soft" data-testid="markers-dialog">
            <p className="text-ink">{confirm.message}</p>
            {confirm.markers && confirm.markers.length > 0 && <p className="rounded-xl bg-cream-deep px-4 py-2 font-medium text-ink">{confirm.markers.join(', ')}</p>}
            <StayNotice textKey="StayCheckInInfoOwnerNotice" />
            <div className="mt-2 flex gap-3">
              <Button variant="secondary" className="min-h-[44px] flex-1" disabled={save.isPending} onClick={() => setConfirm(null)}>
                Вернуться к тексту
              </Button>
              <Button className="min-h-[44px] flex-1" loading={save.isPending} onClick={() => save.mutate(true)}>
                Сохранить всё равно
              </Button>
            </div>
          </div>
        </Modal>
      )}
    </SectionCard>
  )
}

function PreviewBox({ title, text, length, note, children }: { title: string; text: string; length: number; note?: string; children?: React.ReactNode }) {
  return (
    <section className="rounded-2xl border border-line bg-cream/40 px-4 py-3" aria-label={`Предпросмотр: ${title}`}>
      <div className="flex items-baseline justify-between gap-3">
        <h4 className="text-xs font-semibold uppercase tracking-wide text-gold-dark">{title}</h4>
        <span className="text-xs text-muted">{length} симв.</span>
      </div>
      <p className="mt-1 whitespace-pre-line break-words text-sm text-ink">{text}</p>
      {note && <p className="mt-1.5 text-xs text-ink-soft">{note}</p>}
      {children}
    </section>
  )
}
