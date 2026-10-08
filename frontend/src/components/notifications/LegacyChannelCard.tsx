// LEGACY (цикл 40): карточка канала старой модели «назначенные компании». Остаётся только для goods/dom до FE-40-3
// (их ShopNotificationsPage/NotificationsPage импортируют ChannelCard); в кабинете ezbook её заменил NumbersBlock.
import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { notificationChannelsApi } from '../../api/notificationChannels'
import { Card } from '../ui/Card'
import { Button } from '../ui/Button'
import { Icon } from '../ui/Icon'
import { ChannelBreachBanner } from './ChannelBreachBanner'
import { RiskAcceptanceModal } from './RiskAcceptanceModal'
import { QrModal } from './QrModal'
import { AssignCompanyDialog } from './AssignCompanyDialog'
import { getNotificationErrorMessage } from '../../utils/notificationError'
import { TRANSPORT_LABELS } from '../../utils/notificationTransport'
import type { ChannelDto } from '../../types'

// ── Single channel card ──────────────────────────────────────────────────────────

export function ChannelCard({
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
