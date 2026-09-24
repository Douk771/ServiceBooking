import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { notificationChannelsApi } from '../../api/notificationChannels'
import { companiesApi } from '../../api/companies'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Icon } from '../../components/ui/Icon'
import { ChannelBreachBanner } from '../../components/notifications/ChannelBreachBanner'
import { RiskAcceptanceModal } from '../../components/notifications/RiskAcceptanceModal'
import { QrModal } from '../../components/notifications/QrModal'
import { AssignCompanyDialog } from '../../components/notifications/AssignCompanyDialog'
import { ChannelRequestModal } from '../../components/notifications/ChannelRequestModal'
import { getNotificationErrorMessage } from '../../utils/notificationError'
import { TRANSPORT_LABELS } from '../../utils/notificationTransport'
import type { ChannelDto } from '../../types'

// ── Offer (before any channel is bought) ────────────────────────────────────────

function OfferCard({ hasExistingChannel }: { hasExistingChannel: boolean }) {
  const [showRequest, setShowRequest] = useState(false)
  const { data: offer, isLoading } = useQuery({ queryKey: ['notification-channel-offer'], queryFn: notificationChannelsApi.offer })

  if (isLoading) return <div className="h-40 bg-cream-deep rounded-2xl animate-pulse" />
  if (!offer) return null

  // API_CONTRACT_CYCLE9.md §114.1 — `available` was dropped from the DTO (it was always exactly
  // `pricePerMonth is not null` server-side); price not set yet → "temporarily unavailable", never a
  // zero price (US-57 п. 6).
  if (offer.pricePerMonth == null) {
    return (
      <Card className="p-8 text-center text-muted">
        <Icon name="alert-circle" size={28} strokeWidth={1.6} className="mx-auto mb-2" />
        <p>Подключение каналов временно недоступно</p>
      </Card>
    )
  }

  if (!offer.allowedByPlan) {
    return (
      <Card className="p-8 text-center text-muted">
        <Icon name="settings" size={28} strokeWidth={1.6} className="mx-auto mb-2" />
        <p>Уведомления клиентам через мессенджеры доступны на более высоком тарифе</p>
      </Card>
    )
  }

  // §104.9 — the messenger name is only fixed to "WhatsApp" while it's the only offered transport;
  // once MAX is offered too, the card can't claim a single messenger without lying about the other.
  const messengerNames = offer.transports.map((t) => t.displayName).join(' и ')

  return (
    <Card className="p-6">
      <h2 className="text-base font-semibold text-ink mb-2">
        {hasExistingChannel ? 'Подключить ещё один номер' : `Уведомления клиентам через ${messengerNames || 'мессенджер'}`}
      </h2>
      <p className="text-sm text-ink-soft mb-3">
        Сообщения о записи, напоминания, отмены и переносы уходят клиентам с вашего собственного номера через{' '}
        {messengerNames || 'мессенджер'}.
      </p>
      <ul className="text-sm text-ink-soft list-disc pl-5 flex flex-col gap-1 mb-4">
        <li>Подключение номера и безопасный режим отправки — пауза между сообщениями, чтобы не спровоцировать бан</li>
        <li>Автоматическое гашение канала при сбое и журнал доставки</li>
        <li>Ограничений на количество сообщений нет — фиксированная плата за канал в месяц</li>
      </ul>
      <p className="text-sm text-ink-soft mb-4">
        Доставка сообщений не гарантирована из-за ограничений доступа к WhatsApp в РФ; есть риск блокировки номера
        (платформа его снижает, но не устраняет). При блокировке деньги не возвращаются, но номер можно заменить в
        том же периоде.
      </p>
      {/* API_CONTRACT_CYCLE5.md §41.3, §56.5 п. 5, US-72 п. 1 — naming the delivery intermediary
          wherever the product itself explains the channel, not just in the legal texts. */}
      <p className="text-xs text-muted mb-4">
        Доставку сообщений технически обеспечивает привлекаемое лицо — ООО «ГРИН-АПИ» (ИНН 5047259512); ему
        передаются номер получателя, имя и текст сообщения.
      </p>
      <div className="flex items-center justify-between gap-4 flex-wrap">
        <p className="text-lg font-semibold text-gold-dark">{offer.pricePerMonth.toLocaleString('ru-RU')} ₽ / мес</p>
        <Button onClick={() => setShowRequest(true)}>Подключить канал</Button>
      </div>

      {showRequest && <ChannelRequestModal onClose={() => setShowRequest(false)} />}
    </Card>
  )
}

// ── Single channel card ──────────────────────────────────────────────────────────

