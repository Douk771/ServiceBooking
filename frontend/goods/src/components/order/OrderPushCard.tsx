import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { useOrderPush } from '../../hooks/useOrderPush'
import { goodsPushMessage } from '../../utils/goodsPush'
import { InlineError } from '../StatePanels'
import type { OrderWebPushInfoDto } from '../../types'

/**
 * US-24-21 — «Уведомлять о статусе в этом браузере» on the order page. Whether to show anything at all is the server's call
 * (`available`, `unavailableText`): `available: false` with no text = the shop turned it off or the order is finished → nothing.
 * Device-side causes (iPhone outside the Home Screen app, denied permission) are explained with the shared reason picker.
 */
export function OrderPushCard({ token, info }: { token: string; info: OrderWebPushInfoDto }) {
  const push = useOrderPush({ token, publicKey: info.publicKey })

  if (!info.available && !info.unavailableText) return null

  return (
    <section className="rounded-2xl border border-line bg-white px-5 py-4" aria-label="Уведомления о заказе" data-testid="order-push">
      <p className="text-sm font-semibold text-ink flex items-center gap-2">
        <Icon name="bell" size={15} strokeWidth={1.8} /> Уведомления о статусе
      </p>
      {!info.available ? (
        <p className="mt-1.5 text-sm text-ink-soft" data-testid="order-push-unavailable">
          {info.unavailableText}
        </p>
      ) : push.reason ? (
        <p className="mt-1.5 text-sm text-ink-soft" data-testid="order-push-reason" data-reason={push.reason}>
          {goodsPushMessage(push.reason)}
        </p>
      ) : push.subscribed ? (
        <div className="mt-1.5 flex items-center justify-between gap-3 flex-wrap">
          <p className="text-sm text-success font-medium">Уведомления в этом браузере включены</p>
          <Button variant="secondary" size="sm" loading={push.busy} onClick={() => void push.disable()}>
            Отключить
          </Button>
        </div>
      ) : (
        <div className="mt-1.5 flex items-center justify-between gap-3 flex-wrap">
          <p className="text-sm text-ink-soft">Браузер пришлёт уведомление, когда заказ примут и когда он будет готов.</p>
          <Button size="sm" loading={push.busy} onClick={() => void push.enable()}>
            Уведомлять о статусе в этом браузере
          </Button>
        </div>
      )}
      {push.error && (
        <div className="mt-2">
          <InlineError>{push.error}</InlineError>
        </div>
      )}
    </section>
  )
}
