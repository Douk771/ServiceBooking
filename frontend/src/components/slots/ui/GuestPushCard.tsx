import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { useSlotGuestPush, type GuestPushApi } from '@/hooks/slots/useSlotGuestPush'
import { guestPushMessage } from '@/utils/slots/slotPush'
import { InlineError } from '@/components/slots/ui/StatePanels'

/**
 * «Уведомлять о статусе в этом браузере» on the booking page. Whether to show anything is the server's call
 * (`notifications.webPush.available`); device-side causes (iPhone outside the Home Screen app, denied permission) are explained with
 * the shared reason picker. The prompt appears only on the click. Everything stays visible on this page either way (R37-5).
 */
export function GuestPushCard({
  token,
  info,
  kind = 'booking',
  api,
}: {
  token: string
  info: { available: boolean; publicKey?: string | null }
  kind?: 'booking' | 'order'
  /** The routes of the page `kind` points at (the vertical picks them). */
  api: GuestPushApi
}) {
  const push = useSlotGuestPush({ kind, token, publicKey: info.publicKey, api })
  if (!info.available) return null

  return (
    <section className="rounded-2xl border border-line bg-white px-5 py-4" aria-label="Уведомления о брони" data-testid="guest-push">
      <p className="flex items-center gap-2 text-sm font-semibold text-ink">
        <Icon name="bell" size={15} strokeWidth={1.8} /> Уведомления о брони
      </p>
      {push.reason ? (
        <p className="mt-1.5 text-sm text-ink-soft" data-testid="guest-push-reason" data-reason={push.reason}>
          {guestPushMessage(push.reason)}
        </p>
      ) : push.subscribed ? (
        <div className="mt-1.5 flex flex-wrap items-center justify-between gap-3">
          <p className="text-sm font-medium text-success">Уведомления в этом браузере включены</p>
          <Button variant="secondary" size="sm" loading={push.busy} onClick={() => void push.disable()} className="min-h-[44px]">
            Отключить
          </Button>
        </div>
      ) : (
        <div className="mt-1.5 flex flex-wrap items-center justify-between gap-3">
          <p className="text-sm text-ink-soft">Браузер сообщит, когда компания подтвердит оплату и когда будет готова информация к заселению.</p>
          <Button size="sm" loading={push.busy} onClick={() => void push.enable()} className="min-h-[44px]">
            Уведомлять в этом браузере
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
