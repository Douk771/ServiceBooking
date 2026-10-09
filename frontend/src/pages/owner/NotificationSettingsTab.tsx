import { useEffect, useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { notificationsApi } from '../../api/notifications'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Icon } from '../../components/ui/Icon'
import { NumbersBlock } from '../../components/notifications/NumbersBlock'
import { StaffPushSettingsCard } from '../../components/push/StaffPushSettingsCard'
import { getNotificationErrorMessage } from '../../utils/notificationError'
import { TRANSPORT_LABELS, NOTIFICATION_TYPE_LABELS } from '../../utils/notificationTransport'
import type { NotificationType, NotificationDeliveryMode, NotificationTransport } from '../../types'

const CLIENT_TYPES: NotificationType[] = ['BookingConfirmed', 'Reminder', 'BookingCancelled', 'BookingRescheduled']

/**
 * Cycle 40 (US-40-06): ONE section «Уведомления клиентам» — the account's numbers, the delivery choice (only with two working messengers), the types and the
 * reminder terms. No tariff or channel stubs: the settings can be saved before a number is paid, and nothing here depends on the tariff.
 */
export function NotificationSettingsTab({ companyId }: { companyId: string }) {
  const qc = useQueryClient()
  const { data, isLoading, isError } = useQuery({
    queryKey: ['notification-settings', companyId],
    queryFn: () => notificationsApi.getSettings(companyId),
  })

  const [enabledTypes, setEnabledTypes] = useState<Set<NotificationType>>(new Set(CLIENT_TYPES))
  const [reminderLeadMinutes, setReminderLeadMinutes] = useState(1440)
  const [minLeadMinutes, setMinLeadMinutes] = useState(120)
  const [deliveryMode, setDeliveryMode] = useState<NotificationDeliveryMode | null>(null)
  const [priorityTransport, setPriorityTransport] = useState<NotificationTransport | null>(null)
  const [validationError, setValidationError] = useState('')

  useEffect(() => {
    if (!data) return
    setEnabledTypes(new Set(data.enabledTypes))
    setReminderLeadMinutes(data.reminderLeadMinutes)
    setMinLeadMinutes(data.minLeadMinutes)
    // §114.4 — deliveryMode/priorityTransport stay `null` (i.e. untouched) until the owner actually
    // interacts with the picker below; the PUT then omits them entirely rather than re-sending the
    // server's own last value back at it. That matters when the priority transport has since
    // disconnected: re-sending it unchanged would still trip the server's "priorityTransport must be
    // among connectedTransports" 400 for an owner who only meant to change, say, reminderLeadMinutes.
    setDeliveryMode(null)
    setPriorityTransport(null)
  }, [data])

  const effectiveDeliveryMode = deliveryMode ?? data?.deliveryMode ?? 'PriorityChannel'
  const effectivePriorityTransport = priorityTransport ?? data?.priorityTransport ?? 'WhatsApp'

  const saveMut = useMutation({
    mutationFn: () =>
      notificationsApi.updateSettings(companyId, {
        enabledTypes: [...enabledTypes],
        reminderLeadMinutes,
        minLeadMinutes,
        // Omit entirely when the owner hasn't touched the picker (§114.4: "не прислали — не меняем").
        ...(deliveryMode !== null ? { deliveryMode } : {}),
        ...(priorityTransport !== null ? { priorityTransport } : {}),
      }),
    onSuccess: (res) => {
      setValidationError('')
      qc.setQueryData(['notification-settings', companyId], res)
    },
  })

  const toggleType = (t: NotificationType) =>
    setEnabledTypes((prev) => {
      const next = new Set(prev)
      if (next.has(t)) next.delete(t)
      else next.add(t)
      return next
    })

  const onSave = () => {
    // Mirrors the server's 400 (§28.2) client-side so the owner doesn't wait for a round trip to find
    // out the combination can never fire.
    if (minLeadMinutes >= reminderLeadMinutes) {
      setValidationError('Напоминание за 1 час при пороге 2 часа не уйдёт никогда')
      return
    }
    setValidationError('')
    saveMut.mutate()
  }

  const staffPushCard = <StaffPushSettingsCard companyId={companyId} />

  if (isLoading)
    return (
      <div className="flex flex-col gap-4">
        <NumbersBlock />
        <div className="h-64 bg-cream-deep rounded-2xl animate-pulse" />
        {staffPushCard}
      </div>
    )
  if (isError || !data)
    return (
      <div className="flex flex-col gap-4">
        <NumbersBlock />
        <Card className="p-10 text-center text-muted">
          <Icon name="alert-circle" size={28} strokeWidth={1.6} className="mx-auto mb-2" />
          <p>Не удалось загрузить настройки уведомлений.</p>
        </Card>
        {staffPushCard}
      </div>
    )

  // «Как доставлять» is worth showing only with TWO working messengers (or a saved priority that stopped working) — the server decides (deliveryChoiceVisible).
  const working = data.workingTransports ?? data.connectedTransports
  const priorityOptions: NotificationTransport[] = [
    ...working,
    ...(working.includes(effectivePriorityTransport) ? [] : [effectivePriorityTransport]),
  ]

  return (
    <div className="flex flex-col gap-4">
      <NumbersBlock />

      {data.deliveryChoiceVisible && (
        <Card className="p-6">
          <h2 className="text-lg font-semibold text-ink mb-1">Как доставлять клиенту</h2>
          {data.priorityWarning && (
            <div className="rounded-xl bg-warning-bg text-warning text-sm px-4 py-3 flex items-start gap-2 mt-3 mb-1">
              <Icon name="alert-circle" size={15} strokeWidth={1.8} className="shrink-0 mt-0.5" />
              <span>{data.priorityWarning}</span>
            </div>
          )}

          <div className="flex flex-col gap-2.5 mt-3">
            <label className="flex items-start gap-2.5 cursor-pointer">
              <input
                type="radio"
                name="delivery-mode"
                className="w-4 h-4 mt-0.5 accent-gold"
                checked={effectiveDeliveryMode === 'PriorityChannel'}
                onChange={() => setDeliveryMode('PriorityChannel')}
              />
              <span className="text-sm text-ink-soft">
                Только в приоритетный мессенджер
                {effectiveDeliveryMode === 'PriorityChannel' && (
                  <select
                    aria-label="Приоритетный канал"
                    value={effectivePriorityTransport}
                    onChange={(e) => setPriorityTransport(e.target.value as NotificationTransport)}
                    className="ml-2.5 rounded-lg border border-line px-2.5 py-1 text-sm outline-none focus:border-gold bg-white text-ink"
                  >
                    {priorityOptions.map((t) => (
                      <option key={t} value={t}>
                        {TRANSPORT_LABELS[t] ?? t}
                        {!working.includes(t) ? ' (не работает)' : ''}
                      </option>
                    ))}
                  </select>
                )}
              </span>
            </label>

            <label className="flex items-start gap-2.5 cursor-pointer">
              <input
                type="radio"
                name="delivery-mode"
                className="w-4 h-4 mt-0.5 accent-gold"
                checked={effectiveDeliveryMode === 'AllChannels'}
                onChange={() => setDeliveryMode('AllChannels')}
              />
              <span className="text-sm text-ink-soft">Во все подключённые мессенджеры</span>
            </label>
          </div>

          {effectiveDeliveryMode === 'AllChannels' && (
            <p className="text-xs text-muted mt-2.5">Клиент получит два одинаковых сообщения на один номер — по одному в каждый мессенджер.</p>
          )}
          <p className="text-xs text-muted mt-2.5">Изменение действует на события, произошедшие после сохранения.</p>
        </Card>
      )}

      <Card className="p-6">
        <h2 className="text-lg font-semibold text-ink mb-4">Какие сообщения отправлять</h2>
        {!data.messagingActive && data.inactiveText && (
          <div className="rounded-xl bg-cream-deep text-ink-soft text-sm px-4 py-3 flex items-start gap-2 mb-4">
            <Icon name="alert-circle" size={15} strokeWidth={1.8} className="shrink-0 mt-0.5" />
            <span>{data.inactiveText}. Настройки можно сохранить заранее — сообщения пойдут, как только номер заработает.</span>
          </div>
        )}
        <div className="grid gap-2 mb-6">
          {CLIENT_TYPES.map((t) => (
            <label key={t} className="flex items-center justify-between gap-3 py-1.5 cursor-pointer">
              <span className="text-sm text-ink-soft">{NOTIFICATION_TYPE_LABELS[t]}</span>
              <input
                type="checkbox"
                className="w-4 h-4 accent-gold rounded"
                checked={enabledTypes.has(t)}
                onChange={() => toggleType(t)}
              />
            </label>
          ))}
        </div>

        <div className="grid sm:grid-cols-2 gap-4 mb-2">
          <div className="flex flex-col gap-1.5">
            <label className="text-[13px] font-medium text-[#4A4038]">За сколько часов напоминать</label>
            <input
              type="number"
              min={1}
              max={72}
              value={Math.round(reminderLeadMinutes / 60)}
              onChange={(e) => setReminderLeadMinutes(Math.max(1, Math.min(72, Number(e.target.value))) * 60)}
              className="rounded-xl border border-line px-4 py-3 text-sm outline-none focus:border-gold bg-white text-ink"
            />
            <p className="text-xs text-muted">1–72 часа, по умолчанию 24</p>
          </div>
          <div className="flex flex-col gap-1.5">
            <label className="text-[13px] font-medium text-[#4A4038]">Не слать, если до визита осталось меньше</label>
            <input
              type="number"
              min={0}
              max={12}
              value={Math.round(minLeadMinutes / 60)}
              onChange={(e) => setMinLeadMinutes(Math.max(0, Math.min(12, Number(e.target.value))) * 60)}
              className="rounded-xl border border-line px-4 py-3 text-sm outline-none focus:border-gold bg-white text-ink"
            />
            <p className="text-xs text-muted">0–12 часов, по умолчанию 2 — страховка от выброса очереди после починки канала</p>
          </div>
        </div>

        {(validationError || saveMut.isError) && (
          <p className="text-sm text-danger mt-2">
            {validationError || getNotificationErrorMessage(saveMut.error)}
          </p>
        )}
        {saveMut.isSuccess && !validationError && (
          <p className="text-sm text-success mt-2 flex items-center gap-1.5">
            <Icon name="check" size={14} strokeWidth={2} /> Сохранено
          </p>
        )}

        <Button className="mt-4" loading={saveMut.isPending} onClick={onSave}>
          Сохранить
        </Button>
      </Card>

      {staffPushCard}
    </div>
  )
}
