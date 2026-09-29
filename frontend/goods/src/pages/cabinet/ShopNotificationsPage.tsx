import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { ChannelRequestModal } from '@/components/notifications/ChannelRequestModal'
import { ChannelCard } from '@/pages/owner/NotificationsSection'
import { notificationChannelsApi } from '@/api/notificationChannels'
import { TRANSPORT_LABELS } from '@/utils/notificationTransport'
import { formatRub } from '@/utils/money'
import type { Company } from '@/types'
import { shopNotificationsApi } from '../../api/shopNotifications'
import { useShopContext } from '../../hooks/useShop'
import { RadioChips } from '../../components/pickup/RadioChips'
import { ErrorState, InlineError, LoadingList } from '../../components/StatePanels'
import { getGoodsErrorMessage } from '../../utils/orderError'
import type { NotificationDeliveryMode, NotificationTransport, ShopNotificationSettingsDto } from '../../types'

function Toggle({ label, hint, checked, disabled, onChange }: { label: string; hint?: string; checked: boolean; disabled?: boolean; onChange: (v: boolean) => void }) {
  return (
    <label className={`flex items-start justify-between gap-4 py-2 min-h-[44px] ${disabled ? 'opacity-60' : 'cursor-pointer'}`}>
      <span className="min-w-0">
        <span className="block text-sm font-medium text-ink">{label}</span>
        {hint && <span className="block text-xs text-muted mt-0.5">{hint}</span>}
      </span>
      <input type="checkbox" role="switch" className="mt-1 h-5 w-9 shrink-0 accent-[#2B2420]" checked={checked} disabled={disabled} onChange={(e) => onChange(e.target.checked)} />
    </label>
  )
}

/**
 * `/cabinet/:shopId/notifications` (owner) — what the shop sends: staff push, the buyer's browser push and messages in
 * MAX/WhatsApp (US-24-15…20). Whether messages are possible (`messengerAvailable`) and why not is the server's; numbers are
 * connected with the SAME components as on ezbook (request, risk, QR, assignment) through `api/notification-channels`.
 */
