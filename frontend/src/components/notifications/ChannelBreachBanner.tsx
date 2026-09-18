import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { Icon } from '../ui/Icon'
import { Button } from '../ui/Button'
import { getChannelBannerKind } from '../../utils/channelBanner'
import type { ChannelState } from '../../types'

interface Props {
  state: ChannelState
  /** Server-built text (§19.5) — the frontend never writes its own copy for these states (§37 п. 2). */
  stateText: string
  idleSince?: string | null
  idleDeadline?: string | null
  onReplace?: () => void
  replacing?: boolean
}

/**
 * R19: the in-cabinet banner is the owner's ONLY channel of notice about a broken or about-to-expire
 * channel (email was cut, US-62 §4). Renders in three places per US-62 п. 2: the channel list, each
 * assigned company's notification settings, and the general cabinet header — always the same
 * component so the wording can't drift between them.
 */
export function ChannelBreachBanner({ state, stateText, idleSince, idleDeadline, onReplace, replacing }: Props) {
  const kind = getChannelBannerKind(state, idleDeadline)
  if (!kind) return null

  if (kind === 'broken') {
    return (
      <div className="rounded-2xl bg-danger-bg border border-[#EAC5B9] px-5 py-4 flex items-start gap-3">
        <Icon name="alert-circle" size={18} strokeWidth={1.8} className="text-danger shrink-0 mt-0.5" />
        <div className="flex-1">
          <p className="text-sm font-semibold text-danger">Канал уведомлений не работает</p>
          <p className="text-sm text-danger/90 mt-0.5">{stateText}</p>
        </div>
        {state === 'Blocked' && onReplace && (
          <Button variant="danger" size="sm" loading={replacing} onClick={onReplace} className="shrink-0">
            Подключить другой номер
          </Button>
        )}
      </div>
    )
  }

  return (
    <div className="rounded-2xl bg-warning-bg border border-[#EAD9AC] px-5 py-4 flex items-start gap-3">
      <Icon name="alert-circle" size={18} strokeWidth={1.8} className="text-warning shrink-0 mt-0.5" />
      <div>
        <p className="text-sm font-semibold text-warning">Каналом сейчас никто не пользуется</p>
        <p className="text-sm text-warning/90 mt-0.5">
          {idleSince && <>С {format(parseISO(idleSince), 'd MMMM', { locale: ru })} у канала нет ни одной активной компании. </>}
          Если ничего не изменится, {idleDeadline && format(parseISO(idleDeadline), 'd MMMM', { locale: ru })} номер
          будет отключён — его придётся привязывать заново по QR-коду (повторная оплата не потребуется, назначенные
          салоны и оплаченный период сохранятся). Чтобы этого не произошло, разблокируйте или активируйте салон,
          назначьте компанию на канал или продлите оплату.
        </p>
      </div>
    </div>
  )
}
