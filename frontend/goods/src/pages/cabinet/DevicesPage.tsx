import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { Link } from 'react-router-dom'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { useWebPush } from '@/hooks/useWebPush'
import { GoodsIosSteps } from '../../components/push/GoodsIosSteps'
import { LoadingList } from '../../components/StatePanels'
import { goodsPushMessage } from '../../utils/goodsPush'

/**
 * `/cabinet/devices` (US-24-15) — «Уведомления на это устройство» for the shop's staff: turn push on here, see every device
 * of the `Orders` site and switch any of them off. Uses ezbook's `useWebPush` with `site: 'Orders'` and
 * `keepBrowserSubscription: true`: «отключить» removes the SERVER row only, because the buyer role in the same browser
 * shares the one browser subscription (ARCHITECTURE_CYCLE24.md §456.3).
 */
export function DevicesPage() {
  const push = useWebPush({ site: 'Orders', keepBrowserSubscription: true })
  const busy = push.isEnabling || push.isDisabling

  return (
    <main className="max-w-[720px] mx-auto px-4 sm:px-8 pt-8 pb-12">
      <h1 className="font-serif text-[30px] text-ink">Устройства</h1>
      <p className="text-sm text-ink-soft mt-1 mb-6">Уведомления о новых заказах приходят на устройства, где вы их включили. Настройка «сотрудникам» — у каждого магазина в разделе «Уведомления».</p>

      <section aria-labelledby="devices-here" className="rounded-2xl border border-line bg-white p-5 sm:p-6">
        <h2 id="devices-here" className="text-[15px] font-semibold text-ink mb-3">
          Уведомления на это устройство
        </h2>
        {push.isLoading && !push.reason ? (
          <LoadingList rows={1} rowClass="h-14" />
        ) : push.reason ? (
          <div className="rounded-xl bg-cream-deep text-ink-soft text-sm px-4 py-3 flex items-start gap-2.5" data-testid="push-unavailable" data-reason={push.reason}>
            <Icon name="alert-circle" size={15} strokeWidth={1.8} className="shrink-0 mt-0.5 text-muted" />
            <div className="min-w-0">
              <p>{goodsPushMessage(push.reason, 'staff')}</p>
              {push.reason === 'ios-safari-not-installed' && <GoodsIosSteps />}
            </div>
          </div>
        ) : (
          <label className="flex items-center justify-between gap-3 py-1 min-h-[44px]">
            <span className="text-sm text-ink-soft">Уведомлять меня о новых заказах на этом устройстве</span>
            <button
              type="button"
              role="switch"
              aria-checked={push.isSubscribedOnThisDevice}
              aria-label="Уведомлять о новых заказах на этом устройстве"
              disabled={busy}
              onClick={() => void (push.isSubscribedOnThisDevice ? push.disableOnThisDevice() : push.enableOnThisDevice())}
              className={`relative w-12 h-7 rounded-full transition-colors shrink-0 disabled:opacity-50 ${push.isSubscribedOnThisDevice ? 'bg-ink' : 'bg-line-strong'}`}
            >
              <span className={`absolute top-1 left-1 w-5 h-5 rounded-full bg-white transition-transform ${push.isSubscribedOnThisDevice ? 'translate-x-5' : ''}`} />
            </button>
          </label>
        )}
        {push.actionError && (
          <p role="alert" className="text-sm text-danger mt-2">
            {push.actionError}
          </p>
        )}
      </section>

      <section aria-labelledby="devices-list" className="rounded-2xl border border-line bg-white p-5 sm:p-6 mt-6">
        <h2 id="devices-list" className="text-[15px] font-semibold text-ink mb-3">
          Устройства с включёнными уведомлениями
        </h2>
        {push.devices.length === 0 ? (
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
                    {d.lastSuccessAtUtc ? `Последняя доставка: ${format(parseISO(d.lastSuccessAtUtc), 'd MMM, HH:mm', { locale: ru })}` : `Подписано: ${format(parseISO(d.createdAtUtc), 'd MMM, HH:mm', { locale: ru })}`}
                  </p>
                </div>
                <Button variant="ghost" size="sm" className="min-h-[44px] min-w-[44px]" disabled={busy} onClick={() => void push.disableDevice(d.id)} aria-label={`Отключить уведомления на устройстве «${d.deviceLabel}»`}>
                  <Icon name="trash" size={15} strokeWidth={1.8} />
                </Button>
              </li>
            ))}
          </ul>
        )}
      </section>

      <p className="text-xs text-muted mt-6">
        <Link to="/cabinet" className="underline">
          К списку магазинов
        </Link>
      </p>
    </main>
  )
}
