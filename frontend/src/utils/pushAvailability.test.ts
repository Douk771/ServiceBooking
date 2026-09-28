import { describe, it, expect } from 'vitest'
import {
  getPushUnavailableReason,
  detectIosEnvironment,
  detectIosSafariNotInstalled,
  type IosEnvironment,
  type PushAvailabilityInput,
} from './pushAvailability'

const IOS_TAB: IosEnvironment = { isIos: true, isStandalone: false, version: [17, 4] }
const IOS_APP: IosEnvironment = { isIos: true, isStandalone: true, version: [17, 4] }

const BASE: PushAvailabilityInput = {
  serviceWorkerSupported: true,
  isSecureContext: true,
  permission: 'default',
  ios: { isIos: false, isStandalone: false, version: null },
  platformEnabled: true,
  companyStaffPushEnabled: true,
}

describe('getPushUnavailableReason (ARCHITECTURE_CYCLE9.md §105.10)', () => {
  it('everything available → null (toggle is offered)', () => {
    expect(getPushUnavailableReason(BASE)).toBeNull()
  })

  it('no serviceWorker/PushManager → unsupported-browser, wins over everything else', () => {
    expect(
      getPushUnavailableReason({ ...BASE, serviceWorkerSupported: false, isSecureContext: false, permission: 'denied' }),
    ).toBe('unsupported-browser')
  })

  it('not a secure context → insecure-context', () => {
    expect(getPushUnavailableReason({ ...BASE, isSecureContext: false })).toBe('insecure-context')
  })

  it('permission denied → permission-denied, even when platform/company are disabled', () => {
    expect(
      getPushUnavailableReason({ ...BASE, permission: 'denied', platformEnabled: false, companyStaffPushEnabled: false }),
    ).toBe('permission-denied')
  })

  it('iOS Safari not installed to home screen → ios-safari-not-installed', () => {
    expect(getPushUnavailableReason({ ...BASE, ios: IOS_TAB })).toBe('ios-safari-not-installed')
  })

  it('GET /push/config enabled:false → platform-disabled', () => {
    expect(getPushUnavailableReason({ ...BASE, platformEnabled: false })).toBe('platform-disabled')
  })

  it('company staffPushEnabled:false → company-disabled, permission is never requested (US-118)', () => {
    expect(getPushUnavailableReason({ ...BASE, companyStaffPushEnabled: false })).toBe('company-disabled')
  })

  it('platformEnabled still loading (undefined) does not trip platform-disabled', () => {
    expect(getPushUnavailableReason({ ...BASE, platformEnabled: undefined })).toBeNull()
  })
})

describe('detectIosSafariNotInstalled', () => {
  const IPHONE_UA =
    'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1'
  const ANDROID_UA = 'Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Mobile Safari/537.36'

  it('iPhone Safari, not standalone → true', () => {
    expect(detectIosSafariNotInstalled({ userAgent: IPHONE_UA, maxTouchPoints: 5 })).toBe(true)
  })

  it('iPhone Safari installed to home screen (standalone) → false', () => {
    const nav = { userAgent: IPHONE_UA, maxTouchPoints: 5, standalone: true } as Navigator & { standalone: boolean }
    expect(detectIosSafariNotInstalled(nav)).toBe(false)
  })

  it('Android Chrome → false, not iOS at all', () => {
    expect(detectIosSafariNotInstalled({ userAgent: ANDROID_UA, maxTouchPoints: 5 })).toBe(false)
  })

  it('desktop → false', () => {
    expect(
      detectIosSafariNotInstalled({
        userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15',
        maxTouchPoints: 0,
      }),
    ).toBe(false)
  })
})

