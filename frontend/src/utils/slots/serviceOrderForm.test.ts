// @vitest-environment node
import { describe, it, expect } from 'vitest'
import type { ServiceQuoteDto } from '@/types/slots'
import { toCreateOrderInput, validateOrderFields } from './serviceOrderForm'

const quote = { ok: true, totalRub: 4600 } as ServiceQuoteDto
const pick = { businessDate: '2027-01-15', startMinute: 1320, hours: 3, quantities: { a: 0, b: 2 } }

describe('toCreateOrderInput', () => {
  it('orders against the quoted total and sends only chosen positions', () => {
    const body = toCreateOrderInput({
      pick,
      order: ['a', 'b'],
      guest: { name: ' Анна ', phone: '79001112233', comment: ' ', notifyByMessenger: false },
      anonymous: true,
      quote,
      idempotencyKey: 'k1',
      captchaToken: 'cap',
    })
    expect(body).toMatchObject({
      businessDate: '2027-01-15',
      startMinute: 1320,
      hours: 3,
      items: [{ itemId: 'b', quantity: 2 }],
      guestName: 'Анна',
      guestPhone: '79001112233',
      comment: null,
      notifyByMessenger: false,
      expectedTotalRub: 4600,
      idempotencyKey: 'k1',
      captchaToken: 'cap',
    })
  })

  it('does not send the phone or the captcha for a signed-in guest', () => {
    const body = toCreateOrderInput({
      pick,
      order: ['b'],
      guest: { name: 'Анна', phone: '79001112233', comment: 'ок', notifyByMessenger: true },
      anonymous: false,
      quote,
      idempotencyKey: 'k2',
      captchaToken: 'cap',
    })
    expect(body.guestPhone).toBeNull()
    expect(body.captchaToken).toBeNull()
    expect(body.comment).toBe('ок')
  })
})

describe('validateOrderFields', () => {
  it('uses the texts of the server (§39.31)', () => {
    const e = validateOrderFields({ name: '', phone: '123', comment: 'x'.repeat(501), notifyByMessenger: false }, { anonymous: true, captchaRequired: true, captchaToken: '' })
    expect(e.name).toBe('Укажите имя')
    expect(e.phone).toBe('Введите номер телефона в формате +7 (900) 000-00-00')
    expect(e.comment).toBe('Комментарий — не длиннее 500 символов')
    expect(e.captcha).toBe('Подтвердите, что вы не робот')
  })

  it('a signed-in guest needs neither phone nor captcha', () => {
    expect(validateOrderFields({ name: 'Анна', phone: '', comment: '', notifyByMessenger: false }, { anonymous: false, captchaRequired: true, captchaToken: '' })).toEqual({})
  })
})
