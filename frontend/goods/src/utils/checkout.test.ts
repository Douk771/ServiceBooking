// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { checkoutGate, loginUrlForCheckout, maskPhone, storefrontMessengerOffer, validateCheckout } from './checkout'

const ru = (p: string) => /^7\d{10}$/.test(p)

describe('checkoutGate', () => {
  it('is always open in the Anyone mode', () => {
    expect(checkoutGate({ customerMode: 'Anyone', signedIn: false, phoneVerified: undefined, verificationEnabled: undefined })).toEqual({ kind: 'open' })
  })
  it('asks a guest to sign in in the strict mode', () => {
    expect(checkoutGate({ customerMode: 'VerifiedPhoneOnly', signedIn: false, phoneVerified: undefined, verificationEnabled: true }).kind).toBe('login-required')
  })
  it('waits for the profile, then opens for a verified phone', () => {
    const base = { customerMode: 'VerifiedPhoneOnly' as const, signedIn: true, verificationEnabled: true }
    expect(checkoutGate({ ...base, phoneVerified: undefined }).kind).toBe('checking')
    expect(checkoutGate({ ...base, phoneVerified: true }).kind).toBe('open')
  })
  it('offers MAX verification when the subsystem is on, says impossible when off', () => {
    const base = { customerMode: 'VerifiedPhoneOnly' as const, signedIn: true, phoneVerified: false }
    expect(checkoutGate({ ...base, verificationEnabled: true }).kind).toBe('verify-phone')
    expect(checkoutGate({ ...base, verificationEnabled: false }).kind).toBe('verification-unavailable')
  })
  it('a verified phone opens ordering even if the subsystem is switched off', () => {
    expect(checkoutGate({ customerMode: 'VerifiedPhoneOnly', signedIn: true, phoneVerified: true, verificationEnabled: false }).kind).toBe('open')
  })
})

describe('loginUrlForCheckout', () => {
  it('returns to the shop with the checkout flag, encoded', () => {
    expect(loginUrlForCheckout('shaurma')).toBe('/login?returnTo=%2Fshaurma%3Fcheckout%3D1')
    expect(loginUrlForCheckout('shaurma', 'register')).toMatch(/^\/register\?returnTo=/)
  })
})

describe('validateCheckout', () => {
  const guest = { name: 'Иван', comment: '', phone: '79001234567', guest: true, captchaRequired: true, captchaToken: 't' }
  it('accepts a complete guest order', () => {
    expect(validateCheckout(guest, ru)).toBeNull()
  })
  it('checks name, phone, captcha and comment in order', () => {
    expect(validateCheckout({ ...guest, name: '  ' }, ru)).toBe('Укажите имя')
    expect(validateCheckout({ ...guest, phone: '' }, ru)).toBe('Укажите телефон')
    expect(validateCheckout({ ...guest, phone: '7900' }, ru)).toMatch(/формате/)
    expect(validateCheckout({ ...guest, captchaToken: '' }, ru)).toBe('Подтвердите, что вы не робот')
    expect(validateCheckout({ ...guest, comment: 'x'.repeat(501) }, ru)).toMatch(/500/)
  })
  it('a signed-in buyer needs neither phone nor captcha', () => {
    expect(validateCheckout({ ...guest, guest: false, phone: '', captchaToken: '' }, ru)).toBeNull()
  })
  it('does not demand a captcha when none is configured', () => {
    expect(validateCheckout({ ...guest, captchaRequired: false, captchaToken: '' }, ru)).toBeNull()
  })
})

describe('messenger consent line (cycle 24, L9)', () => {
  it('masks all but the first and last two digits of a Russian number', () => {
    expect(maskPhone('79001234567')).toBe('+7 (9**) ***-**-67')
    expect(maskPhone('+7 (900) 123-45-67')).toBe('+7 (9**) ***-**-67')
  })
  it('never prints a foreign or partial number', () => {
    expect(maskPhone('375291234567')).toBe('')
    expect(maskPhone('7900')).toBe('')
    expect(maskPhone(undefined)).toBe('')
  })
  it('cycle 40: offer comes from the server flags only; not offered clears transports and label', () => {
    expect(storefrontMessengerOffer({ customerNotifications: { messengerOffered: false, messengerTransports: ['Max'], messengerLabel: 'x' } })).toEqual({ offered: false, transports: [], checkboxLabel: null })
    expect(storefrontMessengerOffer({})).toEqual({ offered: false, transports: [], checkboxLabel: null })
    expect(storefrontMessengerOffer({ customerNotifications: { messengerOffered: true, messengerTransports: ['WhatsApp', 'Max'], messengerLabel: 'Получать уведомления о заказе в WhatsApp и MAX' } })).toEqual({
      offered: true,
      transports: ['WhatsApp', 'Max'],
      checkboxLabel: 'Получать уведомления о заказе в WhatsApp и MAX',
    })
    expect(storefrontMessengerOffer({ customerNotifications: { messengerOffered: true } }).checkboxLabel).toBeNull()
  })
})
