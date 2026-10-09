import { useEffect, useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { fmtDate } from '../../utils/dateFormat'
import { adminNotificationsApi } from '../../api/platformSettings'
import { adminNumbersApi, type AdminChannelCardDto, type AdminChannelDto } from '../../api/notificationNumbers'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { Icon } from '../../components/ui/Icon'
import { Pagination } from '../../components/ui/Pagination'
import { Modal } from '../../components/ui/Modal'
import { getNotificationErrorMessage } from '../../utils/notificationError'
import { TRANSPORT_FILTER_OPTIONS, TRANSPORT_LABELS } from '../../utils/notificationTransport'
import { adminErrorText } from './adminChannelsHelpers'
import { getPricingPublicBlockedMessage } from '../../utils/legalError'
import type { NotificationTransport, PricingPublicBlockedReason } from '../../types'

// ── Summary tile row ─────────────────────────────────────────────────────────
// Cycle 40 (§40.13): five tiles — the three states the owner sees, the applications waiting for a confirmation and the numbers whose paid period ends soon.

function SummaryRow() {
  const { data } = useQuery({ queryKey: ['admin-channel-summary'], queryFn: adminNumbersApi.summary })
  if (!data) return <div className="h-20 bg-cream-deep rounded-2xl animate-pulse mb-4" />

  const tiles = [
    { label: 'Работает', value: data.working },
    { label: 'Нужно действие', value: data.actionRequired },
    { label: 'Выключен', value: data.off },
    { label: 'Заявок на оплату', value: data.pendingRequests },
    { label: 'Истекает за 7 дней', value: data.expiringIn7Days },
  ]

  return (
    <div className="grid grid-cols-2 md:grid-cols-5 gap-3 mb-5">
      {tiles.map((t) => (
        <Card key={t.label} className="p-4 text-center">
          <p className="text-xl font-bold text-ink">{t.value}</p>
          <p className="text-[11px] text-muted mt-0.5">{t.label}</p>
        </Card>
      ))}
    </div>
  )
}

// ── Channels table, card and «Подтвердить оплату» ────────────────────────────
// Cycle 40 (§40.13): the SAME three states the owner sees (displayStatus/displayText come from the server), the payment text, and the actions the server allows.

const DISPLAY_STATUS_LABEL: Record<string, string> = { Working: 'Работает', ActionRequired: 'Нужно действие', Off: 'Выключен' }
const DISPLAY_STATUS_CLASS: Record<string, string> = {
  Working: 'bg-success-bg text-success',
  ActionRequired: 'bg-warning-bg text-warning',
  Off: 'bg-cream-deep text-ink-soft',
}
const PAYMENT_OPTIONS = [
  { value: '', label: 'Любая оплата' },
  { value: 'Paid', label: 'Оплачен' },
  { value: 'Trial', label: 'Пробный' },
  { value: 'Requested', label: 'Заявка' },
  { value: 'NotPaid', label: 'Не оплачен' },
  { value: 'Suspended', label: 'Приостановлен' },
]
const STATUS_OPTIONS = [
  { value: '', label: 'Любой статус' },
  { value: 'Working', label: 'Работает' },
  { value: 'ActionRequired', label: 'Нужно действие' },
  { value: 'Off', label: 'Выключен' },
]

function ConfirmPaymentModal({ channel, onClose }: { channel: AdminChannelDto; onClose: () => void }) {
  const qc = useQueryClient()
  const [months, setMonths] = useState('1')
  const [comment, setComment] = useState('')
  const mut = useMutation({
    mutationFn: () => adminNumbersApi.confirmPayment(channel.id, { months: Number(months), comment: comment.trim() || null }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['admin-channels'] })
      void qc.invalidateQueries({ queryKey: ['admin-channel-summary'] })
      void qc.invalidateQueries({ queryKey: ['admin-channel-card', channel.id] })
      onClose()
    },
  })
  return (
    <Modal title="Подтвердить оплату" onClose={onClose} dismissible={!mut.isPending}>
      <p className="text-sm text-ink-soft mb-4">
        {TRANSPORT_LABELS[channel.transport]} · {channel.ownerName}. Срок продлевается от конца текущего оплаченного периода (или от сегодняшнего дня).
      </p>
      <div className="grid gap-4">
        <Input label="На сколько месяцев (1–12)" type="number" min={1} max={12} value={months} onChange={(e) => setMonths(e.target.value)} />
        <Input label="Комментарий (счёт, платёж)" value={comment} maxLength={500} onChange={(e) => setComment(e.target.value)} />
      </div>
      {mut.isError && <p className="text-sm text-danger mt-3">{adminErrorText(mut.error)}</p>}
      <div className="flex justify-end gap-2 mt-5">
        <Button variant="secondary" onClick={onClose} disabled={mut.isPending}>Отмена</Button>
        <Button loading={mut.isPending} disabled={!(Number(months) >= 1 && Number(months) <= 12)} onClick={() => mut.mutate()}>Подтвердить</Button>
      </div>
    </Modal>
  )
}

