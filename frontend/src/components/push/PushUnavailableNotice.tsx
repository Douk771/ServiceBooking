import { Icon } from '../ui/Icon'
import type { PushUnavailableReason } from '../../utils/pushAvailability'
import { ONE_SITE_ENOUGH_TEXT, staffPushUnavailableMessage, type PushAppName } from '../../utils/staffPushTexts'

/**
 * ARCHITECTURE_CYCLE33.md §33.8 (US-118, US-21-01) — one explanation per reason, never a generic «unavailable».
 * `reason` is picked by useWebPush()/getPushUnavailableReason and printed here; the copy lives in staffPushTexts.ts and is
 * shared by both sites. For an iPhone outside the installed app the copy is followed by «Add to Home Screen» steps.
 */
export function PushUnavailableNotice({
  reason,
  appName,
  showOneSiteHint = false,
}: {
  reason: PushUnavailableReason
  appName: PushAppName
  /** Staff of both a salon and a shop: one of the two sites is enough. */
  showOneSiteHint?: boolean
}) {
  return (
    <div
      className="rounded-xl bg-cream-deep text-ink-soft text-sm px-4 py-3 flex items-start gap-2.5"
      data-testid="push-unavailable"
      data-reason={reason}
    >
      <Icon name="alert-circle" size={15} strokeWidth={1.8} className="shrink-0 mt-0.5 text-muted" />
      <div className="min-w-0">
        <p>{staffPushUnavailableMessage(reason, appName)}</p>
        {reason === 'ios-safari-not-installed' && <IosInstallSteps appName={appName} showOneSiteHint={showOneSiteHint} />}
      </div>
    </div>
  )
}

function IosInstallSteps({ appName, showOneSiteHint }: { appName: PushAppName; showOneSiteHint: boolean }) {
  return (
    <>
      <ol className="list-decimal pl-5 mt-2 flex flex-col gap-1.5" aria-label={`Как добавить «${appName}» на экран «Домой»`}>
        <li>
          Нажмите кнопку «Поделиться» <ShareGlyph /> внизу или вверху экрана. Если её не видно — сначала нажмите «⋯».
        </li>
        <li>Выберите «На экран «Домой»» (если пункта нет в списке — пролистайте вниз), затем «Добавить».</li>
        <li>
          Откройте «{appName}» с новой иконки на экране «Домой» и <b className="font-medium text-ink">войдите заново</b> —
          приложение не видит вход, выполненный в браузере.
        </li>
        <li>Откройте «Профиль» → «Устройства и уведомления» и включите переключатель — айфон спросит разрешение.</li>
      </ol>
      {showOneSiteHint && <p className="mt-2">{ONE_SITE_ENOUGH_TEXT}</p>}
    </>
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
