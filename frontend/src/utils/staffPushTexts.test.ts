import { describe, it, expect } from 'vitest'
import {
  deviceSiteLabel,
  duplicateHint,
  likelySameBrowserOnOtherSite,
  staffPushIntro,
  staffPushSwitchLabel,
  staffPushUnavailableMessage,
} from './staffPushTexts'
import type { PushUnavailableReason } from './pushAvailability'
import type { PushSubscriptionDevice } from '../types'

const dev = (over: Partial<PushSubscriptionDevice>): PushSubscriptionDevice => ({
  id: 'd1',
  deviceLabel: 'Chrome на Android',
  createdAtUtc: '2026-10-01T08:00:00Z',
  lastSuccessAtUtc: null,
  isCurrent: false,
  site: 'Orders',
  ...over,
})

describe('staffPushTexts (ARCHITECTURE_CYCLE33.md §33.8)', () => {
  it('noun follows the kinds of companies', () => {
    expect(staffPushSwitchLabel({ hasServices: true, hasOrders: false })).toContain('записях на этом')
    expect(staffPushSwitchLabel({ hasServices: false, hasOrders: true })).toContain('заказах на этом')
    expect(staffPushSwitchLabel({ hasServices: true, hasOrders: true })).toContain('записях и заказах')
  })

  it('intro adds the "not messages to clients" sentence on ezbook only', () => {
    const k = { hasServices: true, hasOrders: false }
    expect(staffPushIntro(k, 'Services')).toContain('а не сообщения клиентам')
    expect(staffPushIntro(k, 'Orders')).not.toContain('а не сообщения клиентам')
  })

  it('every reason has a text that names the app where needed', () => {
    const reasons: PushUnavailableReason[] = [
      'ios-safari-not-installed', 'ios-version-too-old', 'unsupported-browser', 'insecure-context',
      'permission-denied', 'ios-permission-denied', 'platform-disabled', 'company-disabled',
    ]
    for (const app of ['Запись', 'Заказы'] as const) {
      for (const r of reasons) expect(staffPushUnavailableMessage(r, app).length).toBeGreaterThan(10)
      expect(staffPushUnavailableMessage('ios-safari-not-installed', app)).toContain(`«${app}»`)
      expect(staffPushUnavailableMessage('ios-permission-denied', app)).toContain(`Уведомления → «${app}»`)
    }
  })

  it('deviceSiteLabel', () => {
    expect(deviceSiteLabel('Services')).toBe('через ezbook.ru')
    expect(deviceSiteLabel('Orders')).toBe('через goods.ezbook.ru')
  })

  it('likelySameBrowserOnOtherSite: other site + same label only', () => {
    const other = dev({ id: 'o', site: 'Orders' })
    expect(likelySameBrowserOnOtherSite([other], 'Services', 'Chrome на Android')).toBe(other)
    expect(likelySameBrowserOnOtherSite([other], 'Orders', 'Chrome на Android')).toBeNull()
    expect(likelySameBrowserOnOtherSite([other], 'Services', 'Safari на iOS')).toBeNull()
    expect(likelySameBrowserOnOtherSite([], 'Services', 'Chrome на Android')).toBeNull()
  })

  it('duplicateHint: two wordings and null', () => {
    const m = dev({ site: 'Orders' })
    expect(duplicateHint(null, false)).toBeNull()
    expect(duplicateHint(m, false)).toMatch(/уже включены через goods\.ezbook\.ru\. Включать ещё раз не нужно/)
    expect(duplicateHint(m, true)).toMatch(/включены и через goods\.ezbook\.ru\. Одно событие может прийти дважды/)
    expect(duplicateHint(dev({ site: 'Services' }), false)).toContain('через ezbook.ru')
  })
})
