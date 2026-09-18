import { describe, it, expect } from 'vitest'
import { getChannelBannerKind } from './channelBanner'

describe('getChannelBannerKind', () => {
  it('Connected with no idle deadline → no banner', () => {
    expect(getChannelBannerKind('Connected', null)).toBeNull()
  })

  it('Disconnected → broken, regardless of idle deadline', () => {
    expect(getChannelBannerKind('Disconnected', null)).toBe('broken')
  })

  it('Blocked → broken (US-63 replace scenario)', () => {
    expect(getChannelBannerKind('Blocked', null)).toBe('broken')
  })

  it('NeedsReconnect → broken, must read differently from NotConnected (US-55 п. 1)', () => {
    expect(getChannelBannerKind('NeedsReconnect', null)).toBe('broken')
  })

  it('NotConnected → no banner (never-connected is not a disruption)', () => {
    expect(getChannelBannerKind('NotConnected', null)).toBeNull()
  })

  it('Connected but idle deadline set → idle warning, not broken', () => {
    expect(getChannelBannerKind('Connected', '2026-09-21T00:00:00Z')).toBe('idle')
  })

  it('a broken state with an idle deadline still resolves to broken (broken wins)', () => {
    expect(getChannelBannerKind('Disconnected', '2026-09-21T00:00:00Z')).toBe('broken')
  })

  it('DisabledByOwner → no banner (owner action, not a disruption to warn about)', () => {
    expect(getChannelBannerKind('DisabledByOwner', null)).toBeNull()
  })
})
