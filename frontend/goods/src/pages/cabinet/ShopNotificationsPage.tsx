import { useEffect, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { NumbersBlock } from '@/components/notifications/NumbersBlock'
import { TRANSPORT_LABELS } from '@/utils/notificationTransport'
import { shopNotificationsApi } from '../../api/shopNotifications'
import { useShopContext } from '../../hooks/useShop'
import { RadioChips } from '../../components/pickup/RadioChips'
import { ErrorState, InlineError, LoadingList } from '../../components/StatePanels'
import { getGoodsErrorMessage } from '../../utils/orderError'
import type { NotificationDeliveryMode, NotificationTransport, ShopNotificationSettingsDto } from '../../types'

/** US-33-06 — devices live in the profile now (ARCHITECTURE_CYCLE33.md §33.10.1). */
function ProfileDevicesLink() {
  return (
    <Link to="/profile#devices" className="underline">
      профиле
    </Link>
  )
}

function Toggle({ label, hint, checked, disabled, onChange }: { label: string; hint?: ReactNode; checked: boolean; disabled?: boolean; onChange: (v: boolean) => void }) {
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
 * MAX/WhatsApp (US-24-15…20). Whether messages are possible (`messengerAvailable`) and why not is the server's; the numbers are the account's, in the
 * SAME «Номера» block as on ezbook (cycle 40: payment request → terms → QR; no assignment of companies).
 */
export function ShopNotificationsPage() {
  const { shop } = useShopContext()
  const qc = useQueryClient()
  const key = ['shop-notification-settings', shop.id]
  const q = useQuery({ queryKey: key, queryFn: () => shopNotificationsApi.get(shop.id) })
  const [draft, setDraft] = useState<ShopNotificationSettingsDto | null>(null)
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    if (q.data) setDraft(q.data)
  }, [q.data])

  const save = useMutation({
    mutationFn: (d: ShopNotificationSettingsDto) =>
      shopNotificationsApi.put(shop.id, {
        staffPushEnabled: d.staffPushEnabled,
        customerWebPushEnabled: d.customerWebPushEnabled,
        customerMessengerEnabled: d.customerMessengerEnabled,
        staffMaxEnabled: d.staffMaxEnabled,
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
  // The messengers the customer can really be written through: bound and paid numbers (the server decides whether the choice is worth showing).
  const workingTransports = [...new Set(draft.channels.filter((c) => c.funded && c.isConnected).map((c) => c.transport))]
  const priorityOptions: NotificationTransport[] = [
    ...workingTransports,
    ...(workingTransports.includes(draft.priorityTransport) ? [] : [draft.priorityTransport]),
  ]

  return (
    <main className="max-w-[860px] mx-auto px-4 sm:px-8 pt-8 pb-12 flex flex-col gap-6">
      <section aria-labelledby="ntf-staff" className="rounded-2xl border border-line bg-white p-5 sm:p-6">
        <h2 id="ntf-staff" className="font-serif text-xl text-ink">
          Сотрудникам магазина
        </h2>
        <Toggle label="Push о новых заказах" hint={<>Сотрудник включает уведомления на своём устройстве в <ProfileDevicesLink />, раздел «Устройства и уведомления».</>} checked={draft.staffPushEnabled} onChange={(v) => set({ staffPushEnabled: v })} />
        {!draft.platformPushEnabled && <p className="text-xs text-muted">Уведомления на устройство пока не включены на платформе.</p>}
        <Toggle
          label="Сообщения сотрудникам в MAX"
          hint={
            <>
              Новые заказы и отмены покупателями приходят в MAX тем сотрудникам, кто подключил его в <ProfileDevicesLink />, раздел «Устройства и уведомления». Кнопка «Сохранить» — ниже, в блоке «Покупателям».
            </>
          }
          checked={draft.staffMaxEnabled}
          onChange={(v) => set({ staffMaxEnabled: v })}
        />
        {!draft.staffMaxAvailable && (
          <p className="text-sm text-ink-soft bg-cream-deep rounded-xl px-4 py-2.5" data-testid="staff-max-unavailable">
            {draft.staffMaxUnavailableText ?? 'Сообщения в MAX пока не включены на платформе'}. Настройку можно сохранить заранее — она начнёт действовать после включения.
          </p>
        )}
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

        {draft.messengerAvailable && draft.customerMessengerEnabled && draft.deliveryChoiceVisible && (
          <div className="mt-3 flex flex-col gap-3">
            {draft.priorityWarning && (
              <p className="text-sm text-warning bg-warning-bg rounded-xl px-4 py-2.5" data-testid="priority-warning">
                {draft.priorityWarning}
              </p>
            )}
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
            {draft.deliveryMode === 'PriorityChannel' && (
              <div>
                <p className="text-[13px] font-medium text-[#4A4038] mb-2">Приоритетный мессенджер</p>
                <RadioChips<NotificationTransport>
                  label="Приоритетный мессенджер"
                  value={draft.priorityTransport}
                  onChange={(v) => set({ priorityTransport: v })}
                  options={priorityOptions.map((t) => ({
                    value: t,
                    label: `${TRANSPORT_LABELS[t] ?? t}${workingTransports.includes(t) ? '' : ' (не работает)'}`,
                  }))}
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

      <NumbersBlock />
    </main>
  )
}