// ARCHITECTURE_CYCLE19.md §362 — cycle 19 (Q13 answered "yes": push on iPhone via the Home Screen).
describe('CY19 — iPhone: Home Screen install is the fix, and the page says so', () => {
  it('CY19-01 an iOS Safari tab has no PushManager → still ios-safari-not-installed, NOT unsupported-browser', () => {
    // The cycle-9 bug: 'unsupported-browser' won, so the iPhone copy was unreachable in real Safari.
    expect(
      getPushUnavailableReason({ ...BASE, ios: IOS_TAB, serviceWorkerSupported: false, permission: 'unsupported' }),
    ).toBe('ios-safari-not-installed')
  })

  it('CY19-02 installed app on iOS ≥ 16.4 with everything available → toggle offered', () => {
    expect(getPushUnavailableReason({ ...BASE, ios: IOS_APP })).toBeNull()
  })

  it('CY19-03 installed app on iOS 16.3 → ios-version-too-old, even though the APIs look absent', () => {
    expect(
      getPushUnavailableReason({ ...BASE, ios: { ...IOS_APP, version: [16, 3] }, serviceWorkerSupported: false }),
    ).toBe('ios-version-too-old')
  })

  it('CY19-04 exactly iOS 16.4 is enough', () => {
    expect(getPushUnavailableReason({ ...BASE, ios: { ...IOS_APP, version: [16, 4] } })).toBeNull()
  })

  it('CY19-05 unknown iOS version does not claim "update iOS"', () => {
    expect(getPushUnavailableReason({ ...BASE, ios: { ...IOS_APP, version: null } })).toBeNull()
  })

  it('CY19-06 permission denied inside the installed app → iPhone Settings instructions, not the padlock', () => {
    expect(getPushUnavailableReason({ ...BASE, ios: IOS_APP, permission: 'denied' })).toBe('ios-permission-denied')
    expect(getPushUnavailableReason({ ...BASE, permission: 'denied' })).toBe('permission-denied')
  })

  it('CY19-07 installed app still respects platform/company switches', () => {
    expect(getPushUnavailableReason({ ...BASE, ios: IOS_APP, platformEnabled: false })).toBe('platform-disabled')
    expect(getPushUnavailableReason({ ...BASE, ios: IOS_APP, companyStaffPushEnabled: false })).toBe('company-disabled')
  })
})

describe('CY19 — detectIosEnvironment', () => {
  const IPHONE_174 =
    'Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Mobile/15E148 Safari/604.1'
  const IPHONE_CHROME_163 =
    'Mozilla/5.0 (iPhone; CPU iPhone OS 16_3 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/120.0 Mobile/15E148 Safari/604.1'
  const IPAD_DESKTOP_UA =
    'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Safari/605.1.15'

  it('CY19-08 iPhone Safari tab → iOS, not standalone, version parsed from "OS 17_4"', () => {
    expect(detectIosEnvironment({ userAgent: IPHONE_174, maxTouchPoints: 5 })).toEqual({
      isIos: true,
      isStandalone: false,
      version: [17, 4],
    })
  })

  it('CY19-09 display-mode: standalone alone (no navigator.standalone) counts as installed', () => {
    expect(detectIosEnvironment({ userAgent: IPHONE_174, maxTouchPoints: 5 }, true).isStandalone).toBe(true)
    expect(detectIosSafariNotInstalled({ userAgent: IPHONE_174, maxTouchPoints: 5 }, true)).toBe(false)
  })

  it('CY19-10 Chrome on iPhone is treated like Safari (same WebKit limitation), version from the OS token', () => {
    expect(detectIosEnvironment({ userAgent: IPHONE_CHROME_163, maxTouchPoints: 5 })).toEqual({
      isIos: true,
      isStandalone: false,
      version: [16, 3],
    })
  })

  it('CY19-11 iPadOS with a desktop UA → iOS, version from "Version/18.1"', () => {
    expect(detectIosEnvironment({ userAgent: IPAD_DESKTOP_UA, maxTouchPoints: 5 })).toEqual({
      isIos: true,
      isStandalone: false,
      version: [18, 1],
    })
  })

  it('CY19-12 a real Mac (no touch) is not iOS', () => {
    expect(detectIosEnvironment({ userAgent: IPAD_DESKTOP_UA, maxTouchPoints: 0 }).isIos).toBe(false)
  })
})