function ChannelCard({
  channel,
  myCompanies,
  assignedElsewhereIds,
  riskVersion,
}: {
  channel: ChannelDto
  myCompanies: import('../../types').Company[]
  assignedElsewhereIds: Set<string>
  /** API_CONTRACT_CYCLE9.md §104.8/§114.1 — pinned from the live offer, not a hardcoded string: after
   *  B12 unifies the risk text into `ChannelRiskNotice`, a stale hardcoded version would record
   *  acceptance of a version the owner was never actually shown. */
  riskVersion: string | undefined
}) {
  const qc = useQueryClient()
  const [showRisk, setShowRisk] = useState(false)
  const [showQr, setShowQr] = useState(false)
  const [showAssign, setShowAssign] = useState(false)
  const [testResult, setTestResult] = useState('')
  const [testError, setTestError] = useState('')
  const [actionError, setActionError] = useState('')

  const invalidate = () => qc.invalidateQueries({ queryKey: ['notification-channels'] })

  const connectMut = useMutation({
    mutationFn: () => notificationChannelsApi.connect(channel.id),
    onSuccess: () => {
      setActionError('')
      setShowQr(true)
    },
    onError: (err: unknown) => setActionError(getNotificationErrorMessage(err)),
  })

  const disconnectMut = useMutation({
    mutationFn: () => notificationChannelsApi.disconnect(channel.id),
    onSuccess: invalidate,
    onError: (err: unknown) => setActionError(getNotificationErrorMessage(err)),
  })

  const replaceMut = useMutation({
    mutationFn: () => notificationChannelsApi.replace(channel.id),
    onSuccess: invalidate,
    onError: (err: unknown) => setActionError(getNotificationErrorMessage(err)),
  })

  const testMut = useMutation({
    mutationFn: () => notificationChannelsApi.testMessage(channel.id),
    onSuccess: (res) => {
      setTestError('')
      setTestResult(res.message)
    },
    onError: (err: unknown) => {
      setTestResult('')
      setTestError(getNotificationErrorMessage(err))
    },
  })

  const unassignMut = useMutation({
    mutationFn: (companyId: string) => notificationChannelsApi.unassignCompany(channel.id, companyId),
    onSuccess: invalidate,
    onError: (err: unknown) => setActionError(getNotificationErrorMessage(err)),
  })

  const candidateCompanies = myCompanies.filter(
    (c) => !channel.companies.some((cc) => cc.companyId === c.id) && !assignedElsewhereIds.has(c.id),
  )

  return (
    <Card className="p-5">
      <div className="flex flex-col gap-3">
        <ChannelBreachBanner
          state={channel.state}
          stateText={channel.stateText}
          idleSince={channel.idleSince}
          idleDeadline={channel.idleDeadline}
          onReplace={() => replaceMut.mutate()}
          replacing={replaceMut.isPending}
        />

        <div className="flex items-start justify-between gap-4 flex-wrap">
          <div>
            <div className="flex items-center gap-2 flex-wrap">
              <p className="font-semibold text-ink">{channel.phoneMasked ?? 'Номер ещё не привязан'}</p>
              {/* API_CONTRACT_CYCLE9.md §104.3 — a company can now hold one channel per transport, so
                  the transport must be visible on every card once there can be more than one. */}
              <span className="text-[11px] font-medium px-2 py-0.5 rounded-full bg-cream-deep text-ink-soft">
                {TRANSPORT_LABELS[channel.transport]}
              </span>
            </div>
            {channel.state !== 'Blocked' && channel.state !== 'NeedsReconnect' && channel.state !== 'Disconnected' && (
              <p className="text-sm text-ink-soft mt-0.5">{channel.stateText}</p>
            )}
            {/* Cycle 7: fundingText is server-composed (BillingTexts) — the frontend never builds its
                own copy about payment/funding state, per project convention. When fundingState is
                Unfunded the server's text names the reason, which number works instead, and both
                ways to fix it (buy another number or delete the extra one). */}
            <p className="text-xs text-muted mt-1">{channel.fundingText}</p>
            {/* §50.2 — visible to the owner (here) and to SuperAdmin only. */}
            {channel.inn && <p className="text-xs text-muted mt-0.5">ИНН {channel.inn}</p>}
          </div>

          <div className="flex gap-2 flex-wrap justify-end">
            {channel.paymentState === 'Paid' && channel.riskAcceptedAt == null && (
              <Button size="sm" disabled={!riskVersion} onClick={() => setShowRisk(true)}>
                Принять условия
              </Button>
            )}
            {channel.paymentState === 'Paid' && channel.riskAcceptedAt != null && channel.canConnect && (
              <Button size="sm" loading={connectMut.isPending} onClick={() => connectMut.mutate()}>
                {channel.state === 'NeedsReconnect' ? 'Подключить заново' : 'Подключить номер'}
              </Button>
            )}
            {channel.state === 'Connected' && (
              <Button size="sm" variant="secondary" loading={testMut.isPending} onClick={() => testMut.mutate()}>
                Проверить канал
              </Button>
            )}
            {channel.state !== 'DisabledByOwner' && channel.state !== 'Replaced' && channel.state !== 'Blocked' && (
              <Button size="sm" variant="danger" loading={disconnectMut.isPending} onClick={() => disconnectMut.mutate()}>
                Отвязать
              </Button>
            )}
          </div>
        </div>

        {testResult && <p className="text-xs text-success">{testResult}</p>}
        {testError && <p className="text-xs text-danger">{testError}</p>}
        {actionError && <p className="text-xs text-danger">{actionError}</p>}

        <div className="border-t border-line pt-3">
          <div className="flex items-center justify-between mb-2">
            <p className="text-xs font-medium text-muted uppercase tracking-wide">Назначенные компании</p>
            {channel.state !== 'Replaced' && (
              <Button size="sm" variant="ghost" onClick={() => setShowAssign(true)}>
                <Icon name="plus" size={13} strokeWidth={2} /> Назначить
              </Button>
            )}
          </div>
          {channel.companies.length === 0 ? (
            <p className="text-sm text-muted">Ни одна компания не назначена на этот канал</p>
          ) : (
            <div className="flex flex-wrap gap-2">
              {channel.companies.map((c) => (
                <span
                  key={c.companyId}
                  className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full bg-cream-deep text-sm text-ink-soft"
                >
                  {c.companyName}
                  {!c.isActive && <span className="text-xs text-muted">(неактивна)</span>}
                  <button
                    onClick={() => unassignMut.mutate(c.companyId)}
                    disabled={unassignMut.isPending}
                    aria-label={`Снять ${c.companyName} с канала`}
                    className="text-muted hover:text-danger"
                  >
                    <Icon name="x" size={12} strokeWidth={2} />
                  </button>
                </span>
              ))}
            </div>
          )}
        </div>
      </div>

      {showRisk && riskVersion && (
        <RiskAcceptanceModal
          channelId={channel.id}
          riskTextVersion={riskVersion}
          onClose={() => setShowRisk(false)}
          onAccepted={() => setShowRisk(false)}
        />
      )}
      {showQr && (
        <QrModal channelId={channel.id} onClose={() => setShowQr(false)} onConnected={() => setShowQr(false)} />
      )}
      {showAssign && (
        <AssignCompanyDialog
          channelId={channel.id}
          existingCompanyCount={channel.companies.length}
          candidateCompanies={candidateCompanies}
          onClose={() => setShowAssign(false)}
        />
      )}
    </Card>
  )
}