function ChannelCardModal({ id, onClose }: { id: string; onClose: () => void }) {
  const { data, isLoading, isError } = useQuery({ queryKey: ['admin-channel-card', id], queryFn: () => adminNumbersApi.card(id) })
  const card: AdminChannelCardDto | undefined = data
  return (
    <Modal title="Карточка номера" onClose={onClose}>
      {isLoading && <div className="h-32 bg-cream-deep rounded-2xl animate-pulse" />}
      {isError && <p className="text-sm text-danger">Не удалось загрузить карточку номера.</p>}
      {card && (
        <div className="grid gap-4 text-sm">
          <div>
            <p className="font-medium text-ink">{card.channel.ownerName} · {TRANSPORT_LABELS[card.channel.transport]}</p>
            <p className="text-ink-soft">{card.channel.displayText}</p>
            <p className="text-xs text-muted mt-1">
              {card.channel.paymentText} · {card.companies.length} компаний аккаунта
              {card.inn && <> · ИНН {card.inn}</>}
              {card.providerServerCountry && <> · сервер провайдера: {card.providerServerCountry}</>}
            </p>
            {card.lastTest && <p className="text-xs text-muted mt-1">Проверка: {card.lastTest.text}</p>}
            {card.replacedByChannelId && <p className="text-xs text-muted mt-1">Заменён другим номером</p>}
            {card.replacesChannelId && <p className="text-xs text-muted mt-1">Заменяет прежний номер</p>}
          </div>
          <div>
            <h3 className="font-semibold text-ink mb-1.5">Оплата и приостановки</h3>
            <ul className="grid gap-1">
              {card.paymentEvents.length === 0 && <li className="text-muted">Записей нет</li>}
              {card.paymentEvents.map((e, i) => (
                <li key={i} className="text-xs text-ink-soft">
                  {fmtDate(e.occurredAtUtc)} · {e.kind === 'PaymentConfirmed' ? 'оплата подтверждена' : e.kind === 'OptionChanged' ? 'опция изменена' : e.kind === 'Suspended' ? 'приостановлен' : 'возобновлён'}
                  {e.changedByName && <> · {e.changedByName}</>}
                  {e.newPaidUntil && <> · до {fmtDate(e.newPaidUntil)}</>}
                  {e.comment && <> · {e.comment}</>}
                </li>
              ))}
            </ul>
          </div>
          <div>
            <h3 className="font-semibold text-ink mb-1.5">Состояния номера</h3>
            <ul className="grid gap-1">
              {card.stateEvents.length === 0 && <li className="text-muted">Записей нет</li>}
              {card.stateEvents.map((e, i) => (
                <li key={i} className="text-xs text-ink-soft">
                  {fmtDate(e.occurredAtUtc)} · {e.reasonText}{e.detail && <> · {e.detail}</>}
                </li>
              ))}
            </ul>
          </div>
        </div>
      )}
    </Modal>
  )
}

