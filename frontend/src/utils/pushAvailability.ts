// ARCHITECTURE_CYCLE9.md §105.10 — the reasons are told apart, "notifications unavailable" alone does
// not pass acceptance (US-118). This module is the single place that decides WHICH explanation applies;
// PushUnavailableNotice.tsx only renders the picked reason's copy.
//
// ARCHITECTURE_CYCLE21.md §362 — cycle 21 answers Q13 with "yes": iPhone push is supported through
// "Add to Home Screen". The iOS checks moved to the TOP of the priority list: an iOS Safari tab has no
// `PushManager` at all, so with the cycle-9 order it always fell into 'unsupported-browser' and the
// master never learned that installing the app is the fix (US-21-02).

export type PushUnavailableReason =
  | 'ios-safari-not-installed'
  | 'ios-version-too-old'
  | 'unsupported-browser'
  | 'insecure-context'
  | 'permission-denied'
  | 'ios-permission-denied'
  | 'platform-disabled'
  | 'company-disabled'

/** What the page knows about iOS/iPadOS. `isIos: false` means none of the iOS branches apply. */
export interface IosEnvironment {
  isIos: boolean
  /** Running as an app opened from the Home Screen (manifest `display: standalone`), not a browser tab. */
  isStandalone: boolean
  /** `[major, minor]` read from the user agent, or `null` when the UA doesn't say. */
  version: readonly [number, number] | null
}

export interface PushAvailabilityInput {
  /** `'serviceWorker' in navigator && 'PushManager' in window` */
  serviceWorkerSupported: boolean
  /** `window.isSecureContext` */
  isSecureContext: boolean
  /** `Notification.permission`, or `'unsupported'` when the `Notification` API itself is missing. */
  permission: NotificationPermission | 'unsupported'
  /** See detectIosEnvironment(). */
  ios: IosEnvironment
  /** `GET /api/push/config` → `enabled`. `undefined` while the request is still loading. */
  platformEnabled: boolean | undefined
  /** `staffPushEnabled` of the company this device's notifications would come from. `undefined` while
   *  loading, or when the master isn't staff anywhere yet. */
  companyStaffPushEnabled: boolean | undefined
}

/** Web Push for Home Screen web apps shipped in iOS/iPadOS 16.4 — below that, installing doesn't help. */
export const IOS_MIN_PUSH_VERSION: readonly [number, number] = [16, 4]

/**
 * Returns the single reason a master cannot (yet) turn push on, in the priority order of
 * §105.10's table as amended by ARCHITECTURE_CYCLE21.md §362, or `null` when the toggle should be
 * offered.
 */
export function getPushUnavailableReason(input: PushAvailabilityInput): PushUnavailableReason | null {
  // iOS first: in a Safari tab the push APIs are simply absent, so every generic check below would
  // misreport the situation as "your browser can't do this".
  if (input.ios.isIos && !input.ios.isStandalone) return 'ios-safari-not-installed'
  if (input.ios.isIos && isIosVersionBelow(input.ios.version, IOS_MIN_PUSH_VERSION)) return 'ios-version-too-old'
  if (!input.serviceWorkerSupported) return 'unsupported-browser'
  if (!input.isSecureContext) return 'insecure-context'
  if (input.permission === 'denied') return input.ios.isIos ? 'ios-permission-denied' : 'permission-denied'
  if (input.platformEnabled === false) return 'platform-disabled'
  if (input.companyStaffPushEnabled === false) return 'company-disabled'
  return null
}

export const PUSH_UNAVAILABLE_MESSAGES: Record<PushUnavailableReason, string> = {
  'ios-safari-not-installed':
    'На айфоне уведомления приходят только приложению EZBOOK, добавленному на экран «Домой». Это займёт минуту:',
  'ios-version-too-old':
    'На этой версии iOS уведомления от сайтов не работают даже с экрана «Домой». Нужна iOS 16.4 или новее — обновить: Настройки → Основные → Обновление ПО.',
  'unsupported-browser': 'Ваш браузер не умеет присылать уведомления. Записи по-прежнему видны на этой странице.',
  'insecure-context': 'Уведомления работают только по защищённому соединению (HTTPS). На этом адресе они недоступны.',
  'permission-denied':
    'Вы запретили уведомления в браузере. Чтобы вернуть: значок замка в адресной строке → Уведомления → Разрешить.',
  'ios-permission-denied':
    'Вы запретили уведомления для EZBOOK. Чтобы вернуть: Настройки айфона → Уведомления → EZBOOK → Допуск уведомлений.',
  'platform-disabled': 'Уведомления на устройство пока не включены на платформе.',
  'company-disabled': 'Уведомления сотрудникам отключены владельцем салона.',
}

type NavigatorLike = Pick<Navigator, 'userAgent' | 'maxTouchPoints'> & { standalone?: boolean }

/**
 * UA-sniff for iOS/iPadOS plus "is this the installed Home Screen app". Every iOS browser (Safari,
 * CriOS, FxiOS, EdgiOS, …) is WebKit under the hood and shares the exact same push limitation, and
 * since iOS 16.4 each of them can "Add to Home Screen" through its Share menu — so they are treated
 * alike rather than special-cased.
 *
 * `displayModeStandalone` is `matchMedia('(display-mode: standalone)').matches`; `navigator.standalone`
 * is Apple's own older flag. Either one being true means we're already inside the installed app.
 */
export function detectIosEnvironment(nav: NavigatorLike, displayModeStandalone = false): IosEnvironment {
  const ua = nav.userAgent
  const isIphoneLike = /iPad|iPhone|iPod/.test(ua)
  // iPadOS 13+ reports itself as desktop Safari ("Macintosh"); a touch screen gives it away.
  const isIpadDesktopUa = ua.includes('Macintosh') && nav.maxTouchPoints > 1
  if (!isIphoneLike && !isIpadDesktopUa) return { isIos: false, isStandalone: false, version: null }

  return {
    isIos: true,
    isStandalone: nav.standalone === true || displayModeStandalone,
    version: parseIosVersion(ua, isIphoneLike),
  }
}

/** Cycle-9 name kept for callers that only need the yes/no answer. */
export function detectIosSafariNotInstalled(nav: NavigatorLike, displayModeStandalone = false): boolean {
  const env = detectIosEnvironment(nav, displayModeStandalone)
  return env.isIos && !env.isStandalone
}

function parseIosVersion(ua: string, isIphoneLike: boolean): readonly [number, number] | null {
  // "CPU iPhone OS 17_4 like Mac OS X" / "CPU OS 16_3 like Mac OS X" (iPad with a mobile UA).
  const os = isIphoneLike ? /OS (\d+)_(\d+)/.exec(ua) : null
  // iPadOS with the desktop UA only carries the Safari version, which tracks the OS version.
  const match = os ?? /Version\/(\d+)\.(\d+)/.exec(ua)
  return match ? [Number(match[1]), Number(match[2])] : null
}

function isIosVersionBelow(version: readonly [number, number] | null, min: readonly [number, number]): boolean {
  // Unknown version → don't block: a false "update iOS" is worse than letting the generic checks decide.
  if (!version) return false
  return version[0] < min[0] || (version[0] === min[0] && version[1] < min[1])
}
