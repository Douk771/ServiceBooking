import { describe, it, expect } from 'vitest'
import { goodsPushMessage, orderPushStorageKey } from './goodsPush'
import type { PushUnavailableReason } from '@/utils/pushAvailability'

const ALL: PushUnavailableReason[] = ['ios-safari-not-installed', 'ios-version-too-old', 'unsupported-browser', 'insecure-context', 'permission-denied', 'ios-permission-denied', 'platform-disabled', 'company-disabled']

describe('goodsPushMessage', () => {
  it('has a wording for every reason, none of them mentioning салон/записи/EZBOOK', () => {
    for (const reason of ALL) {
      const text = goodsPushMessage(reason, 'customer')
      expect(text.length, reason).toBeGreaterThan(20)
      expect(text).not.toMatch(/салон|записи|EZBOOK/i)
    }
  })
  it('tells the buyer to choose messages next time for every device-side cause', () => {
    for (const r of ['ios-safari-not-installed', 'ios-version-too-old', 'unsupported-browser', 'insecure-context', 'permission-denied', 'ios-permission-denied'] as const) {
      expect(goodsPushMessage(r, 'customer')).toContain('выберите при оформлении сообщения в MAX/WhatsApp')
    }
  })
  it('keys the remembered endpoint per order token', () => {
    expect(orderPushStorageKey('abc')).toBe('goods-order-push:abc')
  })
})
