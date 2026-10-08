import { describe, it, expect } from 'vitest'
import {
  OPTED_OUT_TEXT,
  fallbackCheckboxLabel,
  fallbackFullHtml,
  fallbackShortHtml,
  messengerNames,
  optInPayloadValue,
  resolveOptInDefault,
  staffConsentLabel,
  staffHintFallbackHtml,
  type OptInKind,
} from './messengerOptIn'

describe('resolveOptInDefault (Т40-L-09, §40.11.4)', () => {
  it('a guest never gets the box pre-ticked', () => {
    expect(resolveOptInDefault(false, { enabled: true, providerDeliveryConsent: true })).toEqual({ kind: 'guest', checked: false })
  })
  it('a signed-in user is pre-ticked only with enabled AND a valid ProviderDelivery consent', () => {
    expect(resolveOptInDefault(true, { enabled: true, providerDeliveryConsent: true }).checked).toBe(true)
    expect(resolveOptInDefault(true, { enabled: true, providerDeliveryConsent: false }).checked).toBe(false)
  })
  it('an old server without the field or a profile that has not loaded yet means unchecked', () => {
    expect(resolveOptInDefault(true, { enabled: true }).checked).toBe(false)
    expect(resolveOptInDefault(true, undefined).checked).toBe(false)
  })
  it('an opted-out user (enabled = false) wins over any consent, whatever it says', () => {
    expect(resolveOptInDefault(true, { enabled: false, providerDeliveryConsent: true })).toEqual({ kind: 'optedOut', checked: false })
  })
})

describe('optInPayloadValue', () => {
  it('is undefined (field not sent) when not offered or opted out', () => {
    expect(optInPayloadValue(false, false, true)).toBeUndefined()
    expect(optInPayloadValue(true, true, true)).toBeUndefined()
  })
  it('is the box value when offered', () => {
    expect(optInPayloadValue(true, false, true)).toBe(true)
    expect(optInPayloadValue(true, false, false)).toBe(false)
  })
})

describe('texts', () => {
  it('names transports for substitutions', () => {
    expect(messengerNames(['WhatsApp'])).toBe('WhatsApp')
    expect(messengerNames(['Max'])).toBe('MAX')
    expect(messengerNames(['WhatsApp', 'Max'])).toBe('WhatsApp и MAX')
  })
  it('staff label is the verbatim LEGAL_REVIEW_CYCLE40 §7.3 line', () => {
    expect(staffConsentLabel('MAX')).toBe('Клиент согласился получать сообщения об этой записи в MAX')
  })
  it('neutral label per vertical when the server sends none', () => {
    expect(fallbackCheckboxLabel('booking', 'MAX')).toBe('Получать уведомления о записи в MAX')
    expect(fallbackCheckboxLabel('order', 'MAX')).toBe('Получать уведомления о заказе в MAX')
    expect(fallbackCheckboxLabel('stay', 'MAX')).toBe('Получать уведомления о брони в MAX')
  })
  it('fallbacks keep the consent link, never carry an unresolved placeholder and never mention circumvention (Т40-L-14)', () => {
    const kinds: OptInKind[] = ['booking', 'order', 'stay']
    const all = [
      OPTED_OUT_TEXT,
      staffHintFallbackHtml('MAX'),
      ...kinds.flatMap((k) => [fallbackShortHtml(k), fallbackFullHtml(k)]),
    ].join('\n')
    expect(all).not.toMatch(/\{\{/)
    expect(all).not.toMatch(/обход|обойти|vpn|впн|прокси|proxy|анонимайзер|зеркал/i)
    for (const k of kinds) expect(fallbackShortHtml(k)).toContain('href="/pdn-consent"')
  })
  it('short fallback differs by vertical in the verbatim tail', () => {
    expect(fallbackShortHtml('booking')).toContain('сообщения о записи')
    expect(fallbackShortHtml('order')).toContain('а статус виден на странице заказа')
    expect(fallbackShortHtml('stay')).toContain('сведения о ней доступны на её странице')
  })
})
