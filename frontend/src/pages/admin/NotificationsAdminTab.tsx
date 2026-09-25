import { useEffect, useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { adminNotificationsApi } from '../../api/platformSettings'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { Icon } from '../../components/ui/Icon'
import { Pagination } from '../../components/ui/Pagination'
import { getNotificationErrorMessage } from '../../utils/notificationError'
import { TRANSPORT_FILTER_OPTIONS, TRANSPORT_LABELS } from '../../utils/notificationTransport'
import { getPricingPublicBlockedMessage } from '../../utils/legalError'
import type { NotificationTransport, PricingPublicBlockedReason } from '../../types'

function fmt(d: string | null) {
  return d ? format(parseISO(d), 'd MMM yyyy', { locale: ru }) : '—'
}

// ── Summary tile row ─────────────────────────────────────────────────────────

function SummaryRow() {
  const { data } = useQuery({ queryKey: ['admin-channel-summary'], queryFn: adminNotificationsApi.summary })
  if (!data) return <div className="h-20 bg-cream-deep rounded-2xl animate-pulse mb-4" />

  const tiles = [
    { label: 'Подключено', value: data.connected },
    { label: 'Подключается', value: data.connecting },
    { label: 'Отвалилось', value: data.disconnected },
    { label: 'Заблокировано', value: data.blocked },
    { label: 'Требует переподключения', value: data.needsReconnect },
    { label: 'Простаивает', value: data.idle },
    { label: 'Истекает за 7 дней', value: data.expiringIn7Days },
    { label: 'Заявок', value: data.pendingRequests },
  ]

  return (
    <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-5">
      {tiles.map((t) => (
        <Card key={t.label} className="p-4 text-center">
          <p className="text-xl font-bold text-ink">{t.value}</p>
          <p className="text-[11px] text-muted mt-0.5">{t.label}</p>
        </Card>
      ))}
    </div>
  )
}

// ── Channels list ─────────────────────────────────────────────────────────────
// Оплата номера больше не отмечается здесь — единица оплаты переехала в подписку биллинг-аккаунта
// (POST /admin/notification-channels/{id}/payment отвечает 410, API_CONTRACT_CYCLE7.md §53).
// Замена — вкладка «Биллинг-аккаунты» → карточка аккаунта → «Назначить подписку», опция
// notifications.whatsapp с количеством.

const STATE_LABEL_RU: Record<string, string> = {
  NotConnected: 'не подключён',
  Connecting: 'подключается',
  Connected: 'подключён',
  Disconnected: 'отвалился',
  Blocked: 'заблокирован',
  DisabledByOwner: 'отключён владельцем',
  NeedsReconnect: 'требует переподключения',
  Replaced: 'заменён',
}

function ChannelsList() {
  const [page, setPage] = useState(1)
  const [transport, setTransport] = useState<NotificationTransport | ''>('')
  const [actionError, setActionError] = useState('')
  const qc = useQueryClient()

  const { data, isLoading } = useQuery({
    queryKey: ['admin-channels', page, transport],
    queryFn: () => adminNotificationsApi.listChannels({ page, pageSize: 20, transport: transport || undefined }),
  })

  const suspendMut = useMutation({
    mutationFn: (id: string) => adminNotificationsApi.suspend(id),
    onSuccess: () => {
      setActionError('')
      qc.invalidateQueries({ queryKey: ['admin-channels'] })
    },
    onError: (err: unknown) => setActionError(getNotificationErrorMessage(err)),
  })
  const resumeMut = useMutation({
    mutationFn: (id: string) => adminNotificationsApi.resume(id),
    onSuccess: () => {
      setActionError('')
      qc.invalidateQueries({ queryKey: ['admin-channels'] })
    },
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

  return (
    <div>
      {actionError && <p className="text-sm text-danger mb-3">{actionError}</p>}

      {/* API_CONTRACT_CYCLE9.md §114.3 — new ?transport= filter on the admin channel list. */}
      <div className="mb-3">
        <select
          aria-label="Канал"
          value={transport}
          onChange={(e) => {
            setTransport(e.target.value as NotificationTransport | '')
            setPage(1)
          }}
          className="rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold bg-white text-ink"
        >
          {TRANSPORT_FILTER_OPTIONS.map((f) => (
            <option key={f.value} value={f.value}>
              {f.label}
            </option>
          ))}
        </select>
      </div>

      <div className="grid gap-3">
        {(data?.items ?? []).map((c) => (
          <Card key={c.id} className="p-4 flex items-center justify-between gap-4 flex-wrap">
            <div>
              <div className="flex items-center gap-2 flex-wrap">
                <span className="font-medium text-ink">{c.ownerName}</span>
                <span className="text-xs text-muted">{c.ownerPhoneMasked}</span>
                <span className="text-[11px] font-medium px-2 py-0.5 rounded-full bg-cream-deep text-ink-soft">
                  {TRANSPORT_LABELS[c.transport]}
                </span>
                <span className="text-xs px-2 py-0.5 rounded-full bg-cream-deep text-ink-soft">
                  {STATE_LABEL_RU[c.state] ?? c.state}
                </span>
                <span
                  className={`text-xs px-2 py-0.5 rounded-full font-medium ${
                    c.paymentState === 'Paid' ? 'bg-success-bg text-success' : 'bg-warning-bg text-warning'
                  }`}
                >
                  {c.paymentState === 'Paid' ? 'оплачен' : c.paymentState === 'Suspended' ? 'приостановлен' : 'не оплачен'}
                </span>
              </div>
              <p className="text-xs text-muted mt-0.5">
                {c.companyCount} компаний · оплачен до {fmt(c.paidUntil)}
                {c.requestedAt && <> · заявка от {fmt(c.requestedAt)}</>}
                {c.idleSince && <> · простой с {fmt(c.idleSince)}</>}
                {/* §50.2 — INN is visible only to the owner and to SuperAdmin. */}
                {c.inn && <> · ИНН {c.inn}</>}
              </p>
            </div>
            <div className="flex gap-2 shrink-0">
              {c.paymentState === 'Paid' ? (
                <Button size="sm" variant="danger" loading={suspendMut.isPending} onClick={() => suspendMut.mutate(c.id)}>
                  Приостановить
                </Button>
              ) : c.paymentState === 'Suspended' ? (
                <Button size="sm" variant="secondary" loading={resumeMut.isPending} onClick={() => resumeMut.mutate(c.id)}>
                  Возобновить
                </Button>
              ) : null}
            </div>
          </Card>
        ))}
        {data?.items.length === 0 && (
          <Card className="p-10 text-center text-muted">
            <Icon name="megaphone" size={28} strokeWidth={1.6} className="mx-auto mb-2" />
            <p>Каналов пока нет</p>
          </Card>
        )}
      </div>
      {data && (
        <Pagination page={data.page} pageSize={data.pageSize} total={data.total} hasNext={data.hasNext} onPageChange={setPage} />
      )}
    </div>
  )
}

// ── Platform settings (price, idle days) ──────────────────────────────────────

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
  const [price, setPrice] = useState('')
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

  useEffect(() => {
    if (!data) return
    setPrice(data.channelPricePerMonth != null ? String(data.channelPricePerMonth) : '')
    setIdleDays(String(data.channelIdleDays))
    setPricingPublicEnabled(data.pricingPublicEnabled)
    if (data.trialDurationDays != null) setTrialDurationDays(String(data.trialDurationDays))
    if (data.trialMailingWindowDays != null) setTrialMailingWindowDays(String(data.trialMailingWindowDays))
    if (data.trialWarningThresholdsDays != null) setTrialThresholds(data.trialWarningThresholdsDays.join(','))
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
      const parsedPrice = price.trim() === '' ? null : Number(price)
      const parsedIdleDays = Number(idleDays)
      if (parsedPrice !== null && Number.isNaN(parsedPrice)) {
        throw new Error('Некорректная цена опции «канал» — исправьте поле перед сохранением.')
      }
      if (Number.isNaN(parsedIdleDays)) {
        throw new Error('Некорректный срок простоя — исправьте поле перед сохранением.')
      }
      if (Number.isNaN(parsedTrialDuration) || parsedTrialDuration < 1) {
        throw new Error('Некорректная длительность пробного периода — исправьте поле перед сохранением.')
      }
      if (Number.isNaN(parsedTrialMailingWindow) || parsedTrialMailingWindow < 1) {
        throw new Error('Некорректное окно рассылок — исправьте поле перед сохранением.')
      }
      if (trialWindowExceedsDuration) {
        throw new Error('Окно рассылок не может быть длиннее длительности пробного периода.')
      }
      const thresholds = trialThresholds
        .split(',')
        .map((s) => s.trim())
        .filter((s) => s !== '')
        .map(Number)
      if (thresholds.length === 0 || thresholds.some((n) => Number.isNaN(n) || n < 1)) {
        throw new Error('Некорректные пороги предупреждений — перечислите положительные числа через запятую.')
      }
      return adminNotificationsApi.updateSettings({
        channelPricePerMonth: parsedPrice,
        channelIdleDays: parsedIdleDays,
        pricingPublicEnabled: nextPricingPublicEnabled,
        pricingPublicBlockedReason: data?.pricingPublicBlockedReason ?? null,
        trialDurationDays: parsedTrialDuration,
        trialMailingWindowDays: parsedTrialMailingWindow,
        trialWarningThresholdsDays: thresholds,
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
      <h2 className="text-base font-semibold text-ink mb-1">Параметры опции «канал»</h2>
      <p className="text-sm text-muted mb-4">
        Пока цена не задана, подключение канала не предлагается владельцам (не показывается с нулём).
      </p>
      <div className="grid sm:grid-cols-2 gap-4">
        <Input
          label="Цена опции «канал» в месяц (₽)"
          type="number"
          min={0}
          placeholder="не задана"
          value={price}
          onChange={(e) => setPrice(e.target.value)}
        />
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
              onChange={(e) => setTrialDurationDays(e.target.value)}
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
              onChange={(e) => setTrialMailingWindowDays(e.target.value)}
            />
          </div>
          <div>
            <Input
              label="Пороги предупреждений (дней, через запятую)"
              value={trialThresholds}
              onChange={(e) => setTrialThresholds(e.target.value)}
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
      <PlatformSettingsCard />
      <ChannelsList />
    </div>
  )
}
