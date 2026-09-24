import { describe, it, expect } from 'vitest'
import { getPushUnavailableReason, detectIosSafariNotInstalled, type PushAvailabilityInput } from './pushAvailability'

const BASE: PushAvailabilityInput = {
  serviceWorkerSupported: true,
  isSecureContext: true,
  permission: 'default',
  isIosSafariNotInstalled: false,
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
    expect(getPushUnavailableReason({ ...BASE, isIosSafariNotInstalled: true })).toBe('ios-safari-not-installed')
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