function ChannelsList() {
  const [page, setPage] = useState(1)
  const [transport, setTransport] = useState<NotificationTransport | ''>('')
  const [displayStatus, setDisplayStatus] = useState('')
  const [payment, setPayment] = useState('')
  const [includeReplaced, setIncludeReplaced] = useState(false)
  const [actionError, setActionError] = useState('')
  const [confirming, setConfirming] = useState<AdminChannelDto | null>(null)
  const [cardId, setCardId] = useState<string | null>(null)
  const qc = useQueryClient()

  const { data, isLoading } = useQuery({
    queryKey: ['admin-channels', page, transport, displayStatus, payment, includeReplaced],
    queryFn: () =>
      adminNumbersApi.list({
        page, pageSize: 20, transport: transport || undefined,
        displayStatus: (displayStatus || undefined) as never, payment: (payment || undefined) as never,
        includeReplaced: includeReplaced || undefined,
      }),
  })

  const refresh = () => {
    setActionError('')
    void qc.invalidateQueries({ queryKey: ['admin-channels'] })
    void qc.invalidateQueries({ queryKey: ['admin-channel-summary'] })
  }
  const suspendMut = useMutation({
    mutationFn: (id: string) => adminNotificationsApi.suspend(id),
    onSuccess: refresh,
    onError: (err: unknown) => setActionError(getNotificationErrorMessage(err)),
  })
  const resumeMut = useMutation({
    mutationFn: (id: string) => adminNotificationsApi.resume(id),
    onSuccess: refresh,
    onError: (err: unknown) => setActionError(getNotificationErrorMessage(err)),
  })

  if (isLoading)
    return (
      <div className="grid gap-3">
        {Array.from({ length: 4 }).map((_, i) => (
          <div key={i} className="h-20 bg-cream-deep rounded-2xl animate-pulse" />
        ))}
      </div>
    )

  const selectClass = 'rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold bg-white text-ink'
  const resetPage = () => setPage(1)

  return (
    <div>
      {actionError && <p className="text-sm text-danger mb-3">{actionError}</p>}

      <div className="mb-3 flex flex-wrap items-center gap-2">
        <select aria-label="Канал" value={transport} onChange={(e) => { setTransport(e.target.value as NotificationTransport | ''); resetPage() }} className={selectClass}>
          {TRANSPORT_FILTER_OPTIONS.map((f) => (
            <option key={f.value} value={f.value}>{f.label}</option>
          ))}
        </select>
        <select aria-label="Статус" value={displayStatus} onChange={(e) => { setDisplayStatus(e.target.value); resetPage() }} className={selectClass}>
          {STATUS_OPTIONS.map((f) => (<option key={f.value} value={f.value}>{f.label}</option>))}
        </select>
        <select aria-label="Оплата" value={payment} onChange={(e) => { setPayment(e.target.value); resetPage() }} className={selectClass}>
          {PAYMENT_OPTIONS.map((f) => (<option key={f.value} value={f.value}>{f.label}</option>))}
        </select>
        <label className="flex items-center gap-2 text-sm text-ink-soft cursor-pointer">
          <input type="checkbox" className="accent-gold" checked={includeReplaced} onChange={(e) => { setIncludeReplaced(e.target.checked); resetPage() }} />
          Показать заменённые
        </label>
      </div>

      <div className="grid gap-3">
        {(data?.items ?? []).map((c) => {
          const actions = c.availableActions ?? []
          return (
            <Card key={c.id} className="p-4 flex items-center justify-between gap-4 flex-wrap">
              <div>
                <div className="flex items-center gap-2 flex-wrap">
                  <span className="font-medium text-ink">{c.ownerName}</span>
                  <span className="text-xs text-muted">{c.ownerPhoneMasked}</span>
                  <span className="text-[11px] font-medium px-2 py-0.5 rounded-full bg-cream-deep text-ink-soft">{TRANSPORT_LABELS[c.transport]}</span>
                  {c.displayStatus && (
                    <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${DISPLAY_STATUS_CLASS[c.displayStatus]}`}>{DISPLAY_STATUS_LABEL[c.displayStatus]}</span>
                  )}
                  <span className="text-xs px-2 py-0.5 rounded-full bg-cream-deep text-ink-soft">{c.paymentText}</span>
                </div>
                <p className="text-sm text-ink-soft mt-0.5">{c.displayText}</p>
                <p className="text-xs text-muted mt-0.5">
                  {c.companyCount} компаний
                  {c.requestedAt && <> · заявка от {fmtDate(c.requestedAt)}</>}
                  {c.idleSince && <> · простой с {fmtDate(c.idleSince)}</>}
                  {/* §50.2 — INN is visible only to the owner and to SuperAdmin. */}
                  {c.inn && <> · ИНН {c.inn}</>}
                </p>
              </div>
              <div className="flex gap-2 shrink-0 flex-wrap">
                <Button size="sm" variant="ghost" onClick={() => setCardId(c.id)}>Карточка</Button>
                {actions.includes('ConfirmPayment') && (
                  <Button size="sm" onClick={() => setConfirming(c)}>Подтвердить оплату</Button>
                )}
                {actions.includes('Suspend') && (
                  <Button size="sm" variant="danger" loading={suspendMut.isPending} onClick={() => suspendMut.mutate(c.id)}>Приостановить</Button>
                )}
                {actions.includes('Resume') && (
                  <Button size="sm" variant="secondary" loading={resumeMut.isPending} onClick={() => resumeMut.mutate(c.id)}>Возобновить</Button>
                )}
              </div>
            </Card>
          )
        })}
        {data?.items.length === 0 && (
          <Card className="p-10 text-center text-muted">
            <Icon name="megaphone" size={28} strokeWidth={1.6} className="mx-auto mb-2" />
            <p>Номеров пока нет</p>
          </Card>
        )}
      </div>
      {data && data.total != null && data.page != null && data.pageSize != null && (
        <Pagination page={data.page} pageSize={data.pageSize} total={data.total} hasNext={data.page * data.pageSize < data.total} onPageChange={setPage} />
      )}
      {confirming && <ConfirmPaymentModal channel={confirming} onClose={() => setConfirming(null)} />}
      {cardId && <ChannelCardModal id={cardId} onClose={() => setCardId(null)} />}
    </div>
  )
}

// ── Messenger availability and the stop-cock (cycle 40, §40.12–§40.13) ───────

function MessengersCard() {
  const qc = useQueryClient()
  const { data, isLoading } = useQuery({ queryKey: ['admin-platform-settings'], queryFn: adminNotificationsApi.getSettings })
  const mut = useMutation({
    mutationFn: (patch: { whatsAppOptionOpen?: boolean; maxOptionOpen?: boolean; customerMessagingEnabled?: boolean }) =>
      adminNotificationsApi.updateSettings({ ...(data as NonNullable<typeof data>), ...patch }),
    onSuccess: (res) => {
      qc.setQueryData(['admin-platform-settings'], res)
      void qc.invalidateQueries({ queryKey: ['admin-channels'] })
    },
  })
  if (isLoading || !data) return <div className="h-32 bg-cream-deep rounded-2xl animate-pulse mb-5" />

  const rows: { key: 'whatsAppOptionOpen' | 'maxOptionOpen' | 'customerMessagingEnabled'; label: string; hint: string; on: boolean }[] = [
    { key: 'whatsAppOptionOpen', label: 'WhatsApp — открыто для владельцев', on: !!data.whatsAppOptionOpen,
      hint: 'Закрытый мессенджер не продаётся, не показывается в мастере и ценах и не выдаётся в пробном периоде; уже оплаченные номера работают до конца срока.' },
    { key: 'maxOptionOpen', label: 'MAX — открыто для владельцев', on: !!data.maxOptionOpen,
      hint: 'То же правило для MAX. Цена и опубликованная оферта задаются отдельно: без них опция не продаётся и у открытого мессенджера.' },
    { key: 'customerMessagingEnabled', label: 'Рассылки клиентам в мессенджеры', on: data.customerMessagingEnabled !== false,
      hint: 'Общий стоп-кран. Выключен — сообщения не ставятся в очередь, ожидающие не уходят, владельцы видят «временно отключены платформой»; номера не удаляются.' },
  ]

  return (
    <Card className="p-6 mb-5">
      <h2 className="text-base font-semibold text-ink mb-1">Подключение мессенджеров</h2>
      <p className="text-sm text-muted mb-4">Переключатели действуют сразу, без релиза, на новые заявки и новые сообщения.</p>
      <div className="grid gap-4">
        {rows.map((r) => (
          <div key={r.key} className="flex items-start justify-between gap-4">
            <div>
              <label htmlFor={`sw-${r.key}`} className="text-sm font-medium text-ink block mb-0.5">{r.label}</label>
              <p className="text-xs text-muted max-w-md">{r.hint}</p>
            </div>
            <button
              id={`sw-${r.key}`}
              type="button"
              role="switch"
              aria-checked={r.on}
              disabled={mut.isPending}
              onClick={() => mut.mutate({ [r.key]: !r.on })}
              className={`relative shrink-0 w-11 h-6 rounded-full transition-colors disabled:opacity-40 ${r.on ? 'bg-gold-dark' : 'bg-cream-deep border border-line-strong'}`}
            >
              <span className={`absolute top-0.5 left-0.5 w-5 h-5 rounded-full bg-white shadow transition-transform ${r.on ? 'translate-x-5' : 'translate-x-0'}`} />
            </button>
          </div>
        ))}
      </div>
      {mut.isError && <p className="text-sm text-danger mt-3">{getNotificationErrorMessage(mut.error)}</p>}
    </Card>
  )
}

// ── Platform settings (idle days, trial, public prices) ──────────────────────────────────────

// Copy for pricingPublicBlockedReason (API_CONTRACT_CYCLE11.md §114.1) — informational only, shown
// next to the toggle before the operator even tries to switch it on.
const PRICING_BLOCKED_LABEL: Record<PricingPublicBlockedReason, string> = {
  OfferIsDraft:
    'Публичные цены нельзя включить, пока оферта — черновая редакция. Опубликуйте правовые документы.',
  LegalUnavailable: 'Правовые документы временно недоступны — проверьте манифест.',
}

function PlatformSettingsCard() {
  const qc = useQueryClient()
  const { data, isLoading } = useQuery({ queryKey: ['admin-platform-settings'], queryFn: adminNotificationsApi.getSettings })
  const [idleDays, setIdleDays] = useState('3')
  // Local toggle state so a rejected PUT (409) can be visibly reverted rather than left stuck
  // "on" while the server never applied it (API_CONTRACT_CYCLE11.md §119 п. 1).
  const [pricingPublicEnabled, setPricingPublicEnabled] = useState(false)

  // Cycle 18 (API_CONTRACT_CYCLE18.md §367) — "настройки пробного периода" (Д17: not "настройки
  // акции"). Editing these affects only NEW trials (Д19) — already-issued trials keep their own
  // snapshot, this screen doesn't and can't touch that.
  const [trialDurationDays, setTrialDurationDays] = useState('14')
  const [trialMailingWindowDays, setTrialMailingWindowDays] = useState('7')
  const [trialThresholds, setTrialThresholds] = useState('7,3,1')
  // Cycle 18 review (finding #9): "changed" must be tracked from user input (onChange), not inferred
  // by diffing local state against the server value. `GetTrialDurationDaysAsync` can legitimately
  // return `null` for a corrupted/out-of-range stored value (PlatformSettings.cs) — comparing against
  // that would make every save "change" the field the admin never touched, silently overwriting it
  // and re-coupling this save to trial validation it has nothing to do with.
  const [trialDurationTouched, setTrialDurationTouched] = useState(false)
  const [trialMailingWindowTouched, setTrialMailingWindowTouched] = useState(false)
  const [trialThresholdsTouched, setTrialThresholdsTouched] = useState(false)

  useEffect(() => {
    if (!data) return
    setIdleDays(String(data.channelIdleDays))
    setPricingPublicEnabled(data.pricingPublicEnabled)
    if (data.trialDurationDays != null) setTrialDurationDays(String(data.trialDurationDays))
    if (data.trialMailingWindowDays != null) setTrialMailingWindowDays(String(data.trialMailingWindowDays))
    if (data.trialWarningThresholdsDays != null) setTrialThresholds(data.trialWarningThresholdsDays.join(','))
    setTrialDurationTouched(false)
    setTrialMailingWindowTouched(false)
    setTrialThresholdsTouched(false)
  }, [data])

  // Client-side courtesy check only (§367: "окно ≤ длительности") — the server is the real gate and
  // also checks the thresholds against the wording promised by the current terms text (§367.1),
  // which this screen has no way to know client-side.
  const parsedTrialDuration = Number(trialDurationDays)
  const parsedTrialMailingWindow = Number(trialMailingWindowDays)
  const trialWindowExceedsDuration =
    !Number.isNaN(parsedTrialDuration) &&
    !Number.isNaN(parsedTrialMailingWindow) &&
    parsedTrialMailingWindow > parsedTrialDuration

  const mut = useMutation({
    mutationFn: (nextPricingPublicEnabled: boolean) => {
      const parsedIdleDays = Number(idleDays)
      if (Number.isNaN(parsedIdleDays)) {
        throw new Error('Некорректный срок простоя — исправьте поле перед сохранением.')
      }
      // Cycle 18 §367: all three trial fields are nullable on the wire, and null/absent means "leave
      // unchanged" — same convention as isPublic/sortOrder on AdminPlanInput. Sending them unconditionally
      // would silently overwrite a value the admin never touched, and would block saving unrelated
      // fields (price/idle days) behind trial validation errors that have nothing to do with what's
      // being changed. So: only include a trial field in the request when the admin actually edited it
      // (tracked via onChange, not by diffing against the server value — the server value can be `null`
      // for reasons unrelated to "unchanged", e.g. a corrupted stored duration; diffing against that
      // would misfire as "changed" for a field nobody touched).
      const trialDurationChanged = trialDurationTouched
      const trialMailingWindowChanged = trialMailingWindowTouched
      const trialThresholdsChanged = trialThresholdsTouched

      let trialDurationDaysToSend: number | null = null
      if (trialDurationChanged) {
        if (Number.isNaN(parsedTrialDuration) || parsedTrialDuration < 1) {
          throw new Error('Некорректная длительность пробного периода — исправьте поле перед сохранением.')
        }
        trialDurationDaysToSend = parsedTrialDuration
      }

      let trialMailingWindowDaysToSend: number | null = null
      if (trialMailingWindowChanged) {
        if (Number.isNaN(parsedTrialMailingWindow) || parsedTrialMailingWindow < 1) {
          throw new Error('Некорректное окно рассылок — исправьте поле перед сохранением.')
        }
        trialMailingWindowDaysToSend = parsedTrialMailingWindow
      }

      if ((trialDurationChanged || trialMailingWindowChanged) && trialWindowExceedsDuration) {
        throw new Error('Окно рассылок не может быть длиннее длительности пробного периода.')
      }

      let trialWarningThresholdsDaysToSend: number[] | null = null
      if (trialThresholdsChanged) {
        const thresholds = trialThresholds
          .split(',')
          .map((s) => s.trim())
          .filter((s) => s !== '')
          .map(Number)
        if (thresholds.length === 0 || thresholds.some((n) => Number.isNaN(n) || n < 1)) {
          throw new Error('Некорректные пороги предупреждений — перечислите положительные числа через запятую.')
        }
        trialWarningThresholdsDaysToSend = thresholds
      }

      return adminNotificationsApi.updateSettings({
        // Cycle 40 (§40.13): the retired field is accepted and IGNORED by the server; it is sent back as read.
        channelPricePerMonth: data?.channelPricePerMonth ?? null,
        channelIdleDays: parsedIdleDays,
        pricingPublicEnabled: nextPricingPublicEnabled,
        pricingPublicBlockedReason: data?.pricingPublicBlockedReason ?? null,
        trialDurationDays: trialDurationDaysToSend,
        trialMailingWindowDays: trialMailingWindowDaysToSend,
        trialWarningThresholdsDays: trialWarningThresholdsDaysToSend,
      })
    },
    onSuccess: (res) => {
      qc.setQueryData(['admin-platform-settings'], res)
      qc.invalidateQueries({ queryKey: ['notification-channel-offer'] })
      setPricingPublicEnabled(res.pricingPublicEnabled)
    },
    onError: () => {
      // 409 rejects the whole request, including the toggle — snap it back to the last known
      // server state rather than leave it showing a change that never happened.
      setPricingPublicEnabled(data?.pricingPublicEnabled ?? false)
    },
  })

  if (isLoading) return <div className="h-32 bg-cream-deep rounded-2xl animate-pulse mb-5" />

  const blockedReason = data?.pricingPublicBlockedReason ?? null
  const blockedMessage = mut.isError ? getPricingPublicBlockedMessage(mut.error) : null

  return (
    <Card className="p-6 mb-5">
      <h2 className="text-base font-semibold text-ink mb-1">Параметры номеров</h2>
      <p className="text-sm text-muted mb-4">
        Цена подключения мессенджера — это цена его опции (вкладка «Биллинг-аккаунты» и каталог опций); без цены и опубликованной оферты опция не продаётся.
      </p>
      <div className="grid sm:grid-cols-2 gap-4">
        <Input
          label="Срок простоя до отключения номера (дней)"
          type="number"
          min={0}
          max={60}
          value={idleDays}
          onChange={(e) => setIdleDays(e.target.value)}
        />
      </div>

      <div className="mt-5 pt-5 border-t border-line">
        <h3 className="text-sm font-semibold text-ink mb-1">Настройки пробного периода</h3>
        <p className="text-xs text-muted mb-4">
          Действуют только на новые выдачи пробного периода — уже выданные не меняются задним числом.
        </p>
        <div className="grid sm:grid-cols-3 gap-4">
          <div>
            <Input
              label="Длительность (дней)"
              type="number"
              min={1}
              max={365}
              value={trialDurationDays}
              aria-describedby={trialWindowExceedsDuration ? 'trial-window-error' : undefined}
              onChange={(e) => {
                setTrialDurationDays(e.target.value)
                setTrialDurationTouched(true)
              }}
            />
          </div>
          <div>
            <Input
              label="Окно рассылок (дней)"
              type="number"
              min={1}
              max={365}
              value={trialMailingWindowDays}
              aria-describedby={trialWindowExceedsDuration ? 'trial-window-error' : undefined}
              onChange={(e) => {
                setTrialMailingWindowDays(e.target.value)
                setTrialMailingWindowTouched(true)
              }}
            />
          </div>
          <div>
            <Input
              label="Пороги предупреждений (дней, через запятую)"
              value={trialThresholds}
              onChange={(e) => {
                setTrialThresholds(e.target.value)
                setTrialThresholdsTouched(true)
              }}
            />
          </div>
        </div>
        {trialWindowExceedsDuration && (
          <p id="trial-window-error" className="text-xs text-danger mt-2">
            Окно рассылок не может быть длиннее длительности пробного периода.
          </p>
        )}
        {/* §367: пороги обязаны совпадать с числами, буквально названными в текущей редакции текста
            условий активации — сервер отвечает 400 своим текстом, и он печатается дословно ниже. */}
      </div>

      <div className="mt-5 pt-5 border-t border-line flex items-start justify-between gap-4">
        <div>
          <label htmlFor="pricing-public-toggle" className="text-sm font-medium text-ink block mb-1">
            Публичные цены
          </label>
          <p className="text-xs text-muted max-w-md">
            Показывать каталог цен на публичной странице /pricing.
            {blockedReason && <span className="block mt-1 text-warning">{PRICING_BLOCKED_LABEL[blockedReason]}</span>}
          </p>
        </div>
        <button
          id="pricing-public-toggle"
          type="button"
          role="switch"
          aria-checked={pricingPublicEnabled}
          disabled={mut.isPending || (blockedReason !== null && !pricingPublicEnabled)}
          onClick={() => {
            const next = !pricingPublicEnabled
            setPricingPublicEnabled(next)
            mut.mutate(next)
          }}
          className={`relative shrink-0 w-11 h-6 rounded-full transition-colors disabled:opacity-40 disabled:cursor-not-allowed ${pricingPublicEnabled ? 'bg-gold-dark' : 'bg-cream-deep border border-line-strong'}`}
        >
          <span
            className={`absolute top-0.5 left-0.5 w-5 h-5 rounded-full bg-white shadow transition-transform ${pricingPublicEnabled ? 'translate-x-5' : 'translate-x-0'}`}
          />
        </button>
      </div>
      {blockedMessage && <p className="text-sm text-danger mt-3">{blockedMessage}</p>}

      {mut.isError && !blockedMessage && <p className="text-sm text-danger mt-3">{getNotificationErrorMessage(mut.error)}</p>}
      {mut.isSuccess && <p className="text-sm text-success mt-3">Сохранено</p>}
      <Button
        className="mt-4"
        loading={mut.isPending}
        onClick={() => mut.mutate(pricingPublicEnabled)}
      >
        Сохранить
      </Button>
    </Card>
  )
}

// ── Main tab ────────────────────────────────────────────────────────────────

export function NotificationsAdminTab() {
  return (
    <div>
      <SummaryRow />
      <MessengersCard />
      <PlatformSettingsCard />
      <ChannelsList />
    </div>
  )
}
