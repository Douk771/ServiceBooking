import { useEffect, useRef, type ReactNode } from 'react'
import { useLocation } from 'react-router-dom'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import type { PushSite } from '../../api/push'
import { describeDevice, useWebPush } from '../../hooks/useWebPush'
import {
  ONE_DEVICE_ENOUGH_TEXT,
  deviceSiteLabel,
  duplicateHint,
  likelySameBrowserOnOtherSite,
  staffPushIntro,
  staffPushSwitchLabel,
  type PushAppName,
} from '../../utils/staffPushTexts'
import { Card } from '../ui/Card'
import { Icon } from '../ui/Icon'
import { PushUnavailableNotice } from './PushUnavailableNotice'

export interface DevicesAndNotificationsSectionProps {
  site: PushSite
  /** `short_name` of the site's manifest — for the iPhone texts. */
  appName: PushAppName
  /** goods: the browser subscription is shared with the buyer role, «disable» removes the server row only. */
  keepBrowserSubscription?: boolean
  /** goods: rendered below the block only when the user has a shop. */
  ordersExtra?: ReactNode
  className?: string
}

/**
 * ARCHITECTURE_CYCLE33.md §33.7 (US-33-01, US-33-02, US-33-05, US-33-07) — the one «Устройства и уведомления» block of both
 * profiles. Renders nothing while the role is unknown and for users who are not staff anywhere (Q-33-5).
 */
export function DevicesAndNotificationsSection({
  site,
  appName,
  keepBrowserSubscription = false,
  ordersExtra,
  className = '',
}: DevicesAndNotificationsSectionProps) {
  const push = useWebPush({ site, keepBrowserSubscription })
  const { hash } = useLocation()
  const headingRef = useRef<HTMLHeadingElement>(null)
  const visible = push.isStaff === true
  const handledHash = useRef(false)

  // §33.7.3 — `/profile#devices` (also from the old `/cabinet/devices` redirect): scroll to the block and move focus once.
  useEffect(() => {
    if (!visible || handledHash.current || hash !== '#devices') return
    const el = headingRef.current
    if (!el) return
    handledHash.current = true
    el.scrollIntoView?.({ block: 'start' })
    el.focus({ preventScroll: true })
  }, [visible, hash])

  if (!visible) return null

  const kinds = { hasServices: push.hasServices, hasOrders: push.hasOrders }
  const both = push.hasServices && push.hasOrders
  const busy = push.isEnabling || push.isDisabling
  const switchLabel = staffPushSwitchLabel(kinds)
  const listLoading = push.isLoading
  const hint = push.reason
    ? null
    : duplicateHint(likelySameBrowserOnOtherSite(push.devices, site, describeDevice()), push.isSubscribedOnThisDevice)

  return (
    <section aria-labelledby="devices" className={className}>
      <Card className="p-[26px]">
        <h2 id="devices" ref={headingRef} tabIndex={-1} className="text-[15.5px] font-semibold text-ink mb-1 outline-none scroll-mt-4">
          Устройства и уведомления
        </h2>
        <p className="text-sm text-muted mb-1">{staffPushIntro(kinds, site)}</p>
        {both && <p className="text-sm text-muted mb-3">{ONE_DEVICE_ENOUGH_TEXT}</p>}
        <div className={both ? '' : 'mt-3'}>
          {listLoading && !push.reason ? (
            <div className="h-14 bg-cream-deep rounded-xl animate-pulse" data-testid="devices-loading" />
          ) : push.reason ? (
            <PushUnavailableNotice reason={push.reason} appName={appName} showOneSiteHint={both} />
          ) : (
            <label className="flex items-center justify-between gap-3 py-1 min-h-[44px]">
              <span className="text-sm text-ink-soft">{switchLabel}</span>
              <button
                type="button"
                role="switch"
                aria-checked={push.isSubscribedOnThisDevice}
                aria-label={switchLabel}
                disabled={busy}
                onClick={() => void (push.isSubscribedOnThisDevice ? push.disableOnThisDevice() : push.enableOnThisDevice())}
                className={`relative w-12 h-7 rounded-full transition-colors shrink-0 disabled:opacity-50 ${
                  push.isSubscribedOnThisDevice ? 'bg-ink' : 'bg-line-strong'
                }`}
              >
                <span
                  className={`absolute top-1 left-1 w-5 h-5 rounded-full bg-white transition-transform ${
                    push.isSubscribedOnThisDevice ? 'translate-x-5' : ''
                  }`}
                />
              </button>
            </label>
          )}
        </div>

        {hint && (
          <p className="text-sm text-ink-soft mt-2" data-testid="duplicate-hint">
            {hint}
          </p>
        )}
        {push.actionError && (
          <p role="alert" className="text-sm text-danger mt-2">
            {push.actionError}
          </p>
        )}

        {!listLoading && (
          <div className="mt-5 pt-4 border-t border-line">
            <h3 className="text-[13px] font-medium text-[#4A4038] mb-1.5">Устройства с включёнными уведомлениями</h3>
            {push.devicesError ? (
              <p role="alert" className="text-sm text-danger">
                Не удалось загрузить список устройств. Обновите страницу.
              </p>
            ) : push.devices.length === 0 ? (
              <p className="text-sm text-muted" data-testid="no-devices">
                Пока ни одного устройства. Включите уведомления выше — устройство появится здесь.
              </p>
            ) : (
              <ul className="flex flex-col divide-y divide-line">
                {push.devices.map((d) => (
                  <li key={d.id} className="py-2.5 flex items-center justify-between gap-3">
                    <div className="min-w-0">
                      <p className="text-sm text-ink truncate">
                        {d.deviceLabel}
                        {d.isCurrent && <span className="text-muted"> · это устройство</span>}
                      </p>
                      <p className="text-xs text-muted">
                        {deviceSiteLabel(d.site)} ·{' '}
                        {d.lastSuccessAtUtc
                          ? `Последняя доставка: ${format(parseISO(d.lastSuccessAtUtc), 'd MMM, HH:mm', { locale: ru })}`
                          : `Подписано: ${format(parseISO(d.createdAtUtc), 'd MMM, HH:mm', { locale: ru })}`}
                      </p>
                    </div>
                    <button
                      type="button"
                      disabled={busy}
                      onClick={() => void push.disableDevice(d.id)}
                      className="shrink-0 min-h-[44px] min-w-[44px] inline-flex items-center justify-center text-muted hover:text-danger disabled:opacity-50"
                      aria-label={`Отключить уведомления на устройстве «${d.deviceLabel}»`}
                    >
                      <Icon name="trash" size={15} strokeWidth={1.8} />
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
        )}
      </Card>
      {push.hasOrders && ordersExtra}
    </section>
  )
}
