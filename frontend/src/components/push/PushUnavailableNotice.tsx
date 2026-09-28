import { Icon } from '../ui/Icon'
import { PUSH_UNAVAILABLE_MESSAGES, type PushUnavailableReason } from '../../utils/pushAvailability'

/**
 * ARCHITECTURE_CYCLE9.md §105.10 (US-118) — one explanation per reason, never a generic "notifications
 * unavailable". `reason` is picked by useWebPush()/getPushUnavailableReason and just printed here
 * verbatim: this component makes no decisions of its own.
 *
 * ARCHITECTURE_CYCLE21.md §363 (US-21-01) — for an iPhone outside the installed app the copy is
 * followed by step-by-step "Add to Home Screen" instructions: saying "install it" without saying how
 * is exactly the dead end the customer hit.
 */
export function PushUnavailableNotice({ reason }: { reason: PushUnavailableReason }) {
  return (
    <div className="rounded-xl bg-cream-deep text-ink-soft text-sm px-4 py-3 flex items-start gap-2.5">
      <Icon name="alert-circle" size={15} strokeWidth={1.8} className="shrink-0 mt-0.5 text-muted" />
      <div className="min-w-0">
        <p>{PUSH_UNAVAILABLE_MESSAGES[reason]}</p>
        {reason === 'ios-safari-not-installed' && <IosInstallSteps />}
      </div>
    </div>
  )
}

function IosInstallSteps() {
  return (
    <ol className="list-decimal pl-5 mt-2 flex flex-col gap-1.5" aria-label="Как добавить EZBOOK на экран «Домой»">
      <li>
        Нажмите кнопку «Поделиться»{' '}
        <ShareGlyph />
        {' '}внизу или вверху экрана. Если её не видно — сначала нажмите «⋯».
      </li>
      <li>
        Выберите «На экран «Домой»» (если пункта нет в списке — пролистайте вниз), затем «Добавить».
      </li>
      <li>
        Откройте EZBOOK с новой иконки на экране «Домой» и <b className="font-medium text-ink">войдите заново</b> —
        приложение не видит вход, выполненный в браузере.
      </li>
      <li>Зайдите в «Мои записи» и включите уведомления переключателем — айфон спросит разрешение.</li>
    </ol>
  )
}

/** The iOS "Share" glyph (square with an arrow out of the top) — there's no such icon in ui/Icon. */
function ShareGlyph() {
  return (
    <svg
      viewBox="0 0 24 24"
      width={14}
      height={14}
      fill="none"
      stroke="currentColor"
      strokeWidth={1.8}
      strokeLinecap="round"
      strokeLinejoin="round"
      className="inline-block align-[-2px] text-ink"
      aria-hidden="true"
    >
      <path d="M12 3v12" />
      <path d="M8 7l4-4 4 4" />
      <path d="M7 11H5v10h14V11h-2" />
    </svg>
  )
}
