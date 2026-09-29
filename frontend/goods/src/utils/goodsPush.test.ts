import { describe, it, expect } from 'vitest'
import { goodsPushMessage, orderPushStorageKey } from './goodsPush'
import type { PushUnavailableReason } from '@/utils/pushAvailability'

const ALL: PushUnavailableReason[] = ['ios-safari-not-installed', 'ios-version-too-old', 'unsupported-browser', 'insecure-context', 'permission-denied', 'ios-permission-denied', 'platform-disabled', 'company-disabled']

describe('goodsPushMessage', () => {
  it('has a distinct wording for every reason and audience, none of them mentioning салон/записи/EZBOOK', () => {
    for (const reason of ALL) for (const audience of ['customer', 'staff'] as const) {
      const text = goodsPushMessage(reason, audience)
      expect(text.length, `${reason}/${audience}`).toBeGreaterThan(20)
      expect(text).not.toMatch(/салон|записи|EZBOOK/i)
    }
  })
  it('tells the buyer to choose messages next time for every device-side cause, but not the staff', () => {
    for (const r of ['ios-safari-not-installed', 'ios-version-too-old', 'unsupported-browser', 'insecure-context', 'permission-denied', 'ios-permission-denied'] as const) {
      expect(goodsPushMessage(r, 'customer')).toContain('выберите при оформлении сообщения в MAX/WhatsApp')
      expect(goodsPushMessage(r, 'staff')).not.toContain('MAX/WhatsApp')
    }
  })
  it('keys the remembered endpoint per order token', () => {
    expect(orderPushStorageKey('abc')).toBe('goods-order-push:abc')
  })
})