// ── Main ──────────────────────────────────────────────────────────────────────

export function NotificationsSection() {
  const { data: channels, isLoading, isError } = useQuery({
    queryKey: ['notification-channels'],
    queryFn: notificationChannelsApi.list,
  })
  const { data: myCompanies } = useQuery({ queryKey: ['my-companies'], queryFn: companiesApi.getMy })
  // Same query key as OfferCard/ChannelRequestModal — a cache hit once any of the three has loaded it.
  const { data: offer } = useQuery({ queryKey: ['notification-channel-offer'], queryFn: notificationChannelsApi.offer })

  if (isLoading) {
    return (
      <div className="flex flex-col gap-3">
        {Array.from({ length: 2 }).map((_, i) => (
          <div key={i} className="h-32 bg-cream-deep rounded-2xl animate-pulse" />
        ))}
      </div>
    )
  }

  if (isError) {
    return (
      <Card className="p-10 text-center text-muted">
        <Icon name="alert-circle" size={28} strokeWidth={1.6} className="mx-auto mb-2" />
        <p>Не удалось загрузить каналы уведомлений. Обновите страницу.</p>
      </Card>
    )
  }

  const list = channels ?? []
  const companies = myCompanies ?? []
  // API_CONTRACT_CYCLE9.md §104.3 — a company can now be assigned to at most one channel PER
  // TRANSPORT, not one channel overall (cycle 4's original invariant). Only channels of the SAME
  // transport as the one being assigned-to exclude a company from the candidate list — a company
  // already on a WhatsApp channel is still a valid candidate for a MAX channel.
  const assignedElsewhereByChannel = (currentChannelId: string, transport: ChannelDto['transport']) => {
    const ids = new Set<string>()
    for (const ch of list) {
      if (ch.id === currentChannelId || ch.transport !== transport) continue
      for (const c of ch.companies) ids.add(c.companyId)
    }
    return ids
  }

  return (
    <div className="flex flex-col gap-4">
      {list.map((ch) => (
        <ChannelCard
          key={ch.id}
          channel={ch}
          myCompanies={companies}
          assignedElsewhereIds={assignedElsewhereByChannel(ch.id, ch.transport)}
          riskVersion={offer?.riskVersion}
        />
      ))}
      <OfferCard hasExistingChannel={list.length > 0} />
    </div>
  )
}
