import { createHash } from 'node:crypto'
import { describe, expect, it } from 'vitest'
import { fallbackFullHtml, fallbackShortHtml, staffHintFallbackHtml } from './messengerOptIn'

// The server stores the version of a messenger tick made before the lawyer's text existed as `fallback:<key>:<MessengerConsentVersions.FallbackRevision>`.
// If a fallback text below changes, THIS test fails: raise FallbackRevision in ServiceBooking.API/Services/Notifications/MessengerConsentVersions.cs and re-pin the hash.
const PINNED = 'efaa1933c31ea07f365920724bdbccf3721a99ac60e87b602c4639dd889d4347'

describe('the fallback texts of the messenger tick (Т40-L-12)', () => {
  it('are the edition the server names in MessengerConsentVersions.FallbackRevision', () => {
    const all = (['booking', 'order', 'stay'] as const).flatMap((k) => [fallbackShortHtml(k), fallbackFullHtml(k)]).concat(staffHintFallbackHtml('MAX')).join('\n')
    expect(createHash('sha256').update(all).digest('hex')).toBe(PINNED)
  })
})
