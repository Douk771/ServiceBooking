// ARCHITECTURE_CYCLE9.md §105.10 — the reasons are told apart, "notifications unavailable" alone does
// not pass acceptance (US-118). This module is the single place that decides WHICH explanation applies;
// PushUnavailableNotice.tsx only renders the picked reason's copy.

export type PushUnavailableReason =
  | 'unsupported-browser'
  | 'insecure-context'
  | 'permission-denied'
  | 'ios-safari-not-installed'
  | 'platform-disabled'
  | 'company-disabled'

export interface PushAvailabilityInput {
  /** `'serviceWorker' in navigator && 'PushManager' in window` */
  serviceWorkerSupported: boolean
  /** `window.isSecureContext` */
  isSecureContext: boolean
  /** `Notification.permission`, or `'unsupported'` when the `Notification` API itself is missing. */
  permission: NotificationPermission | 'unsupported'
  /** iOS/iPadOS Safari that is NOT running as an installed "Add to Home Screen" app (Q13). */
  isIosSafariNotInstalled: boolean
  /** `GET /api/push/config` → `enabled`. `undefined` while the request is still loading. */
  platformEnabled: boolean | undefined
  /** `staffPushEnabled` of the company this device's notifications would come from. `undefined` while
   *  loading, or when the master isn't staff anywhere yet. */
  companyStaffPushEnabled: boolean | undefined
}

/**
 * Returns the single reason a master cannot (yet) turn push on, in the priority order of
 * §105.10's table, or `null` when the toggle should be offered.
 */
export function getPushUnavailableReason(input: PushAvailabilityInput): PushUnavailableReason | null {
  if (!input.serviceWorkerSupported) return 'unsupported-browser'
  if (!input.isSecureContext) return 'insecure-context'
  if (input.permission === 'denied') return 'permission-denied'
  if (input.isIosSafariNotInstalled) return 'ios-safari-not-installed'
  if (input.platformEnabled === false) return 'platform-disabled'
  if (input.companyStaffPushEnabled === false) return 'company-disabled'
  return null
}

export const PUSH_UNAVAILABLE_MESSAGES: Record<PushUnavailableReason, string> = {
  'unsupported-browser': 'Ваш браузер не умеет присылать уведомления. Записи по-прежнему видны на этой странице.',
  'insecure-context': 'Уведомления работают только по защищённому соединению (HTTPS). На этом адресе они недоступны.',
  'permission-denied':
    'Вы запретили уведомления в браузере. Чтобы вернуть: значок замка в адресной строке → Уведомления → Разрешить.',
  'ios-safari-not-installed':
    'На айфоне уведомления приходят только приложению, добавленному на экран Домой. Сейчас их не будет.',
  'platform-disabled': 'Уведомления на устройство пока не включены на платформе.',
  'company-disabled': 'Уведомления сотрудникам отключены владельцем салона.',
}

/** UA-sniff for iOS/iPadOS Safari, restricted to "not already installed as a standalone app" (Q13). */
export function detectIosSafariNotInstalled(nav: Pick<Navigator, 'userAgent' | 'maxTouchPoints'>): boolean {
  const ua = nav.userAgent
  const isIos = /iPad|iPhone|iPod/.test(ua) || (ua.includes('Macintosh') && nav.maxTouchPoints > 1)
  if (!isIos) return false
  // Safari on iOS always includes "Safari" in the UA; other iOS browsers (CriOS, FxiOS, ...) are
  // Chromium/Gecko wrappers forced through WebKit by Apple and share the exact same push limitation, so
  // they're treated the same rather than special-cased out.
  const nav2 = nav as Navigator & { standalone?: boolean }
  return nav2.standalone !== true
}