export function ShopNotificationsPage() {
  const { shop } = useShopContext()
  const qc = useQueryClient()
  const key = ['shop-notification-settings', shop.id]
  const q = useQuery({ queryKey: key, queryFn: () => shopNotificationsApi.get(shop.id) })
  const [draft, setDraft] = useState<ShopNotificationSettingsDto | null>(null)
  const [saved, setSaved] = useState(false)
  const [showRequest, setShowRequest] = useState(false)

  useEffect(() => {
    if (q.data) setDraft(q.data)
  }, [q.data])

  const save = useMutation({
    mutationFn: (d: ShopNotificationSettingsDto) =>
      shopNotificationsApi.put(shop.id, {
        staffPushEnabled: d.staffPushEnabled,
        customerWebPushEnabled: d.customerWebPushEnabled,
        customerMessengerEnabled: d.customerMessengerEnabled,
        deliveryMode: d.deliveryMode,
        priorityTransport: d.priorityTransport,
      }),
    onSuccess: (dto) => {
      qc.setQueryData(key, dto)
      setDraft(dto)
      setSaved(true)
    },
    onError: () => void qc.invalidateQueries({ queryKey: key }),
  })

  const channels = useQuery({ queryKey: ['notification-channels'], queryFn: notificationChannelsApi.list })
  const offer = useQuery({ queryKey: ['notification-channel-offer'], queryFn: notificationChannelsApi.offer })

  if (q.isLoading || !draft) {
    if (q.isError) return <main className="max-w-[860px] mx-auto px-4 sm:px-8 pt-8"><ErrorState message={getGoodsErrorMessage(q.error, 'Не удалось загрузить настройки уведомлений.')} onRetry={() => void q.refetch()} /></main>
    return <main className="max-w-[860px] mx-auto px-4 sm:px-8 pt-8"><LoadingList rows={3} rowClass="h-32" /></main>
  }

  const set = (patch: Partial<ShopNotificationSettingsDto>) => {
    setDraft({ ...draft, ...patch })
    setSaved(false)
    save.reset()
  }
  const serverDraftDiffers = JSON.stringify(draft) !== JSON.stringify(q.data)
  // The shop as a «company» for the shared assignment dialog (it only reads id and name).
  const shopAsCompany = [{ id: shop.id, name: shop.name }] as unknown as Company[]
  const ownChannels = channels.data ?? []
  const assignedElsewhere = (channelId: string, transport: string) => {
    const ids = new Set<string>()
    for (const ch of ownChannels) if (ch.id !== channelId && ch.transport === transport) ch.companies.forEach((c) => ids.add(c.companyId))
    return ids
  }
  const paidTransports = draft.channels.filter((c) => c.funded).map((c) => c.transport)

  return (
    <main className="max-w-[860px] mx-auto px-4 sm:px-8 pt-8 pb-12 flex flex-col gap-6">
      <section aria-labelledby="ntf-staff" className="rounded-2xl border border-line bg-white p-5 sm:p-6">
        <h2 id="ntf-staff" className="font-serif text-xl text-ink">
          Сотрудникам магазина
        </h2>
        <Toggle label="Push о новых заказах" hint="Сотрудник включает уведомления на своём устройстве в разделе «Устройства»." checked={draft.staffPushEnabled} onChange={(v) => set({ staffPushEnabled: v })} />
        {!draft.platformPushEnabled && <p className="text-xs text-muted">Уведомления на устройство пока не включены на платформе.</p>}
      </section>

      <section aria-labelledby="ntf-customers" className="rounded-2xl border border-line bg-white p-5 sm:p-6">
        <h2 id="ntf-customers" className="font-serif text-xl text-ink">
          Покупателям
        </h2>
        <Toggle
          label="Уведомления в браузере"
          hint="На странице заказа покупатель может включить уведомления о статусе."
          checked={draft.customerWebPushEnabled}
          onChange={(v) => set({ customerWebPushEnabled: v })}
        />
        {!draft.platformPushEnabled && <p className="text-xs text-muted">Уведомления в браузере пока не включены на платформе.</p>}
        <Toggle
          label="Сообщения в MAX/WhatsApp"
          hint={draft.messengerAvailable ? 'Получит только тот, кто отметил это при оформлении заказа.' : undefined}
          checked={draft.customerMessengerEnabled}
          disabled={!draft.messengerAvailable}
          onChange={(v) => set({ customerMessengerEnabled: v })}
        />
        {!draft.messengerAvailable && draft.messengerUnavailableText && (
          <p className="text-sm text-warning bg-warning-bg rounded-xl px-4 py-2.5" data-testid="messenger-unavailable">
            {draft.messengerUnavailableText}
          </p>
        )}

        {draft.messengerAvailable && draft.customerMessengerEnabled && (
          <div className="mt-3 flex flex-col gap-3">
            <div>
              <p className="text-[13px] font-medium text-[#4A4038] mb-2">Куда отправлять</p>
              <RadioChips<NotificationDeliveryMode>
                label="Режим доставки"
                value={draft.deliveryMode}
                onChange={(v) => set({ deliveryMode: v })}
                options={[
                  { value: 'PriorityChannel', label: 'В приоритетный мессенджер' },
                  { value: 'AllChannels', label: 'Во все подключённые' },
                ]}
              />
            </div>
            {draft.deliveryMode === 'PriorityChannel' && paidTransports.length > 1 && (
              <div>
                <p className="text-[13px] font-medium text-[#4A4038] mb-2">Приоритетный мессенджер</p>
                <RadioChips<NotificationTransport>
                  label="Приоритетный мессенджер"
                  value={draft.priorityTransport}
                  onChange={(v) => set({ priorityTransport: v })}
                  options={[...new Set(paidTransports)].map((t) => ({ value: t, label: TRANSPORT_LABELS[t] ?? t }))}
                />
              </div>
            )}
          </div>
        )}

        {save.isError && (
          <div className="mt-3">
            <InlineError>{getGoodsErrorMessage(save.error, 'Не удалось сохранить настройки уведомлений.')}</InlineError>
          </div>
        )}
        <div className="mt-4 flex items-center gap-3 flex-wrap">
          <Button loading={save.isPending} disabled={!serverDraftDiffers} onClick={() => save.mutate(draft)}>
            Сохранить
          </Button>
          {saved && !serverDraftDiffers && (
            <span role="status" className="text-sm text-success font-medium">
              Сохранено
            </span>
          )}
        </div>
      </section>

      <section aria-labelledby="ntf-channels" className="flex flex-col gap-3">
        <h2 id="ntf-channels" className="font-serif text-xl text-ink">
          Номера для сообщений
        </h2>
        {draft.channels.length > 0 && (
          <ul className="flex flex-col gap-1 text-sm text-ink-soft" aria-label="Номера этого магазина">
            {draft.channels.map((c) => (
              <li key={c.channelId} data-testid="shop-channel">
                <span className="font-medium text-ink">{c.phoneMasked ?? 'Номер не привязан'}</span> · {TRANSPORT_LABELS[c.transport] ?? c.transport} · {c.stateText} · {c.fundingText}
              </li>
            ))}
          </ul>
        )}
        {channels.isLoading ? (
          <LoadingList rows={1} rowClass="h-28" />
        ) : channels.isError ? (
          <ErrorState message={getGoodsErrorMessage(channels.error, 'Не удалось загрузить номера.')} onRetry={() => void channels.refetch()} />
        ) : (
          ownChannels.map((ch) => (
            <ChannelCard key={ch.id} channel={ch} myCompanies={shopAsCompany} assignedElsewhereIds={assignedElsewhere(ch.id, ch.transport)} riskVersion={offer.data?.riskVersion} />
          ))
        )}
        {offer.data && offer.data.pricePerMonth != null && offer.data.allowedByPlan ? (
          <Card className="p-5 flex items-center justify-between gap-4 flex-wrap">
            <p className="text-sm text-ink-soft">
              Сообщения уходят покупателям с вашего номера. {formatRub(offer.data.pricePerMonth)} / мес за номер; оплату включает администратор по заявке в разделе «Подписка».
            </p>
            <Button onClick={() => setShowRequest(true)}>Подключить номер</Button>
          </Card>
        ) : offer.data ? (
          <p className="text-sm text-muted">Подключение номера недоступно на вашем тарифе или временно закрыто.</p>
        ) : null}
        {showRequest && <ChannelRequestModal onClose={() => setShowRequest(false)} />}
      </section>
    </main>
  )
}
