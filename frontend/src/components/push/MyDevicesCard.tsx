import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { useWebPush } from '../../hooks/useWebPush'
import { Card } from '../ui/Card'
import { Icon } from '../ui/Icon'
import { PushUnavailableNotice } from './PushUnavailableNotice'

/**
 * ARCHITECTURE_CYCLE9.md §105.5/§105.10, API_CONTRACT_CYCLE9.md §115.2/§115.4 (US-116, US-117,
 * US-123, US-124) — a switch for THIS device plus the full device list, any of which can be turned
 * off from here (e.g. "forgot to log out on the salon's shared computer").
 */
export function MyDevicesCard() {
  const push = useWebPush()

  if (push.reason) {
    return (
      <Card className="p-6">
        <h2 className="text-lg font-semibold text-ink mb-3">Уведомления о новых записях</h2>
        <PushUnavailableNotice reason={push.reason} />
      </Card>
    )
  }

  if (push.isLoading) {
    return <div className="h-32 bg-cream-deep rounded-2xl animate-pulse" />
  }

  const onToggle = () => {
    if (push.isSubscribedOnThisDevice) void push.disableOnThisDevice()
    else void push.enableOnThisDevice()
  }

  const busy = push.isEnabling || push.isDisabling

  return (
    <Card className="p-6">
      <h2 className="text-lg font-semibold text-ink mb-1">Уведомления о новых записях</h2>
      <p className="text-sm text-muted mb-4">Push-уведомление придёт в браузер, когда клиент запишется к вам.</p>

      <label className="flex items-center justify-between gap-3 py-1">
        <span className="text-sm text-ink-soft">Уведомлять меня о новых записях на этом устройстве</span>
        <button
          type="button"
          role="switch"
          aria-checked={push.isSubscribedOnThisDevice}
          disabled={busy}
          onClick={onToggle}
          className={`relative w-11 h-6 rounded-full transition-colors shrink-0 disabled:opacity-50 ${
            push.isSubscribedOnThisDevice ? 'bg-ink' : 'bg-line-strong'
          }`}
        >
          <span
            className={`absolute top-0.5 left-0.5 w-5 h-5 rounded-full bg-white transition-transform ${
              push.isSubscribedOnThisDevice ? 'translate-x-5' : ''
            }`}
          />
        </button>
      </label>

      {push.actionError && <p className="text-sm text-danger mt-2">{push.actionError}</p>}

      {push.devices.length > 0 && (
        <div className="mt-5 pt-4 border-t border-line flex flex-col gap-2.5">
          <p className="text-[13px] font-medium text-[#4A4038]">Устройства с включёнными уведомлениями</p>
          {push.devices.map((d) => (
            <div key={d.id} className="flex items-center justify-between gap-3 py-1">
              <div className="min-w-0">
                <p className="text-sm text-ink-soft truncate">
                  {d.deviceLabel}
                  {d.isCurrent && <span className="text-muted"> · это устройство</span>}
                </p>
                <p className="text-xs text-muted">
                  {d.lastSuccessAtUtc
                    ? `Последняя доставка: ${format(parseISO(d.lastSuccessAtUtc), 'd MMM, HH:mm', { locale: ru })}`
                    : `Подписано: ${format(parseISO(d.createdAtUtc), 'd MMM, HH:mm', { locale: ru })}`}
                </p>
              </div>
              <button
                type="button"
                disabled={busy}
                onClick={() => void push.disableDevice(d.id)}
                className="shrink-0 text-muted hover:text-danger disabled:opacity-50 p-1.5"
                aria-label={`Отключить уведомления на устройстве «${d.deviceLabel}»`}
              >
                <Icon name="trash" size={15} strokeWidth={1.8} />
              </button>
            </div>
          ))}
        </div>
      )}
    </Card>
  )
}
